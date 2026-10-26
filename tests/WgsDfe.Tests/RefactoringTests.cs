using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application;
using WgsDfe.Application.Export;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Export;
using Xunit;
namespace WgsDfe.Tests;
public sealed class DatePeriodTests
{
 [Theory][InlineData("29/02/2024",true)][InlineData("29/02/2026",false)][InlineData("31/04/2026",false)][InlineData("00/01/2026",false)][InlineData("01/13/2026",false)][InlineData("20/10/2026",true)][InlineData("1/1/2026",false)][InlineData("01-01-2026",false)][InlineData("aa/bb/cccc",false)][InlineData("",true)][InlineData("29/02/2000",true)][InlineData("29/02/1900",false)]
 public void StrictDatesRespectCalendar(string value,bool valid)=>Assert.Equal(valid,DatePeriod.TryParse(value,out _));
}
public sealed partial class ExportTests
{
 private readonly Xunit.Abstractions.ITestOutputHelper output;
 public ExportTests(Xunit.Abstractions.ITestOutputHelper output){this.output=output;}
 [Fact]public async Task VolumeExportProcessesFiveHundredXmlsAndReportsProgress()
 {
  await using var h=await Harness.Create();for(int number=1;number<=500;number++)await h.Import(Doc(number:number));
  int progress=0;long memory=GC.GetTotalMemory(true);var timer=System.Diagnostics.Stopwatch.StartNew();var result=await h.Service.ExportAsync(h.Request(ExportFormat.Folder),new Callback(p=>progress=Math.Max(progress,p.Processed)));timer.Stop();
  Assert.False(result.Failed,result.Code);Assert.Equal(500,result.Written);Assert.Equal(500,progress);Assert.Equal(500,Directory.GetFiles(result.Output!,"*.xml",SearchOption.AllDirectories).Length);Assert.Empty(Directory.GetFiles(result.Output!,"*.xlsx",SearchOption.AllDirectories));
  output.WriteLine($"500 XMLs: {timer.ElapsedMilliseconds} ms; managed memory delta: {GC.GetTotalMemory(false)-memory} bytes. Synthetic small XMLs; this does not measure large production XMLs or UI/network latency.");
 }
 [Fact]public async Task RestoreRejectsCorruptionAndActiveInstanceWithoutReplacingDatabase()
 {
  string root=Path.Combine(Path.GetTempPath(),"wgs-restore-guards-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var org=Guid.NewGuid();var data=Path.Combine(root,"data");var backup=Path.Combine(root,"copy.wgsbackup");
  try
  {
   await using(var runtime=new FoundationRuntime(new FoundationOptions{Application=new(){OrganizationId=org,OrganizationName="Guard test",DataDirectory=data}}))
   {await runtime.InitializeAsync();await BackupService.CreateAsync(data,org,backup);await Assert.ThrowsAsync<IOException>(()=>BackupService.RestoreOfflineAsync(data,org,backup));}
   Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
   using(var zip=ZipFile.Open(backup,ZipArchiveMode.Update)){zip.GetEntry("database.sqlite")!.Delete();using var writer=new StreamWriter(zip.CreateEntry("database.sqlite").Open());writer.Write("CORRUPT");}
   byte[] before=await File.ReadAllBytesAsync(Path.Combine(data,"wgs-dfe.db"));await Assert.ThrowsAsync<InvalidDataException>(()=>BackupService.RestoreOfflineAsync(data,org,backup));Assert.Equal(before,await File.ReadAllBytesAsync(Path.Combine(data,"wgs-dfe.db")));
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
 }
 [Theory][InlineData(ExportFormat.Folder)][InlineData(ExportFormat.Zip)]
 public async Task CompanyExportsContainOnlyFiscalXmlForEachParticipant(ExportFormat format)
 {
  await using var h=await Harness.Create(twoCompanies:true);await h.Import(Doc());var result=await h.Service.ExportAsync(h.Request(format));Assert.False(result.Failed,result.Code);Assert.Equal(2,result.Written);
  if(format==ExportFormat.Zip){using var zip=ZipFile.OpenRead(result.Output!);Assert.All(zip.Entries,x=>Assert.EndsWith(".xml",x.FullName));}
  else Assert.All(Directory.GetFiles(result.Output!,"*",SearchOption.AllDirectories),x=>Assert.Equal(".xml",Path.GetExtension(x)));
 }
 [Fact]public async Task UnknownStatusCannotBeExportedAsAuthorized()
 {
  await using var h=await Harness.Create();await h.Import(DfeParserTests.Document(processed:false));
  var result=await h.Service.ExportAsync(h.Request());var row=Assert.Single(result.Rows);Assert.Contains("Não confirmados/",row.Path);Assert.Equal("Não confirmada",row.Details.KnownStatus);Assert.Equal("EXPORTED",row.Code);
 }
 [Fact]public async Task CancelledOriginalAndEventRemainTogether()
 {
  await using var h=await Harness.Create();await h.Import(Doc());await h.Import(Ev());var result=await h.Service.ExportAsync(h.Request());Assert.Equal(2,result.Written);Assert.All(result.Rows,x=>Assert.Contains("Cancelados/",x.Path));Assert.Contains("Cancelado",result.Rows.Single(x=>x.Kind=="Documento").Details.KnownStatus);
 }
 [Fact]public async Task ConsultationCompanyScopeNeverExportsAnotherCompanyCopy()
 {
  await using var h=await Harness.Create(twoCompanies:true);await h.Import(Doc());var result=await h.Service.ExportAsync(h.Request() with{Filter=new(CompanyIds:[h.Company],Models:[DocumentModel.NFe,DocumentModel.NFCe])});Assert.Single(result.Rows);Assert.Equal("12345678000195",result.Rows[0].Details.Cnpj);
 }
 [Fact]public async Task ExportReceiptAllowsLocalReexportWithoutOfficialService()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var first=await h.Service.ExportAsync(h.Request());var doc=await h.Db.FiscalDocuments.SingleAsync();File.Delete(Path.Combine(h.Data,doc.FileRelativePath));
  var second=await h.Service.ExportAsync(h.Request(ExportFormat.Folder));Assert.False(second.Failed,second.Code);Assert.Equal(Doc(),Encoding.UTF8.GetString(await Read(second,second.Rows[0].Path,ExportFormat.Folder)));Assert.NotEqual(first.Output,second.Output);
 }
 [Fact]public async Task RetentionOnlyDeletesVerifiedExportedEligibleCopiesAndKeepsMetadata()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var result=await h.Service.ExportAsync(h.Request(ExportFormat.Folder));var doc=await h.Db.FiscalDocuments.SingleAsync();
  var location=new ArchiveLocation(h.Data,h.Org);var store=new ExportReceiptStore(location);var receipt=(await store.FindAsync(doc.FileRelativePath,doc.XmlHash,default))!;
  await store.SaveAsync(receipt with{PublishedAtUtc=DateTime.UtcNow.AddDays(-31)},default);
  using var gate=new ArchiveGate();using var export=new ExportGate();var maintenance=new ArchiveRetention(h.Db,location,gate,export);var clean=await maintenance.CleanAsync(30);
  Assert.Equal(1,clean.Removed);Assert.False(File.Exists(Path.Combine(h.Data,doc.FileRelativePath)));Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.True(File.Exists(Path.Combine(result.Output!,result.Rows[0].Path)));Assert.Equal(1,(await h.Service.ExportAsync(h.Request())).Written);
 }
 [Theory][InlineData("unexported")][InlineData("recent")][InlineData("missing")][InlineData("changed")][InlineData("cancelled")][InlineData("conflict")]
 public async Task RetentionProtectsCopiesWithoutSufficientEvidence(string scenario)
 {
  await using var h=await Harness.Create();await h.Import(Doc());
  if(scenario=="cancelled")await h.Import(Ev());if(scenario=="conflict")await h.Import(Doc().Replace("123456789.01","2.00"));
  var doc=await h.Db.FiscalDocuments.SingleAsync();var location=new ArchiveLocation(h.Data,h.Org);
  if(scenario!="unexported")
  {
   var result=await h.Service.ExportAsync(h.Request(ExportFormat.Folder));var store=new ExportReceiptStore(location);var receipt=(await store.FindAsync(doc.FileRelativePath,doc.XmlHash,default))!;
   if(scenario!="recent")await store.SaveAsync(receipt with{PublishedAtUtc=DateTime.UtcNow.AddDays(-31)},default);
   if(scenario=="missing")Directory.Delete(result.Output!,true);
   if(scenario=="changed")await File.WriteAllTextAsync(Path.Combine(result.Output!,result.Rows[0].Path),"BROKEN");
  }
  using var gate=new ArchiveGate();using var export=new ExportGate();var clean=await new ArchiveRetention(h.Db,location,gate,export).CleanAsync(30);Assert.Equal(0,clean.Removed);Assert.True(File.Exists(Path.Combine(h.Data,doc.FileRelativePath)));
 }
 [Fact]public async Task BackupExcludesSecretsAndRestoresCompaniesAndNsu()
 {
  await using var h=await Harness.Create();await h.Import(Doc());h.Db.DocumentSyncStates.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,ProviderCode="SEF_SC_NFE",Model=DocumentModel.NFe,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,LastNsu="123"});await h.Db.SaveChangesAsync();
  // Runtime uses wgs-dfe.db; the export harness uses db.sqlite.
  await h.Db.Database.OpenConnectionAsync();
  using(var target=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=Path.Combine(h.Data,"wgs-dfe.db"),Pooling=false}.ToString()))
  {target.Open();((Microsoft.Data.Sqlite.SqliteConnection)h.Db.Database.GetDbConnection()).BackupDatabase(target);}
  await h.Db.Database.CloseConnectionAsync();
  var backup=Path.Combine(h.Root,"test.wgsbackup");Directory.CreateDirectory(Path.Combine(h.Data,"Certificates"));await File.WriteAllTextAsync(Path.Combine(h.Data,"Certificates","secret.pfx"),"NEVER COPY");
  await BackupService.CreateAsync(h.Data,h.Org,backup);using(var zip=ZipFile.OpenRead(backup)){Assert.DoesNotContain(zip.Entries,e=>e.FullName.Contains("pfx",StringComparison.OrdinalIgnoreCase));Assert.NotNull(zip.GetEntry("database.sqlite"));}
  var restored=Path.Combine(h.Root,"restored");await BackupService.RestoreOfflineAsync(restored,h.Org,backup);
  await using var db=WgsDfe.Database.DatabaseFactory.Create(Path.Combine(restored,"wgs-dfe.db"),h.Org);Assert.Equal("123",(await db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Equal(1,await db.Companies.CountAsync());Assert.Equal(1,await db.FiscalDocuments.CountAsync());Assert.False(db.Database.HasPendingModelChanges());
  await Assert.ThrowsAsync<InvalidDataException>(()=>BackupService.RestoreOfflineAsync(Path.Combine(h.Root,"foreign"),Guid.NewGuid(),backup));
 }
}
