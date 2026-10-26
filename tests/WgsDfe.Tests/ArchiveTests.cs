using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WgsDfe.Application.Archive;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Parsing;
using Xunit;
namespace WgsDfe.Tests;
public sealed class ArchiveTests
{
 [Theory][InlineData(55)][InlineData(57)][InlineData(62)][InlineData(65)][InlineData(67)]public async Task ImportFiveModels_PreservesBytesAndMetadata(int model)
 {
  await using var h=await Harness.Create();string xml=DfeParserTests.Document(model);string source=h.Xml(xml);var result=await h.Import(source);Assert.Equal(ArchiveOutcome.Imported,Assert.Single(result.Rows).Outcome);
  var row=Assert.Single((await h.Service.SearchAsync(new())).Items);Assert.Equal((DocumentModel)model,row.Model);Assert.Equal(123456789.01m,row.Total);Assert.Equal("2026-09-10",row.IssueDate);Assert.Contains("-03:00",row.OriginalDate);Assert.Equal(2,row.LinkedCompanies);
  var file=await h.Db.ArchiveFiles.SingleAsync();Assert.Equal(await File.ReadAllBytesAsync(source),await h.Store.ReadAsync(file.RelativePath,file.Hash,default));Assert.Contains("Homologacao/2026/09/12345678000195",file.RelativePath);Assert.Equal(DocumentStatus.Authorized,row.Status);
 }
 [Fact]public async Task DuplicateXml_NeverCreatesSecondDocumentOrFile()
 {await using var h=await Harness.Create();var path=h.Xml(DfeParserTests.Document());await h.Import(path);var second=await h.Import(path);Assert.Equal(ArchiveOutcome.Duplicate,Assert.Single(second.Rows).Outcome);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(1,await h.Db.ArchiveFiles.CountAsync());Assert.Single(h.Store.EnumeratePhysicalFiles());Assert.Equal(2,await h.Db.FiscalDocumentCompanies.CountAsync());}
 [Fact]public async Task RawThenProcessed_UpgradesButPreservesBothOriginals()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document(processed:false)));var result=await h.Import(h.Xml(DfeParserTests.Document()));Assert.Equal(ArchiveOutcome.Upgraded,Assert.Single(result.Rows).Outcome);var d=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal(DocumentStatus.Authorized,d.Status);Assert.NotNull(d.ProtocolNumber);Assert.Equal(2,await h.Db.ArchiveFiles.CountAsync());Assert.Equal(2,h.Store.EnumeratePhysicalFiles().Count());}
 [Fact]public async Task RawAfterProcessed_CannotDowngrade()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()));string hash=(await h.Db.FiscalDocuments.SingleAsync()).XmlHash;var result=await h.Import(h.Xml(DfeParserTests.Document(processed:false)));Assert.Equal(ArchiveOutcome.VariantStored,Assert.Single(result.Rows).Outcome);Assert.Equal(hash,(await h.Db.FiscalDocuments.SingleAsync()).XmlHash);Assert.Equal(DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task FormattingVariant_IsPreservedOnce()
 {await using var h=await Harness.Create();var original=h.Xml(DfeParserTests.Document());await h.Import(original);var formatted=h.Xml(XDocument.Parse(DfeParserTests.Document()).ToString());Assert.Equal(ArchiveOutcome.VariantStored,Assert.Single((await h.Import(formatted)).Rows).Outcome);Assert.Equal(ArchiveOutcome.Duplicate,Assert.Single((await h.Import(formatted)).Rows).Outcome);Assert.Equal(2,await h.Db.ArchiveFiles.CountAsync());}
 [Theory][InlineData("total")][InlineData("protocol")][InlineData("recipient")]
 public async Task DivergentVersion_IsQuarantinedWithoutOverwrite(string change)
 {
  await using var h=await Harness.Create();string xml=DfeParserTests.Document();await h.Import(h.Xml(xml));var old=await h.Db.FiscalDocuments.AsNoTracking().SingleAsync();
  string altered=change switch{"total"=>xml.Replace("123456789.01","100.00"),"protocol"=>xml.Replace("142260000000001","142260000000002"),_=>xml.Replace("<dest><CNPJ>12345678000276","<dest><CNPJ>12345678000195")};
  var result=await h.Import(h.Xml(altered));Assert.Equal(ArchiveOutcome.Conflict,Assert.Single(result.Rows).Outcome);h.Db.ChangeTracker.Clear();var current=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal(old.XmlHash,current.XmlHash);Assert.Equal(old.TotalValue,current.TotalValue);Assert.Equal(old.ProtocolNumber,current.ProtocolNumber);Assert.Equal(2,await h.Db.ArchiveFiles.CountAsync());Assert.Single(await h.Db.ArchiveFiles.Where(x=>x.IsConflict).ToListAsync());
 }
 [Fact]public async Task SameKey_DifferentEnvironment_IsSeparate()
 {await using var h=await Harness.Create();string xml=DfeParserTests.Document();await h.Import(h.Xml(xml),h.Xml(xml.Replace("<tpAmb>2","<tpAmb>1")));Assert.Equal(2,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(2,h.Store.EnumeratePhysicalFiles().Count());Assert.Single((await h.Service.SearchAsync(new(Environment:FiscalEnvironment.Production))).Items);}
 [Fact]public async Task EventsBeforeDocument_AttachAndReconcileDeclaredCancellation()
 {
  await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Event()));Assert.Null((await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(0,await h.Db.FiscalDocuments.CountAsync());
  await h.Import(h.Xml(DfeParserTests.Document()));var d=await h.Db.FiscalDocuments.SingleAsync();var ev=await h.Db.FiscalDocumentEvents.SingleAsync();Assert.Equal(d.Id,ev.FiscalDocumentId);Assert.Equal(DocumentStatus.Cancelled,d.Status);Assert.Equal(DocumentStatus.Authorized,d.DeclaredStatus);Assert.NotNull(d.CancellationDateUtc);Assert.Single((await h.Service.DetailAsync(d.Id)).Events);
 }
 [Theory][InlineData(55,2)][InlineData(57,2)][InlineData(62,3)][InlineData(65,2)][InlineData(67,3)]public async Task EventAfterDocument_AndExactDuplicate(int model,int width)
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document(model)));var ev=h.Xml(DfeParserTests.Event(model,width:width));await h.Import(ev);Assert.Equal(ArchiveOutcome.Duplicate,Assert.Single((await h.Import(ev)).Rows).Outcome);Assert.Equal(DocumentStatus.Cancelled,(await h.Db.FiscalDocuments.SingleAsync()).Status);Assert.Equal(1,await h.Db.FiscalDocumentEvents.CountAsync());Assert.Equal(2,await h.Db.ArchiveFiles.CountAsync());}
 [Theory][InlineData(136,true)][InlineData(135,false)]public async Task UnlinkedOrRawCancellation_CannotCancelDocument(int code,bool processed)
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Event(code:code,processed:processed)));Assert.Equal(DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task RawThenProcessedEvent_UpgradesAndCancels()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Event(processed:false)));var result=await h.Import(h.Xml(DfeParserTests.Event()));Assert.Equal(ArchiveOutcome.Upgraded,Assert.Single(result.Rows).Outcome);Assert.Equal(DocumentStatus.Cancelled,(await h.Db.FiscalDocuments.SingleAsync()).Status);Assert.Equal(3,await h.Db.ArchiveFiles.CountAsync());}
 [Fact]public async Task ConflictingEvent_CannotReplaceRegisteredCanonical()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Event(code:136)));var result=await h.Import(h.Xml(DfeParserTests.Event()));Assert.Equal(ArchiveOutcome.Conflict,Assert.Single(result.Rows).Outcome);Assert.Equal(136,(await h.Db.FiscalDocumentEvents.SingleAsync()).ResponseCode);Assert.Equal(DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task CartaCorrecao_IsSeparateAndDoesNotCancel()
 {await using var h=await Harness.Create();string xml=DfeParserTests.Event().Replace("110111","110110");await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(xml));Assert.Equal("110110",(await h.Db.FiscalDocumentEvents.SingleAsync()).EventTypeCode);Assert.Equal(DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task Summary_IsNeverStoredAsAvailableFullDocument()
 {await using var h=await Harness.Create();string xml=$"<resNFe xmlns='{DfeParser.NfeNamespace}' versao='1.01'><chNFe>{DfeParserTests.Key()}</chNFe><CNPJ>12345678000195</CNPJ><xNome>Empresa</xNome><dhEmi>2026-09-10T11:12:13-03:00</dhEmi><vNF>0.00</vNF><cSitNFe>1</cSitNFe><nProt>142260000000001</nProt><dhRecbto>2026-09-10T11:12:14-03:00</dhRecbto></resNFe>";var r=await h.Import(h.Xml(xml));Assert.Equal(ArchiveOutcome.SummarySkipped,Assert.Single(r.Rows).Outcome);Assert.Empty(await h.Db.ArchiveFiles.ToListAsync());Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.All(await h.Db.ProcessingJobItems.ToListAsync(),x=>Assert.NotEqual(Availability.Available,x.Availability));}
 [Fact]public async Task FolderAndMultipleZips_ParseContentAndIgnorePdf()
 {
  await using var h=await Harness.Create();string folder=Path.Combine(h.Source,"sub","folder");Directory.CreateDirectory(folder);await File.WriteAllTextAsync(Path.Combine(folder,"anything.xml"),DfeParserTests.Document());await File.WriteAllTextAsync(Path.Combine(folder,"note.pdf"),"irrelevante");
  string zip=h.Zip(("sub/random.xml",DfeParserTests.Document(57)),("note.pdf","not XML"));string zip2=h.Zip(("random.xml",DfeParserTests.Document(62)));var result=await h.Import(Path.Combine(h.Source,"sub"),zip,zip2);Assert.Equal(3,result.Imported);Assert.Equal(2,result.Rows.Count(x=>x.Outcome==ArchiveOutcome.Ignored));Assert.Equal(3,await h.Db.FiscalDocuments.CountAsync());
 }
 [Theory][InlineData("../escape.xml")][InlineData("/escape.xml")][InlineData("C:/escape.xml")][InlineData("safe/../../escape.xml")][InlineData("..\\escape.xml")][InlineData("safe.xml:stream")]
 public async Task UnsafeZip_RejectsEntirePackageBeforeAnyWrite(string entry)
 {await using var h=await Harness.Create();string zip=h.Zip(("valid.xml",DfeParserTests.Document()),(entry,DfeParserTests.Document(57)));var r=await h.Import(zip);Assert.Equal(ArchiveOutcome.UnsafeArchive,Assert.Single(r.Rows).Outcome);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Empty(h.Store.EnumeratePhysicalFiles());}
 [Fact]public async Task ZipSymlink_IsRejected()
 {await using var h=await Harness.Create();string path=Path.Combine(h.Source,"link.zip");using(var z=new ZipArchive(File.Create(path),ZipArchiveMode.Create)){var e=z.CreateEntry("fake.xml");e.ExternalAttributes=unchecked((int)0xA1FF0000);using var s=new StreamWriter(e.Open());s.Write(DfeParserTests.Document());}Assert.Equal(ArchiveOutcome.UnsafeArchive,Assert.Single((await h.Import(path)).Rows).Outcome);}
 [Fact]public async Task NestedZip_IsNotExpanded(){await using var h=await Harness.Create();var r=await h.Import(h.Zip(("inside.zip","not expanded"),("ok.xml",DfeParserTests.Document())));Assert.Equal(1,r.Imported);Assert.Contains(r.Rows,x=>x.Code=="NON_XML_OR_NESTED_ZIP");}
 [Fact]public async Task CorruptZipAndBadXml_DoNotStopOtherSources()
 {await using var h=await Harness.Create();string zip=Path.Combine(h.Source,"bad.zip");await File.WriteAllTextAsync(zip,"bad zip");var r=await h.Import(zip,h.Xml("<a>"),h.Xml(DfeParserTests.Document()));Assert.Equal(1,r.Imported);Assert.Contains(r.Rows,x=>x.Outcome==ArchiveOutcome.UnsafeArchive);Assert.Contains(r.Rows,x=>x.Outcome==ArchiveOutcome.Invalid);}
 [Fact]public async Task TamperedZipCrc_IsRejected()
 {await using var h=await Harness.Create();string zip=h.Zip(("a.xml",DfeParserTests.Document()));var bytes=await File.ReadAllBytesAsync(zip);int i=bytes.AsSpan().IndexOf(new byte[]{0x50,0x4b,0x01,0x02});Assert.True(i>=0);bytes[i+16]^=0xff;await File.WriteAllBytesAsync(zip,bytes);var r=await h.Import(zip);Assert.Equal(ArchiveOutcome.Invalid,Assert.Single(r.Rows).Outcome);Assert.Empty(await h.Db.ArchiveFiles.ToListAsync());}
 [Fact]public async Task ZipBombAndOversizedXml_AreBounded()
 {await using var h=await Harness.Create();string zip=h.Zip(("big.xml",new string('A',2*1024*1024)));Assert.Equal(ArchiveOutcome.LimitExceeded,Assert.Single((await h.Import(zip)).Rows).Outcome);string path=h.Xml(new string('A',DfeParser.MaximumBytes+1));Assert.Equal(ArchiveOutcome.LimitExceeded,Assert.Single((await h.Import(path)).Rows).Outcome);}
 [Fact]public async Task TooManyZipEntries_AreRejected()
 {await using var h=await Harness.Create();string path=Path.Combine(h.Source,"many.zip");using(var z=new ZipArchive(File.Create(path),ZipArchiveMode.Create))for(int i=0;i<10001;i++)z.CreateEntry(i+".xml");Assert.Equal(ArchiveOutcome.LimitExceeded,Assert.Single((await h.Import(path)).Rows).Outcome);}
 [Fact]public async Task OwnDataDirectory_IsNotReimported(){await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()));var r=await h.Import(h.Data);Assert.Equal(ArchiveOutcome.Ignored,Assert.Single(r.Rows).Outcome);Assert.Equal("OWN_DATA_DIRECTORY",r.Rows[0].Code);}
 [UnixFact]public async Task SourceSymlinks_AreNotFollowed()
 {await using var h=await Harness.Create();string actual=h.Xml(DfeParserTests.Document()),link=Path.Combine(h.Source,"link.xml");File.CreateSymbolicLink(link,actual);var r=await h.Import(link);Assert.Equal("LINK_SKIPPED",Assert.Single(r.Rows).Code);Assert.Empty(await h.Db.ArchiveFiles.ToListAsync());}
 [UnixFact]public async Task DestinationSymlink_CannotEscape()
 {await using var h=await Harness.Create();string outside=Path.Combine(h.Root,"outside");Directory.CreateDirectory(outside);Directory.CreateDirectory(Path.Combine(h.Data,"XML"));Directory.CreateSymbolicLink(Path.Combine(h.Data,"XML",h.Org.ToString("N")),outside);var r=await h.Import(h.Xml(DfeParserTests.Document()));Assert.True(r.Failed);Assert.Empty(Directory.GetFiles(outside,"*",SearchOption.AllDirectories));Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task MissingPhysicalXml_IsRestoredByExactReimport()
 {await using var h=await Harness.Create();var source=h.Xml(DfeParserTests.Document());await h.Import(source);var file=await h.Db.ArchiveFiles.SingleAsync();File.Delete(Path.Combine(h.Data,file.RelativePath));var check=await h.Service.CheckIntegrityAsync();Assert.Contains(check.Problems,x=>x.Code==nameof(ArchiveError.FileMissing));var r=await h.Import(source);Assert.Equal(ArchiveOutcome.Restored,Assert.Single(r.Rows).Outcome);Assert.Equal(1,(await h.Service.CheckIntegrityAsync()).Valid);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());}
 [Fact]public async Task LegacyCanonicalPath_CanBeReindexedWithoutChangingDeclaredStatus()
 {await using var h=await Harness.Create();string source=h.Xml(DfeParserTests.Document());await h.Import(source);h.Db.ArchiveFiles.Remove(await h.Db.ArchiveFiles.SingleAsync());var d=await h.Db.FiscalDocuments.SingleAsync();d.FileRelativePath="legacy.xml";await h.Db.SaveChangesAsync();var r=await h.Import(source);Assert.Equal(ArchiveOutcome.Restored,Assert.Single(r.Rows).Outcome);d=await h.Db.FiscalDocuments.SingleAsync();Assert.StartsWith("XML/",d.FileRelativePath);Assert.Equal(DocumentStatus.Authorized,d.DeclaredStatus);Assert.Equal(Availability.Available,(await h.Service.CheckAvailabilityAsync(d.AccessKey,d.Environment)).Availability);}
 [Fact]public async Task ArchiveImport_DoesNotAdvanceSynchronizationCursor()
 {await using var h=await Harness.Create();var cursor=new DocumentSyncState{OrganizationId=h.Org,CompanyId=h.Companies[0],ProviderCode="FUTURE",Model=DocumentModel.NFe,Environment=FiscalEnvironment.Homologation,LastNsu="125"};h.Db.DocumentSyncStates.Add(cursor);await h.Db.SaveChangesAsync();await h.Import(h.Xml(DfeParserTests.Document()));var saved=await h.Db.DocumentSyncStates.SingleAsync();Assert.Equal("125",saved.LastNsu);Assert.Null(saved.LastSuccessAtUtc);Assert.Equal(1,saved.Revision);}
 [Fact]public async Task ModifiedPhysicalXml_IsNeverOverwritten()
 {await using var h=await Harness.Create();var source=h.Xml(DfeParserTests.Document());await h.Import(source);var file=await h.Db.ArchiveFiles.SingleAsync();string path=Path.Combine(h.Data,file.RelativePath);await File.WriteAllTextAsync(path,"tampered");await Assert.ThrowsAsync<ArchiveException>(()=>h.Service.ReadXmlAsync(file.Id));var r=await h.Import(source);Assert.True(r.Failed);Assert.Equal("tampered",await File.ReadAllTextAsync(path));Assert.Contains((await h.Service.CheckIntegrityAsync()).Problems,x=>x.Code==nameof(ArchiveError.IntegrityMismatch));}
 [Fact]public async Task XmlPreview_VerifiesHashAndPreservesSource()
 {await using var h=await Harness.Create();string xml=DfeParserTests.Document();await h.Import(h.Xml(xml));var file=await h.Db.ArchiveFiles.SingleAsync();var view=await h.Service.ReadXmlAsync(file.Id);Assert.Contains("infNFe",view.Text);Assert.Equal(file.Hash,view.Hash);Assert.False(view.PreviewTruncated);Assert.Equal(Encoding.UTF8.GetBytes(xml),await h.Store.ReadAsync(file.RelativePath,file.Hash,default));}
 [Fact]public async Task DatabaseFailure_RollsBackIndexAndLeavesRecoverableOrphan()
 {
  await using var h=await Harness.Create();await h.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_archive BEFORE INSERT ON ArchiveFiles BEGIN SELECT RAISE(ABORT,'forced'); END;");
  var r=await h.Import(h.Xml(DfeParserTests.Document()));Assert.True(r.Failed);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Empty(await h.Db.ArchiveFiles.ToListAsync());Assert.Equal(1,(await h.Service.CheckIntegrityAsync()).Orphans);Assert.False(await h.Db.AuditLogs.AnyAsync(x=>x.Action==AuditAction.XmlImported));
  await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_archive;");Assert.Equal(1,(await h.Import(h.Xml(DfeParserTests.Document()))).Imported);Assert.Equal(0,(await h.Service.CheckIntegrityAsync()).Orphans);
 }
 [Fact]public async Task CancelAfterFirstCommit_PreservesProgress_AndReimportContinues()
 {await using var h=await Harness.Create();string a=h.Xml(DfeParserTests.Document()),b=h.Xml(DfeParserTests.Document(57));using var c=new CancellationTokenSource();var progress=new SyncProgress<ArchiveProgress>(_=>c.Cancel());var r=await h.Service.ImportAsync(new([a,b]),progress,c.Token);Assert.True(r.Cancelled);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(JobStatus.Cancelled,(await h.Db.ProcessingJobs.SingleAsync()).Status);var retry=await h.Import(a,b);Assert.Equal(1,retry.Imported);Assert.Equal(1,retry.Duplicates);Assert.Equal(2,await h.Db.FiscalDocuments.CountAsync());}
 [Fact]public async Task ConcurrentImports_AreSerializedAndIdempotent()
 {await using var h=await Harness.Create();string path=h.Xml(DfeParserTests.Document());await using var db2=DatabaseFactory.Create(h.DbPath,h.Org);var service2=new ArchiveService(db2,new DfeParser(),h.Store,h.Gate);var results=await Task.WhenAll(h.Import(path),service2.ImportAsync(new([path])));Assert.Equal(1,results.Sum(x=>x.Imported));Assert.Equal(1,results.Sum(x=>x.Duplicates));Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Single(await h.Db.ArchiveFiles.ToListAsync());}
 [Fact]public async Task Search_AllFilters_UseExactCentsAndLocalDate()
 {
  await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document().Replace("Empresa sintética","Ação São José")),h.Xml(DfeParserTests.Document(57)));
  Assert.Single((await h.Service.SearchAsync(new(Search:"acao sao jose"))).Items);
  Assert.Single((await h.Service.SearchAsync(new(CompanyId:h.Companies[0],Model:DocumentModel.NFe,Environment:FiscalEnvironment.Homologation,Status:DocumentStatus.Authorized,IssuerTaxId:"12.345.678/0001-95",RecipientTaxId:"12345678000276",Number:"123",Series:"1",From:new(2026,9,10),To:new(2026,9,10),Minimum:123456789.01m,Maximum:123456789.01m,Role:ActorFilter.Issuer))).Items);
  Assert.Empty((await h.Service.SearchAsync(new(Minimum:123456789.02m))).Items);Assert.Empty((await h.Service.SearchAsync(new(From:new(2026,9,11)))).Items);Assert.Empty((await h.Service.SearchAsync(new(CompanyId:h.Companies[0],Role:ActorFilter.Recipient))).Items);
 }
 [Fact]public async Task ConsultationPeriodFiltersSelectedCompaniesAndModelsWithoutDuplicates()
 {
  await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Document(57)));
  var query=new ArchiveQuery(CompanyIds:h.Companies,Models:[DocumentModel.NFe],From:new(2026,9,10),To:new(2026,9,10));
  var result=await h.Service.SearchAsync(query);Assert.Equal(1,result.Total);Assert.Single(result.Items);
  Assert.Empty((await h.Service.SearchAsync(query with{CompanyIds=[Guid.NewGuid()]})).Items);
  Assert.Empty((await h.Service.SearchAsync(query with{From=new(2026,9,11),To=new(2026,9,12)})).Items);
  Assert.Empty((await h.Service.SearchAsync(query with{From=null,To=new(2026,9,9)})).Items);
  Assert.Equal(2,(await h.Service.SearchAsync(query with{Models=[DocumentModel.NFe,DocumentModel.CTe]})).Total);
  await Assert.ThrowsAsync<ArchiveException>(()=>h.Service.SearchAsync(query with{CompanyIds=[]}));
 }
 [Fact]public async Task UnassignedDocuments_CanBeLinkedAfterCompanyRegistration()
 {await using var h=await Harness.Create(seedCompanies:false);await h.Import(h.Xml(DfeParserTests.Document()));Assert.Single((await h.Service.SearchAsync(new(CompanyId:Guid.Empty))).Items);h.Db.Companies.Add(new(){OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Nova"});await h.Db.SaveChangesAsync();Assert.Equal(1,await h.Service.RefreshLinksAsync());Assert.Empty((await h.Service.SearchAsync(new(CompanyId:Guid.Empty))).Items);Assert.Equal(0,await h.Service.RefreshLinksAsync());}
 [Fact]public async Task SelfInvoice_UsesOneBothRoleLink()
 {await using var h=await Harness.Create();string xml=DfeParserTests.Document().Replace("<dest><CNPJ>12345678000276","<dest><CNPJ>12345678000195");await h.Import(h.Xml(xml));var link=await h.Db.FiscalDocumentCompanies.SingleAsync();Assert.Equal(ActorFilter.Both,link.Role);Assert.Single((await h.Service.SearchAsync(new(CompanyId:h.Companies[0],Role:ActorFilter.Issuer))).Items);Assert.Single((await h.Service.SearchAsync(new(CompanyId:h.Companies[0],Role:ActorFilter.Recipient))).Items);}
 [Fact]public async Task InvalidFilters_AreRejected()
 {await using var h=await Harness.Create();foreach(var q in new[]{new ArchiveQuery(Page:0),new ArchiveQuery(Minimum:0.001m),new ArchiveQuery(From:new(2026,2,1),To:new(2026,1,1)),new ArchiveQuery(Minimum:2,Maximum:1),new ArchiveQuery(Number:"1e2"),new ArchiveQuery(Model:(DocumentModel)99)})await Assert.ThrowsAsync<ArchiveException>(()=>h.Service.SearchAsync(q));}
 [Fact]public async Task Pagination_IsStable()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Document(57)),h.Xml(DfeParserTests.Document(62)));var first=await h.Service.SearchAsync(new(PageSize:2));var second=await h.Service.SearchAsync(new(Page:2,PageSize:2));Assert.Equal(3,first.Total);Assert.Equal(2,first.Items.Count);Assert.Single(second.Items);Assert.Empty(first.Items.Select(x=>x.Id).Intersect(second.Items.Select(x=>x.Id)));}
 [Fact]public async Task OtherOrganization_CannotReadFilesOrIndexes()
 {await using var h=await Harness.Create();await h.Import(h.Xml(DfeParserTests.Document()),h.Xml(DfeParserTests.Event()));var file=await h.Db.ArchiveFiles.FirstAsync();Guid other=Guid.NewGuid();await using var db=DatabaseFactory.Create(h.DbPath,other);db.Organizations.Add(new(){Id=other,Name="Outra"});await db.SaveChangesAsync();var store=new ArchiveFileStore(new(h.Data,other));using var gate=new ArchiveGate();var service=new ArchiveService(db,new DfeParser(),store,gate);Assert.Empty((await service.SearchAsync(new())).Items);Assert.Empty((await service.SearchEventsAsync("")).Items);Assert.Equal(ArchiveError.NotFound,(await Assert.ThrowsAsync<ArchiveException>(()=>service.ReadXmlAsync(file.Id))).Code);Assert.Equal(ArchiveError.UnsafePath,(await Assert.ThrowsAsync<ArchiveException>(()=>store.ReadAsync(file.RelativePath,file.Hash,default))).Code);}
 [Fact]public async Task CrashRecovery_MarksOnlyArchiveJobs_AndDoesNotRepeat()
 {await using var h=await Harness.Create();var job=new ProcessingJob{OrganizationId=h.Org,Kind=JobKind.Import,Status=JobStatus.Running};var other=new ProcessingJob{OrganizationId=h.Org,Kind=JobKind.Import,Status=JobStatus.Running};h.Db.ProcessingJobs.AddRange(job,other);h.Db.AuditLogs.Add(new(){OrganizationId=h.Org,Action=AuditAction.ArchiveImportStarted,EntityId=job.Id,ResultCode="ARCHIVE_STARTED"});await h.Db.SaveChangesAsync();await ArchiveMaintenance.InitializeAsync(h.Db);Assert.Equal(JobStatus.Failed,job.Status);Assert.Equal("ARCHIVE_INTERRUPTED",job.LastErrorCode);Assert.Equal(JobStatus.Running,other.Status);await ArchiveMaintenance.InitializeAsync(h.Db);Assert.Equal(1,await h.Db.AuditLogs.CountAsync(x=>x.ResultCode=="ARCHIVE_INTERRUPTED"));}
 [Fact]public async Task AvailabilityChecksPhysicalBytesAndEnvironment()
 {await using var h=await Harness.Create();string key=DfeParserTests.Key();Assert.Equal(Availability.Pending,(await h.Service.CheckAvailabilityAsync(key,FiscalEnvironment.Homologation)).Availability);await h.Import(h.Xml(DfeParserTests.Document()));Assert.Equal(Availability.Available,(await h.Service.CheckAvailabilityAsync(key,FiscalEnvironment.Homologation)).Availability);Assert.Equal(Availability.Pending,(await h.Service.CheckAvailabilityAsync(key,FiscalEnvironment.Production)).Availability);var f=await h.Db.ArchiveFiles.SingleAsync();string path=Path.Combine(h.Data,f.RelativePath);File.Delete(path);Assert.Equal(Availability.Pending,(await h.Service.CheckAvailabilityAsync(key,FiscalEnvironment.Homologation)).Availability);await File.WriteAllTextAsync(path,"tampered");Assert.Equal(Availability.TemporaryError,(await h.Service.CheckAvailabilityAsync(key,FiscalEnvironment.Homologation)).Availability);Assert.Equal(Availability.InvalidKey,(await h.Service.CheckAvailabilityAsync("bad",FiscalEnvironment.Homologation)).Availability);}
 [Fact]public async Task CsvReport_NeutralizesFormulas()
 {await using var h=await Harness.Create();var r=new ArchiveImportResult(Guid.NewGuid(),new[]{new ArchiveImportRow(1,"=HYPERLINK(\"evil\")",ArchiveOutcome.Invalid,null,"INVALID")},false,false);string path=Path.Combine(h.Root,"report.csv");await ArchiveReport.SaveAsync(r,path);Assert.Contains("\"'=HYPERLINK",await File.ReadAllTextAsync(path));}
 [Fact]public async Task UpgradePhase4_PreservesDataAndReindexesExactLargeDecimal()
 {
  string root=Path.Combine(Path.GetTempPath(),"wgs-upgrade5-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);Guid org=Guid.NewGuid(),id=Guid.NewGuid();string path=Path.Combine(root,"test.db");
  try
  {
   await using(var db=DatabaseFactory.Create(path,org))
   {
    await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261007234620_CertificatePurpose");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Organizations (Id,Name,CreatedAtUtc) VALUES ({org},{"Teste"},{DateTime.UtcNow})");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO FiscalDocuments (Id,OrganizationId,CreatedAtUtc,UpdatedAtUtc,Revision,AccessKey,Model,Environment,Status,FileRelativePath,XmlHash,SourceProviderCode,RetrievedAtUtc,TotalValue,IssuerName,IssueDateUtc) VALUES ({id},{org},{DateTime.UtcNow},{DateTime.UtcNow},{1},{DfeParserTests.Key()},{55},{2},{1},{"legacy.xml"},{"legacyhash"},{"LEGACY"},{DateTime.UtcNow},{"999999999999999.99"},{"São José"},{new DateTime(2026,9,10,15,0,0,DateTimeKind.Utc)})");
   }
   await using(var db=DatabaseFactory.Create(path,org)){await db.Database.MigrateAsync();await ArchiveMaintenance.InitializeAsync(db);var d=await db.FiscalDocuments.SingleAsync();Assert.Equal(id,d.Id);Assert.Equal(999999999999999.99m,d.TotalValue);Assert.Equal(99999999999999999L,d.TotalCents);Assert.Contains("SAO JOSE",d.SearchText);Assert.Equal(DocumentStatus.Authorized,d.DeclaredStatus);Assert.Equal("legacy.xml",d.FileRelativePath);Assert.Equal("2026-09-10",d.IssueLocalDate);Assert.False(db.Database.HasPendingModelChanges());}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
 }
 [Fact]public async Task FinancialSummary_CalculatesGrossCancelledAndNetAccurately()
 {
  await using var h=await Harness.Create();
  await h.Import(h.Xml(DfeParserTests.Document(55)));
  await h.Import(h.Xml(DfeParserTests.Document(57)));
  await h.Import(h.Xml(DfeParserTests.Event(57)));
  var summary=await h.Service.FinancialSummaryAsync(new());
  Assert.Equal(2,summary.TotalDocuments);
  Assert.Equal(1,summary.AuthorizedCount);
  Assert.Equal(123456789.01m,summary.GrossAuthorized);
  Assert.Equal(1,summary.CancelledCount);
  Assert.Equal(123456789.01m,summary.TotalCancelled);
  Assert.Equal(0.00m,summary.NetAmount);
 }
 [Fact]
 public async Task ReadOriginalXml_ReturnsExactOriginalBytesAndFilename()
 {
  await using var h=await Harness.Create();
  string rawXml=DfeParserTests.Document(55);
  await h.Import(h.Xml(rawXml));
  var doc=await h.Db.FiscalDocuments.FirstAsync();
  var file=await h.Db.ArchiveFiles.FirstAsync(f=>f.FiscalDocumentId==doc.Id);
  var original=await h.Service.ReadOriginalXmlAsync(file.Id);
  Assert.NotNull(original);
  Assert.Equal(new UTF8Encoding(false).GetBytes(rawXml),original.Bytes);
  Assert.Equal(doc.AccessKey+".xml",original.FileName);
  Assert.Equal(doc.XmlHash,original.Hash);

  await Assert.ThrowsAsync<ArchiveException>(()=>h.Service.ReadOriginalXmlAsync(Guid.NewGuid()));
 }
 private sealed class SyncProgress<T>(Action<T> action):IProgress<T>{public void Report(T value)=>action(value);}
 private sealed class Harness:IAsyncDisposable
 {
  public string Root{get;}=Path.Combine(Path.GetTempPath(),"wgs-archive-tests-"+Guid.NewGuid().ToString("N"));public string Source=>Path.Combine(Root,"source");public string Data=>Path.Combine(Root,"data");public string DbPath=>Path.Combine(Data,"test.db");public Guid Org{get;}=Guid.NewGuid();public WgsDbContext Db{get;private set;}=null!;public ArchiveGate Gate{get;}=new();public ArchiveFileStore Store{get;private set;}=null!;public ArchiveService Service{get;private set;}=null!;public Guid[] Companies{get;private set;}=[];
  public static async Task<Harness> Create(bool seedCompanies=true)
  {var h=new Harness();Directory.CreateDirectory(h.Source);Directory.CreateDirectory(h.Data);h.Db=DatabaseFactory.Create(h.DbPath,h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Teste"});if(seedCompanies){var a=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="A"};var b=new Company{OrganizationId=h.Org,Cnpj="12345678000276",LegalName="B"};h.Db.Companies.AddRange(a,b);h.Companies=[a.Id,b.Id];}await h.Db.SaveChangesAsync();h.Store=new(new(h.Data,h.Org));h.Service=new(h.Db,new DfeParser(),h.Store,h.Gate);return h;}
  public string Xml(string xml){string path=Path.Combine(Source,Guid.NewGuid().ToString("N")+".xml");File.WriteAllText(path,xml,new UTF8Encoding(false));return path;}
  public string Zip(params (string Name,string Content)[] files)
  {string path=Path.Combine(Source,Guid.NewGuid().ToString("N")+".zip");using var zip=new ZipArchive(File.Create(path),ZipArchiveMode.Create);foreach(var file in files){var entry=zip.CreateEntry(file.Name,CompressionLevel.Optimal);using var writer=new StreamWriter(entry.Open(),new UTF8Encoding(false));writer.Write(file.Content);}return path;}
  public Task<ArchiveImportResult> Import(params string[] paths)=>Service.ImportAsync(new(paths));
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();Gate.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(Root,true);}
 }
}
public sealed class UnixFactAttribute:FactAttribute{public UnixFactAttribute(){if(OperatingSystem.IsWindows())Skip="Teste de symlink executado em Unix; homologar reparse points no Windows.";}}
