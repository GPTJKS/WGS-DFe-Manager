using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using Xunit;
namespace WgsDfe.Tests;
public sealed partial class ExportTests
{
 private static string Dated(int model,int number,string day)
 {
  string ym=DateOnly.Parse(day).ToString("yyMM");string old=DfeParserTests.Key(model,number:number.ToString("D9")),next=DfeParserTests.Key(model,number:number.ToString("D9"),ym:ym);
  return Doc(model,number).Replace(old,next).Replace($"<cDV>{old[^1]}</cDV>",$"<cDV>{next[^1]}</cDV>").Replace("2026-09-10",day);
 }
 private static ExportRequest DayRequest(Harness h,string from,string? to=null)=>h.Request(ExportFormat.Folder) with{Filter=new(From:DateOnly.Parse(from),To:DateOnly.Parse(to??from))};
 private static void OnlyReadableXml(string root)
 {
  Assert.All(Directory.GetFiles(root,"*",SearchOption.AllDirectories),file=>Assert.Equal(".xml",Path.GetExtension(file)));
  Assert.All(Directory.GetDirectories(root,"*",SearchOption.AllDirectories),folder=>{string name=Path.GetFileName(folder);Assert.False(name.StartsWith("WGS_DFe_",StringComparison.OrdinalIgnoreCase));Assert.False(Guid.TryParse(name,out _));Assert.DoesNotMatch("^[A-Fa-f0-9]{32,64}$",name);});
 }
 [Fact]public async Task OneDayNfceHasExactReadableHierarchyAndOriginalBytes()
 {
  await using var h=await Harness.Create();var firm=await h.Db.Companies.SingleAsync();firm.LegalName="Sorvetes São João";await h.Db.SaveChangesAsync();string xml=Dated(65,123,"2026-10-01");await h.Import(xml);
  var result=await h.Service.ExportAsync(DayRequest(h,"2026-10-01"));Assert.False(result.Failed,result.Code);var row=Assert.Single(result.Rows);
  Assert.Equal($"Sorvetes São João/Autorizados/2026/10 - Outubro/01-10-2026/NFC-e/Produção/{row.Key}.xml",row.Path);
  Assert.Equal(h.Output,result.Output);Assert.Equal(Encoding.UTF8.GetBytes(xml),await File.ReadAllBytesAsync(Path.Combine(h.Output,row.Path)));OnlyReadableXml(h.Output);
 }
 [Fact]public async Task TwoDaysSeparateAuthorizedAndCancelledWithoutExtraLevels()
 {
  await using var h=await Harness.Create();await h.Import(Dated(65,123,"2026-10-01"));await h.Import(Dated(65,124,"2026-10-02"));
  string key=DfeParserTests.Key(65,number:"000000124",ym:"2610");await h.Import(Ev(65).Replace(DfeParserTests.Key(65),key).Replace("2026-09-11","2026-10-03"));
  var result=await h.Service.ExportAsync(DayRequest(h,"2026-10-01","2026-10-02"));Assert.False(result.Failed,result.Code);
  Assert.Contains(result.Rows,x=>x.Path.Contains("/Autorizados/"));Assert.Contains(result.Rows,x=>x.Path.Contains("/Cancelados/"));
  Assert.All(result.Rows,x=>{Assert.Contains("/2026/10 - Outubro/01-10-2026 a 02-10-2026/NFC-e/Produção/",x.Path);Assert.Equal(8,x.Path.Split('/').Length);});OnlyReadableXml(h.Output);
 }
 [Fact]public async Task CrossingYearsUsesEachEmissionMonthAndTheCompleteRequestedInterval()
 {
  await using var h=await Harness.Create();await h.Import(Dated(65,123,"2025-12-31"));await h.Import(Dated(65,124,"2026-01-01"));
  var result=await h.Service.ExportAsync(DayRequest(h,"2025-12-31","2026-01-01"));Assert.False(result.Failed,result.Code);Assert.Equal(2,result.Written);
  Assert.Contains(result.Rows,x=>x.Path.Contains("/2025/12 - Dezembro/31-12-2025 a 01-01-2026/"));Assert.Contains(result.Rows,x=>x.Path.Contains("/2026/01 - Janeiro/31-12-2025 a 01-01-2026/"));OnlyReadableXml(h.Output);
 }
 [Fact]public async Task EqualCompanyNamesUseOnlyTheNecessaryReadableTaxSuffix()
 {
  await using var h=await Harness.Create(twoCompanies:true);foreach(var firm in await h.Db.Companies.ToListAsync())firm.LegalName="Empresa Iguais";await h.Db.SaveChangesAsync();await h.Import(Doc());
  var result=await h.Service.ExportAsync(DayRequest(h,"2026-09-10"));Assert.Equal(2,result.Written);Assert.Contains(result.Rows,x=>x.Path.StartsWith("Empresa Iguais (0195)/"));Assert.Contains(result.Rows,x=>x.Path.StartsWith("Empresa Iguais (0276)/"));OnlyReadableXml(h.Output);
 }
 [Theory][InlineData("  São   João: Comércio?.  ","São João Comércio")][InlineData("CON","Empresa CON")][InlineData("LPT1","Empresa LPT1")][InlineData("Café / Açúcar *","Café Açúcar")][InlineData("AUX.txt","Empresa AUX.txt")]
 public async Task WindowsNamesAreReadableAndSafe(string name,string expected)
 {
  await using var h=await Harness.Create();var company=await h.Db.Companies.SingleAsync();company.LegalName=name;await h.Db.SaveChangesAsync();await h.Import(Doc());var result=await h.Service.ExportAsync(DayRequest(h,"2026-09-10"));Assert.False(result.Failed,result.Code);Assert.StartsWith(expected+"/",Assert.Single(result.Rows).Path);OnlyReadableXml(h.Output);
 }
 [Fact]public async Task IdenticalReexportSkipsWithoutChangingAnyFileOrCreatingDuplicates()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var request=DayRequest(h,"2026-09-10");var first=await h.Service.ExportAsync(request);var path=Path.Combine(h.Output,first.Rows[0].Path);var before=await File.ReadAllBytesAsync(path);var modified=File.GetLastWriteTimeUtc(path);
  var second=await h.Service.ExportAsync(request);Assert.Equal(0,second.Written);Assert.Equal(1,second.Skipped);Assert.Equal("ALREADY_EXISTS",Assert.Single(second.Rows).Code);Assert.Equal(before,await File.ReadAllBytesAsync(path));Assert.Equal(modified,File.GetLastWriteTimeUtc(path));Assert.Single(Directory.GetFiles(h.Output,"*.xml",SearchOption.AllDirectories));OnlyReadableXml(h.Output);
 }
 [Fact]public async Task DivergentExistingFileIsPreservedAndReportedAsActionableConflict()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var request=DayRequest(h,"2026-09-10");var first=await h.Service.ExportAsync(request);string path=Path.Combine(h.Output,first.Rows[0].Path);await File.WriteAllTextAsync(path,"SYNTHETIC DIVERGENT");
  var second=await h.Service.ExportAsync(request);Assert.Equal("EXPORT_WARNINGS",second.Code);Assert.Equal("EXPORT_DESTINATION_CONFLICT",Assert.Single(second.Rows).Code);Assert.Equal(0,second.Written);Assert.Equal("SYNTHETIC DIVERGENT",await File.ReadAllTextAsync(path));OnlyReadableXml(h.Output);
 }
 [Fact]public async Task CancellationKeepsPreexistingAndAlreadyCompletedXmlsAndRemovesTemporaryFiles()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var request=DayRequest(h,"2026-09-10");var first=await h.Service.ExportAsync(request);string existing=Path.Combine(h.Output,first.Rows[0].Path);byte[] before=await File.ReadAllBytesAsync(existing);await h.Import(Doc(number:124));await h.Import(Doc(number:125));using var cts=new CancellationTokenSource();
  var result=await h.Service.ExportAsync(request,new Callback(p=>{if(p.Code=="EXPORT_COPYING"&&p.Written==1)cts.Cancel();}),cts.Token);
  Assert.True(result.Cancelled);Assert.Equal(before,await File.ReadAllBytesAsync(existing));Assert.Equal(2,Directory.GetFiles(h.Output,"*.xml",SearchOption.AllDirectories).Length);Assert.Contains(result.Rows,x=>x.Code=="NOT_PROCESSED");OnlyReadableXml(h.Output);
 }
 [Fact]public async Task NearWindowsPathLimitShortensCompanyReadablyAndNeverChangesFiscalFilename()
 {
  await using var h=await Harness.Create();var firm=await h.Db.Companies.SingleAsync();firm.LegalName="Empresa de Comércio e Distribuição "+new string('A',90);await h.Db.SaveChangesAsync();await h.Import(Doc());string destination=Path.Combine(h.Output,new string('d',55));Directory.CreateDirectory(destination);
  var result=await h.Service.ExportAsync(DayRequest(h,"2026-09-10") with{Destination=destination});Assert.False(result.Failed,result.Code);var row=Assert.Single(result.Rows);Assert.True(Path.Combine(destination,row.Path).Length<=259);Assert.StartsWith("Empresa",row.Path);Assert.EndsWith(row.Key+".xml",row.Path);OnlyReadableXml(destination);
 }
 [Fact]public async Task DirectFolderExportRejectsAncestorsOfApplicationStorage()
 {
  await using var h=await Harness.Create();await h.Import(Doc());var result=await h.Service.ExportAsync(DayRequest(h,"2026-09-10") with{Destination=h.Root});Assert.Equal("EXPORT_DESTINATION_INVALID",result.Code);Assert.Equal(0,result.Written);Assert.False(Directory.Exists(Path.Combine(h.Root,"Synthetic")));
 }
}
