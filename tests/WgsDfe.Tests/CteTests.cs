using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Cte;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Cte;
using WgsDfe.Infrastructure.Nfe;
using WgsDfe.Infrastructure.Parsing;
using WgsDfe.Providers.Cte;
using Xunit;
namespace WgsDfe.Tests;
public sealed class CteTests
{
 private const string Tax="12345678000276";
 private static string Doc(int model=57)=>DfeParserTests.Document(model).Replace("<tpAmb>2","<tpAmb>1");
 private static string Event(int model=57)=>DfeParserTests.Event(model).Replace("<tpAmb>2","<tpAmb>1");
 private static DocumentRequest Request(string nsu="0",string? point=null)=>new(Guid.NewGuid(),Guid.NewGuid(),DocumentModel.CTe,FiscalEnvironment.Production,ActorFilter.Either,PayloadMode.FullXml,nsu,null,null,null,Guid.NewGuid(),Tax,point);
 private static string Zip(string xml){using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionMode.Compress,true))gzip.Write(Encoding.UTF8.GetBytes(xml));return Convert.ToBase64String(output.ToArray());}
 private static byte[] Page(int status=138,string next="1",string? max=null,params (string Nsu,string Xml,string Schema)[] payloads)
 {
  if(payloads.Length==0&&status==138)payloads=[(next,Doc(),"procCTe_v4.00.xsd")];
  string entries=string.Concat(payloads.Select(x=>$"<docZip NSU='{x.Nsu}' schema='{x.Schema}'>{Zip(x.Xml)}</docZip>"));
  return Soap($"<retDistDFeInt xmlns='{CteWireCodec.Namespace}' versao='1.00'><tpAmb>1</tpAmb><verAplic>Teste</verAplic><cStat>{status}</cStat><xMotivo>Resposta sintética</xMotivo><dhResp>2026-10-16T10:00:00-03:00</dhResp><ultNSU>{next}</ultNSU><maxNSU>{max??next}</maxNSU>{(entries.Length>0?$"<loteDistDFeInt>{entries}</loteDistDFeInt>":"")}</retDistDFeInt>");
 }
 private static byte[] Soap(string ret)=>Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s='http://www.w3.org/2003/05/soap-envelope'><s:Body><cteDistDFeInteresseResponse xmlns='{CteWireCodec.WsNamespace}'><cteDistDFeInteresseResult>{ret}</cteDistDFeInteresseResult></cteDistDFeInteresseResponse></s:Body></s:Envelope>");
 [Theory][InlineData(1)][InlineData(2)]public void Build_UsesOfficialEndpointAndSchema(int env)
 {
  var request=Request() with{Environment=(FiscalEnvironment)env};var wire=CteWireCodec.Build(request,Tax,"42");Assert.True(wire.Soap12);Assert.Equal(env==1?CteWireCodec.Production:CteWireCodec.Homologation,wire.Endpoint.AbsoluteUri);Assert.EndsWith("/cteDistDFeInteresse",wire.SoapAction);
  var xml=XDocument.Parse(Encoding.UTF8.GetString(wire.Body));XNamespace ns=CteWireCodec.Namespace;var payload=xml.Descendants(ns+"distDFeInt").Single();
  string path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../docs/sources/phase7/schemas/PL_CTeDistDFe_100/distDFeInt_v1.00.xsd"));var schema=new XmlSchemaSet{XmlResolver=new System.Xml.XmlUrlResolver()};schema.Add(ns.NamespaceName,path);new XDocument(payload).Validate(schema,(_,e)=>throw new Exception(e.Message));
  Assert.Contains("000000000000000",Encoding.UTF8.GetString(wire.Body));Assert.DoesNotContain(xml.Descendants(),x=>x.Name.LocalName is "mod" or "indAtor" or "chCTe" or "dIni");
 }
 [Fact]public void ConsNsu_HasOnePointAndNoDistributionCursor()
 {var wire=CteWireCodec.Build(Request("99","42"),Tax,"42");var xml=Encoding.UTF8.GetString(wire.Body);Assert.Contains("<consNSU>",xml);Assert.Contains("000000000000042",xml);Assert.DoesNotContain("ultNSU",xml);}
 [Theory][InlineData("")][InlineData("0")][InlineData("-1")][InlineData("1000000000000000")][InlineData("١٢")][InlineData("1e5")]
 public void InvalidPointNsu_IsRejected(string value)=>Assert.Throws<NfeException>(()=>CteWireCodec.Build(Request(point:value),Tax,"42"));
 [Fact]public void KeyLookup_IsNotInvented()=>Assert.Throws<NfeException>(()=>CteWireCodec.Build(Request() with{AccessKey=DfeParserTests.Key(57)},Tax,"42"));
 [Theory][InlineData(55)][InlineData(65)][InlineData(62)]public void UnsupportedRequestedModel_IsRejected(int model)=>Assert.Throws<NfeException>(()=>CteWireCodec.Build(Request() with{Model=(DocumentModel)model},Tax,"42"));
 [Fact]public void SoapEnvelopeAndBytes_AreDecoded()
 {var page=CteWireCodec.Decode(Page(),Request());Assert.Equal(138,page.Status);Assert.Equal("1",page.NextNsu);Assert.False(page.HasMore);Assert.Equal(Encoding.UTF8.GetBytes(Doc()),Assert.Single(page.Items).Xml);Assert.Equal(TimeSpan.FromHours(1),page.Delay);}
 [Fact]public async Task AutomaticModeContinuesBeyondTenCteBatches()
 {
  await using var h=await Harness.Create();for(int n=1;n<=12;n++)h.Transport.Add(Page(next:n.ToString(),max:"12"));
  var result=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,MaxPages:0));var row=Assert.Single(result.Rows);
  Assert.Equal("CTE_SYNCHRONIZED",row.Code);Assert.Equal(12,row.Pages);Assert.Equal("12",row.Nsu);Assert.Equal(12,h.Transport.Calls);
 }
 [Fact]public void MorePages_ContinueWithoutHourlyWait(){var page=CteWireCodec.Decode(Page(max:"2"),Request());Assert.True(page.HasMore);Assert.Equal(TimeSpan.Zero,page.Delay);}
 [Fact]public void PointQuery_DoesNotChangeDistributionCursor(){var page=CteWireCodec.Decode(Page(next:"42"),Request("99","42"));Assert.Null(page.NextNsu);Assert.False(page.HasMore);}
 [Fact]public void WrongPointNsu_IsRejected()=>Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Page(next:"41"),Request("99","42")));
 [Theory][InlineData("namespace")][InlineData("environment")][InlineData("version")][InlineData("duplicate-status")][InlineData("cursor")][InlineData("max")][InlineData("soap-version")]
 public void InvalidResponse_IsRejected(string change)
 {
  string xml=Encoding.UTF8.GetString(Page());xml=change switch{"namespace"=>xml.Replace(CteWireCodec.Namespace,"urn:wrong"),"environment"=>xml.Replace("<tpAmb>1","<tpAmb>2"),"version"=>xml.Replace("versao='1.00'","versao='2.00'"),"duplicate-status"=>xml.Replace("<cStat>138</cStat>","<cStat>138</cStat><cStat>137</cStat>"),"cursor"=>xml.Replace("<ultNSU>1","<ultNSU>0"),"max"=>xml.Replace("<maxNSU>1","<maxNSU>0"),_=>xml.Replace("http://www.w3.org/2003/05/soap-envelope","http://schemas.xmlsoap.org/soap/envelope/")};Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Encoding.UTF8.GetBytes(xml),Request()));
 }
 [Theory][InlineData(108)][InlineData(109)][InlineData(280)][InlineData(593)][InlineData(656)]public void Rejection_DoesNotReturnCursor(int status)
 {var page=CteWireCodec.Decode(Page(status,"99"),Request());Assert.Null(page.NextNsu);Assert.Empty(page.Items);Assert.Equal(TimeSpan.FromHours(1),page.Delay);}
 [Fact]public void XmlDtd_IsRejected()=>Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Encoding.UTF8.GetBytes("<!DOCTYPE a [<!ENTITY s SYSTEM 'file:///etc/passwd'>]><a>&s;</a>"),Request()));
 [Fact]public void ExpandedGzip_IsBounded()=>Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Page(138,"1",null,("1",new string('x',10*1024*1024+1),"procCTe_v4.00.xsd")),Request()));
 [Fact]public void CorruptGzipCrc_IsRejected()
 {var x=XDocument.Parse(Encoding.UTF8.GetString(Page()));var entry=x.Descendants().Single(x=>x.Name.LocalName=="docZip");var b=Convert.FromBase64String(entry.Value);b[^8]^=1;entry.Value=Convert.ToBase64String(b);Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Encoding.UTF8.GetBytes(x.ToString()),Request()));}
 [Fact]public void MalformedXmlInUnknownSchema_IsStillRejected()=>Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Page(138,"1",null,("1","<unknown>","unknown_v1.00.xsd")),Request()));
 [Fact]public void MoreThanFifty_IsRejected()
 {var entries=Enumerable.Range(1,51).Select(n=>(n.ToString(),"<unknown/>","unknown.xsd")).ToArray();Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Page(138,"51",null,entries),Request()));}
 [Fact]public void NsuOrderCannotRegress()=>Assert.Throws<NfeException>(()=>CteWireCodec.Decode(Page(138,"2",null,("2",Doc(),"procCTe_v4.00.xsd"),("1",Doc(),"procCTe_v4.00.xsd")),Request()));
 [Theory][InlineData(57)][InlineData(67)]public async Task Sync_ArchivesBothModelsAndKeepsOriginalDocZip(int model)
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1",Doc(model),model==57?"procCTe_v4.00.xsd":"procCTeOS_v4.00.xsd")));var r=await h.Run();Assert.Equal(1,Assert.Single(r.Rows).Archived);Assert.Equal(0,r.Rows[0].Retained);var doc=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal((DocumentModel)model,doc.Model);Assert.Equal(CteSource.Code,doc.SourceProviderCode);var file=await h.Db.ArchiveFiles.SingleAsync();Assert.Equal(Encoding.UTF8.GetBytes(Doc(model)),await h.Files.ReadAsync(file.RelativePath,file.Hash,default));Assert.True((await h.Db.CteDistributionReceipts.SingleAsync()).Applied);Assert.True((await h.Db.CteDistributionItems.SingleAsync()).Archived);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task Mixed57And67_UseOneStreamAndDeferredSelectionIsRecoverableOffline()
 {
  await using var h=await Harness.Create();h.Transport.Add(Page(138,"2",null,("1",Doc(),"procCTe_v4.00.xsd"),("2",Doc(67),"procCTeOS_v4.00.xsd")));var result=await h.Run(EnabledDocuments.CTe);Assert.Equal(1,Assert.Single(result.Rows).Archived);Assert.Equal(1,result.Rows[0].Retained);Assert.Equal("2",result.Rows[0].Nsu);Assert.Single(await h.Db.DocumentSyncStates.ToListAsync());Assert.Equal(DocumentModel.CTe,(await h.Db.DocumentSyncStates.SingleAsync()).Model);
  result=await h.Service.ReprocessAsync(new([h.Company],FiscalEnvironment.Production,EnabledDocuments.CTeOS));Assert.Equal(1,Assert.Single(result.Rows).Archived);Assert.Equal(0,result.Rows[0].Retained);Assert.Equal(1,h.Transport.Calls);Assert.Equal(2,await h.Db.FiscalDocuments.CountAsync());Assert.Equal("2",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
 }
 [Fact]public async Task UnknownGtvOrSimplifiedLayout_IsDurablyRetainedWithoutFakeDocument()
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1","<GTVe xmlns='http://www.portalfiscal.inf.br/cte'><infGTVe/></GTVe>","procGTVe_v4.00.xsd")));var r=await h.Run();Assert.Equal(1,Assert.Single(r.Rows).Retained);Assert.Equal("1",r.Rows[0].Nsu);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());var stored=await h.Db.CteDistributionItems.SingleAsync();Assert.NotEmpty(stored.XmlBytes);Assert.False(stored.Archived);Assert.Equal("CTE_LAYOUT_RETAINED",stored.ResultCode);Assert.Equal(JobStatus.CompletedWithWarnings,(await h.Db.ProcessingJobs.SingleAsync()).Status);}
 [Fact]public async Task WrongFiscalEnvironment_IsRetainedWithoutMarkingAvailable()
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1",DfeParserTests.Document(57),"procCTe_v4.00.xsd")));await h.Run();Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.False((await h.Db.CteDistributionItems.SingleAsync()).Archived);Assert.Empty(await h.Db.KnownDocumentKeys.ToListAsync());}
 [Fact]public async Task TamperedReceipt_HoldsCursorAndPreventsNetwork()
 {await using var h=await Harness.Create();h.Db.CteDistributionReceipts.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,Environment=FiscalEnvironment.Production,ResponseBytes=Page(),ResponseHash="BAD"});await h.Db.SaveChangesAsync();var r=await h.Run();Assert.Equal("CTE_INBOX_HASH_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal(0,h.Transport.Calls);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task InvalidSoap_DoesNotAdvanceAndKeepsPendingInbox()
 {await using var h=await Harness.Create();h.Transport.Add(Encoding.UTF8.GetBytes("<bad/>"));await h.Run();Assert.False((await h.Db.CteDistributionReceipts.SingleAsync()).Applied);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);await h.Run();Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task SqlFailure_RollsBackPageAndOfflineReplayUsesSavedReceipt()
 {
  await using var h=await Harness.Create();await h.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_cte BEFORE UPDATE ON DocumentSyncStates WHEN NEW.LastNsu <> '0' BEGIN SELECT RAISE(ABORT,'synthetic'); END;");h.Transport.Add(Page());await Assert.ThrowsAsync<DbUpdateException>(()=>h.Run());h.Db.ChangeTracker.Clear();Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Empty(await h.Db.CteDistributionItems.ToListAsync());Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.CteDistributionReceipts.SingleAsync()).Applied);Assert.Single(h.Files.EnumeratePhysicalFiles());await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_cte;");await h.Service.ReprocessAsync(new([h.Company],FiscalEnvironment.Production));Assert.Equal(1,h.Transport.Calls);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
 }
 [Fact]public async Task CancelAfterReceive_KeepsReceiptAndReplayWithoutHttp()
 {await using var h=await Harness.Create();using var cts=new CancellationTokenSource();h.Transport.AfterReceive=()=>cts.Cancel();h.Transport.Add(Page());var r=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production),ct:cts.Token);Assert.True(r.Cancelled);h.Db.ChangeTracker.Clear();Assert.False((await h.Db.CteDistributionReceipts.SingleAsync()).Applied);h.Transport.AfterReceive=null;await h.Service.ReprocessAsync(new([h.Company],FiscalEnvironment.Production));Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task CooldownSurvivesNewServiceCallAndNoNsuReset()
 {await using var h=await Harness.Create();h.Transport.Add(Page());await h.Run();var r=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);h.Clock.Advance(TimeSpan.FromHours(1));h.Transport.Add(Page(137,"1"));await h.Run();Assert.Contains("000000000000001",Encoding.UTF8.GetString(h.Transport.Requests.Last().Body));}
 [Fact]public async Task NsuPointIsCachedAndNeverChangesDistributionCursor()
 {await using var h=await Harness.Create();h.Transport.Add(Page(next:"42"));var r=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,SpecificNsu:"42"));Assert.Equal("0",Assert.Single(r.Rows).Nsu);r=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,SpecificNsu:"42"));Assert.Equal("CTE_LOCAL_NSU_PRESENT",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task CachedPointCanRestoreMissingPhysicalXml()
 {await using var h=await Harness.Create();h.Transport.Add(Page());await h.Run();var f=await h.Db.ArchiveFiles.SingleAsync();File.Delete(Path.Combine(h.Directory,f.RelativePath));await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,SpecificNsu:"1"));Assert.True(File.Exists(Path.Combine(h.Directory,f.RelativePath)));Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task NetworkUncertainty_DoesNotPermitImmediateRepeat()
 {await using var h=await Harness.Create();h.Transport.Error="NETWORK_TIMEOUT";await h.Run();h.Transport.Error=null;var r=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task CertificatePreflightFailure_AllowsCorrectionWithoutArtificialWait()
 {await using var h=await Harness.Create();h.Transport.Error="PROVIDER_CERTIFICATE_REQUIRED";await h.Run();Assert.Null((await h.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc);h.Transport.Error=null;h.Transport.Add(Page());await h.Run();Assert.Equal(2,h.Transport.Calls);}
 [Fact]public async Task ConsumptionBlock_AlsoBlocksPointRequest()
 {await using var h=await Harness.Create();h.Transport.Add(Page(656,"0"));await h.Run();var r=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,SpecificNsu:"1"));Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);Assert.Single(await h.Db.ApplicationSettings.ToListAsync());}
 [Theory][InlineData("rem","Remetente")][InlineData("exped","Expedidor")][InlineData("receb","Recebedor")][InlineData("toma4","Tomador")][InlineData("autXML","Autorizado no XML")]
 public async Task OtherTransportParticipants_AreLinkedAndSearchable(string role,string label)
 {
  await using var h=await Harness.Create();var xml=XDocument.Parse(Doc());XNamespace ns=CteWireCodec.Namespace;var info=xml.Descendants(ns+"infCte").Single();info.Element(ns+"dest")!.Element(ns+"CNPJ")!.Value="12345678000195";var element=new XElement(ns+role,new XElement(ns+"CNPJ",Tax));
  if(role=="toma4"){element.AddFirst(new XElement(ns+"toma","4"));info.Element(ns+"ide")!.Add(element);}else info.Add(element);
  h.Transport.Add(Page(138,"1",null,("1",xml.ToString(),"procCTe_v4.00.xsd")));await h.Run();var link=await h.Db.FiscalDocumentCompanies.SingleAsync();Assert.Equal(ActorFilter.OtherParticipant,link.Role);Assert.Contains(label,link.Participation);Assert.Single((await h.Archive.SearchAsync(new(CompanyId:h.Company))).Items);Assert.Empty((await h.Archive.SearchAsync(new(CompanyId:h.Company,Role:ActorFilter.Recipient))).Items);
 }
 [Theory][InlineData("0","rem")][InlineData("1","exped")][InlineData("2","receb")][InlineData("3","dest")]
 public void Toma3_ResolvesCorrespondingActor(string indicator,string actor)
 {var xml=XDocument.Parse(Doc());XNamespace ns=CteWireCodec.Namespace;var info=xml.Descendants(ns+"infCte").Single();if(actor!="dest")info.Add(new XElement(ns+actor,new XElement(ns+"CNPJ",Tax)));info.Element(ns+"ide")!.Add(new XElement(ns+"toma3",new XElement(ns+"toma",indicator)));Assert.Contains(CteParticipants.Read(Encoding.UTF8.GetBytes(xml.ToString())),x=>x.TaxId==Tax&&x.Role=="Tomador");}
 [Fact]public async Task CteOsTakerWithoutDest_IsLinked()
 {await using var h=await Harness.Create();var xml=XDocument.Parse(Doc(67));XNamespace ns=CteWireCodec.Namespace;var info=xml.Descendants(ns+"infCte").Single();info.Element(ns+"dest")!.Remove();info.Add(new XElement(ns+"toma",new XElement(ns+"CNPJ",Tax),new XElement(ns+"xNome","Tomador")));h.Transport.Add(Page(138,"1",null,("1",xml.ToString(),"procCTeOS_v4.00.xsd")));await h.Run();Assert.Contains("Tomador",(await h.Db.FiscalDocumentCompanies.SingleAsync()).Participation);}
 [Fact]public async Task EventBeforeNote_IsLinkedAndKnownKeyRemainsPendingUntilFullXml()
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1",Event(),"procEventoCTe_v4.00.xsd")));await h.Run();Assert.Null((await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(Availability.Pending,(await h.Db.KnownDocumentKeys.SingleAsync()).Availability);h.Clock.Advance(TimeSpan.FromHours(1));h.Transport.Add(Page(next:"2"));await h.Run();Assert.NotNull((await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(DocumentStatus.Cancelled,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task Conflict_IsPreservedAndDoesNotOverwriteCanonicalDocument()
 {await using var h=await Harness.Create();h.Transport.Add(Page());await h.Run();string hash=(await h.Db.FiscalDocuments.SingleAsync()).XmlHash;h.Clock.Advance(TimeSpan.FromHours(1));h.Transport.Add(Page(138,"2",null,("2",Doc().Replace("123456789.01","2.00"),"procCTe_v4.00.xsd")));var r=await h.Run();Assert.Equal(1,Assert.Single(r.Rows).Retained);Assert.Equal("2",r.Rows[0].Nsu);Assert.Equal(hash,(await h.Db.FiscalDocuments.SingleAsync()).XmlHash);Assert.Equal(2,await h.Db.ArchiveFiles.CountAsync());Assert.Equal("CTE_CONFLICT_RETAINED",(await h.Db.CteDistributionItems.SingleAsync(x=>!x.Archived)).ResultCode);}
 [Fact]public async Task OneHundredDeferredItems_ReprocessAllChunksWithoutLosingIds()
 {await using var h=await Harness.Create();var a=Enumerable.Range(1,50).Select(n=>(n.ToString(),Doc(67),"procCTeOS_v4.00.xsd")).ToArray();var b=Enumerable.Range(51,50).Select(n=>(n.ToString(),Doc(67),"procCTeOS_v4.00.xsd")).ToArray();h.Transport.Add(Page(138,"50","100",a));h.Transport.Add(Page(138,"100","100",b));var r=await h.Run(EnabledDocuments.CTe);Assert.Equal(100,Assert.Single(r.Rows).Retained);r=await h.Service.ReprocessAsync(new([h.Company],FiscalEnvironment.Production,EnabledDocuments.CTeOS));Assert.Equal(100,Assert.Single(r.Rows).Archived);Assert.Equal(0,r.Rows[0].Retained);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("100",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Equal(2,h.Transport.Calls);}
 [Fact]public async Task CompanyDisabledOrAnotherId_NeverCallsNetwork()
 {await using var h=await Harness.Create();var company=await h.Db.Companies.SingleAsync();company.EnabledDocuments=EnabledDocuments.NFe;await h.Db.SaveChangesAsync();var r=await h.Run();Assert.Equal("CTE_COMPANY_DISABLED",Assert.Single(r.Rows).Code);await Assert.ThrowsAsync<NfeException>(()=>h.Service.SynchronizeAsync(new([Guid.NewGuid()],FiscalEnvironment.Production)));Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task CteCertificateMustBeParticipantWithSameRoot()
 {await Task.CompletedTask;NfeCredentialSource.ValidateIdentity(CteSource.Code,Tax,"12345678000195",CertificateUse.Participant);Assert.Throws<NfeException>(()=>NfeCredentialSource.ValidateIdentity(CteSource.Code,Tax,"12345678000195",CertificateUse.Accountant));Assert.Throws<NfeException>(()=>NfeCredentialSource.ValidateIdentity(CteSource.Code,Tax,"00000000000191",CertificateUse.Participant));}

 [Fact]public void AlphaCnpj_IsKeptAsTextInWireRequest()
 {var wire=CteWireCodec.Build(Request(),"12ABC34501DE35","42");Assert.Contains("12ABC34501DE35",Encoding.UTF8.GetString(wire.Body));}
 [Fact]public async Task RetainedPreview_ConfirmsHashAndDoesNotPretendToBeArchived()
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1","<unknown><value>Teste</value></unknown>","unknown.xsd")));await h.Run();var row=Assert.Single(await h.Service.RetainedAsync());var preview=await h.Service.ReadRetainedXmlAsync(row.Id);Assert.Contains("Teste",preview.Text);Assert.False(preview.PreviewTruncated);Assert.Equal((await h.Db.CteDistributionItems.SingleAsync()).XmlHash,preview.Hash);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task RetainedTampering_IsDetectedBeforePreviewOrReprocess()
 {await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1","<unknown/>","unknown.xsd")));await h.Run();var item=await h.Db.CteDistributionItems.SingleAsync();item.XmlBytes=Encoding.UTF8.GetBytes("<changed/>");await h.Db.SaveChangesAsync();await Assert.ThrowsAsync<NfeException>(()=>h.Service.ReadRetainedXmlAsync(item.Id));var r=await h.Service.ReprocessAsync(new([h.Company],FiscalEnvironment.Production));Assert.Equal("CTE_ITEM_HASH_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task CachedPointForUnselectedModel_DoesNotChangePreviousArchiveState()
 {await using var h=await Harness.Create();h.Transport.Add(Page());await h.Run();var r=await h.Service.SynchronizeAsync(new([h.Company],FiscalEnvironment.Production,EnabledDocuments.CTeOS,SpecificNsu:"1"));Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.True((await h.Db.CteDistributionItems.SingleAsync()).Archived);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task RequesterConsumptionBlock_AppliesToOtherCompanyUsingSameCertificate()
 {
  await using var h=await Harness.Create();var other=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Outra empresa",State="SC"};var certificate=new WgsDfe.Domain.Certificate{OrganizationId=h.Org,Label="Synthetic",SecureStoreReference="synthetic"};h.Db.Companies.Add(other);h.Db.Certificates.Add(certificate);h.Db.CertificateAssociations.AddRange(new CertificateAssociation{OrganizationId=h.Org,CompanyId=h.Company,CertificateId=certificate.Id,ProviderCode=CteSource.Code},new CertificateAssociation{OrganizationId=h.Org,CompanyId=other.Id,CertificateId=certificate.Id,ProviderCode=CteSource.Code});await h.Db.SaveChangesAsync();h.Transport.Add(Page(656,"0"));var r=await h.Service.SynchronizeAsync(new([h.Company,other.Id],FiscalEnvironment.Production));Assert.Equal(2,r.Rows.Count);Assert.Contains(r.Rows,x=>x.Code=="WAIT_UNTIL_ALLOWED");Assert.Equal(1,h.Transport.Calls);
 }
 [Fact]public async Task PreservedItems_RespectOrganizationQueryFilters()
 {
  await using var h=await Harness.Create();h.Transport.Add(Page(138,"1",null,("1","<unknown/>","unknown.xsd")));await h.Run();Guid id=(await h.Db.CteDistributionItems.SingleAsync()).Id;Guid org=Guid.NewGuid();await using var db=DatabaseFactory.Create(Path.Combine(h.Directory,"test.db"),org);db.Organizations.Add(new(){Id=org,Name="Other"});await db.SaveChangesAsync();var parser=new DfeParser();using var archiveGate=new ArchiveGate();using var gate=new NfeSyncGate();var files=new ArchiveFileStore(new(h.Directory,org));var archive=new ArchiveService(db,parser,files,archiveGate);var service=new CteSyncService(db,new CteSoapProvider(new Credentials(),h.Transport),parser,archive,archiveGate,gate,h.Clock);Assert.Empty(await service.RetainedAsync());await Assert.ThrowsAsync<NfeException>(()=>service.ReadRetainedXmlAsync(id));Assert.Empty(await db.CteDistributionReceipts.ToListAsync());
 }
 [Fact]public async Task Restart_OnlyMarksInterruptedCteJobAndPreservesPendingReceipt()
 {
  string path=Path.Combine(Path.GetTempPath(),"wgs-cte-restart-"+Guid.NewGuid().ToString("N"));Guid org=Guid.NewGuid(),jobId=Guid.NewGuid(),other=Guid.NewGuid();var options=new FoundationOptions{Application=new(){OrganizationId=org,OrganizationName="Synthetic",DataDirectory=path}};
  try
  {
   await using(var runtime=new FoundationRuntime(options))
   {await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();var company=new Company{OrganizationId=org,Cnpj=Tax,LegalName="Synthetic"};db.Companies.Add(company);db.ProcessingJobs.AddRange(new ProcessingJob{Id=jobId,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running},new ProcessingJob{Id=other,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running});db.AuditLogs.Add(new(){OrganizationId=org,Action=AuditAction.DownloadStarted,EntityId=jobId,ResultCode="CTE_STARTED"});var bytes=Page();db.CteDistributionReceipts.Add(new(){OrganizationId=org,CompanyId=company.Id,Environment=FiscalEnvironment.Production,ResponseBytes=bytes,ResponseHash=Convert.ToHexString(SHA256.HashData(bytes))});await db.SaveChangesAsync();}
   await using(var runtime=new FoundationRuntime(options))
   {await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();Assert.Equal(JobStatus.Failed,(await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId)).Status);Assert.Equal("CTE_INTERRUPTED_INBOX_PRESERVED",(await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId)).LastErrorCode);Assert.Equal(JobStatus.Running,(await db.ProcessingJobs.SingleAsync(x=>x.Id==other)).Status);Assert.False((await db.CteDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await db.DocumentSyncStates.ToListAsync());Assert.False(runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests);}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(path,true);}
 }
 private sealed class FakeClock:TimeProvider{private DateTimeOffset time=new(2026,10,17,0,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>time;public void Advance(TimeSpan span)=>time+=span;}
 private sealed class Credentials:INfeCredentialSource
 {public Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct){using var rsa=RSA.Create(2048);var cert=new CertificateRequest("CN=SYNTHETIC",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));return Task.FromResult(new NfeCredential(request.CompanyTaxId!,"42",cert));}}
 private sealed class Transport:INfeTransport
 {private readonly Queue<byte[]> responses=new();public int Calls;public string? Error;public Action? AfterReceive;public List<NfeWireRequest> Requests{get;}=[];public void Add(byte[] response)=>responses.Enqueue(response);public Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct){Calls++;Requests.Add(request);if(Error is not null)throw new NfeException(Error);var result=responses.Dequeue();AfterReceive?.Invoke();return Task.FromResult(result);}}
 [Theory][InlineData(10,10,1)][InlineData(1,30,1)][InlineData(11,11,0)]
 public async Task Period_filter_does_not_rewind_shared_Cte_stream(int first,int last,int expected)
 {
  await using var h=await Harness.Create();h.Transport.Add(Page());
  var from=new DateOnly(2026,9,first);var to=new DateOnly(2026,9,last);var progress=new List<NfeProgress>();
  var request=new CteSyncRequest([h.Company],FiscalEnvironment.Production,MaxPages:0,From:from,To:to);
  var result=await h.Service.SynchronizeAsync(request,new PeriodProgress(progress));
  Assert.Equal("CTE_SYNCHRONIZED",Assert.Single(result.Rows).Code);
  var committed=Assert.Single(progress,x=>x.Code=="CTE_PAGE_COMMITTED");Assert.Equal(expected,committed.InPeriod);Assert.Equal(1-expected,committed.OutsidePeriod);
  var page=await h.Archive.SearchAsync(new ArchiveQuery(CompanyId:h.Company,From:from,To:to));Assert.Equal(expected,page.Total);Assert.All(page.Items,x=>Assert.InRange(DateOnly.Parse(x.IssueDate!),from,to));
  Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
  int calls=h.Transport.Calls;await h.Service.SynchronizeAsync(request);Assert.Equal(calls,h.Transport.Calls);
  h.Clock.Advance(TimeSpan.FromHours(1));h.Transport.Add(Page(137,"1"));await h.Service.SynchronizeAsync(request);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
 }
 private sealed class PeriodProgress(List<NfeProgress> rows):IProgress<NfeProgress>{public void Report(NfeProgress value)=>rows.Add(value);}
 private sealed class Harness:IAsyncDisposable
 {
  public string Directory{get;}=Path.Combine(Path.GetTempPath(),"wgs-cte-test-"+Guid.NewGuid().ToString("N"));public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public WgsDbContext Db{get;private set;}=null!;public ArchiveService Archive{get;private set;}=null!;public ArchiveFileStore Files{get;private set;}=null!;public CteSyncService Service{get;private set;}=null!;public Transport Transport{get;}=new();public FakeClock Clock{get;}=new();private ArchiveGate archiveGate=new();private NfeSyncGate syncGate=new();
  public static async Task<Harness> Create()
  {var h=new Harness();System.IO.Directory.CreateDirectory(h.Directory);h.Db=DatabaseFactory.Create(Path.Combine(h.Directory,"test.db"),h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Synthetic"});var company=new Company{OrganizationId=h.Org,Cnpj=Tax,LegalName="Empresa sintética",State="SC"};h.Company=company.Id;h.Db.Companies.Add(company);await h.Db.SaveChangesAsync();h.Files=new(new(h.Directory,h.Org));var parser=new DfeParser();h.Archive=new(h.Db,parser,h.Files,h.archiveGate);h.Service=new(h.Db,new CteSoapProvider(new Credentials(),h.Transport),parser,h.Archive,h.archiveGate,h.syncGate,h.Clock);return h;}
  public Task<CteSyncResult> Run(EnabledDocuments models=EnabledDocuments.CTe|EnabledDocuments.CTeOS)=>Service.SynchronizeAsync(new([Company],FiscalEnvironment.Production,models));
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();archiveGate.Dispose();syncGate.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(Directory,true);}
 }
}
