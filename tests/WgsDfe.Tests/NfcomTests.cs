using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Nfe;
using WgsDfe.Infrastructure.Parsing;
using WgsDfe.Providers.Nfe;
using Xunit;
namespace WgsDfe.Tests;
public sealed class NfcomTests
{
 private static string Key=>DfeParserTests.Key(62);
 private static string Doc=>DfeParserTests.Document(62).Replace("<tpAmb>2","<tpAmb>1");
 private static DocumentRequest Request(string source=NfeSources.SantaCatarinaNfcom,string nsu="0",string? key=null,PayloadMode mode=PayloadMode.FullXml)=>new(Guid.NewGuid(),Guid.NewGuid(),DocumentModel.NFCom,FiscalEnvironment.Production,ActorFilter.Either,mode,nsu,key,null,null,Guid.NewGuid(),"12345678000195");
 private static string Zip(string xml){using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionMode.Compress,true))gzip.Write(Encoding.UTF8.GetBytes(xml));return Convert.ToBase64String(output.ToArray());}
 private static byte[] Soap(string source,string ret)
 {
  string ws=source==NfeSources.SantaCatarinaNfcom?NfeWireCodec.NfcomNamespace:NfeWireCodec.NationalNamespace,op=source==NfeSources.SantaCatarinaNfcom?"nfcomDownloadContab":"nfeDistDFeInteresse";
  return Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><{op}Response xmlns='{ws}'><{op}Result>{ret}</{op}Result></{op}Response></s:Body></s:Envelope>");
 }
 private static byte[] Sc(int status=118,string nsu="1",string? document=null,int? count=null,bool keys=false)
 {
  string lot=$"<loteDistNFComSC versao='1.00'><distNFComSC NSU='{nsu}' chAcesso='{Key}'>{(keys?"":document??Doc)}</distNFComSC></loteDistNFComSC>";
  string ret=$"<retDistNFComSC xmlns='{NfeWireCodec.NfcomNamespace}' versao='1.00'><tpAmb>1</tpAmb><cStat>{status}</cStat><xMotivo>Teste sintético</xMotivo><dhResp>2026-10-16T10:00:00-03:00</dhResp><CNPJ>12345678000195</CNPJ>{(status==118?$"<ultNuNSURet>{nsu}</ultNuNSURet><qtDfeRet>{count??1}</qtDfeRet><loteDistComp>{Zip(lot)}</loteDistComp>":"")}</retDistNFComSC>";
  return Soap(NfeSources.SantaCatarinaNfcom,ret);
 }
 [Fact]public void ScHomologation_IsExplicitlyUnsupported()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request() with{Environment=FiscalEnvironment.Homologation},"12345678000195","42"));
 [Fact]public void ScFullPage_ExtractsOriginalFiscalNamespace()
 {var page=NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Sc(),Request());Assert.Equal(118,page.Status);Assert.Equal("1",page.NextNsu);Assert.Equal(TimeSpan.FromHours(12),page.Delay);Assert.False(page.HasMore);Assert.Equal(Key,new DfeParser().Parse(Assert.Single(page.Items).Xml!).Key.Value);}
 [Fact]public void ScKeysOnly_DoesNotInventXml()
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Sc(keys:true),Request(mode:PayloadMode.KeysOnly));Assert.Null(Assert.Single(p.Items).Xml);Assert.Equal(Key,p.Items[0].Key);}
 [Fact]public void ScEmpty_DoesNotResetCursor()
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Sc(117),Request(nsu:"42"));Assert.Equal("42",p.NextNsu);Assert.Empty(p.Items);}
 [Theory][InlineData(110,1)][InlineData(117,12)][InlineData(657,12)][InlineData(8002,1)][InlineData(632,1)]public void ScOfficialRejections_KeepCursorAndRespectWait(int status,int hours)
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Sc(status),Request());Assert.Equal(status,p.Status);Assert.Equal(TimeSpan.FromHours(hours),p.Delay);if(status!=117)Assert.Null(p.NextNsu);}
 [Theory][InlineData("status")][InlineData("env")][InlineData("namespace")][InlineData("company")][InlineData("count")][InlineData("cursor")]
 public void InvalidEnvelopeOrCursor_IsRejected(string change)
 {
  string xml=Encoding.UTF8.GetString(Sc());xml=change switch{"status"=>xml.Replace("<cStat>118</cStat>","<cStat>118</cStat><cStat>118</cStat>"),"env"=>xml.Replace("<tpAmb>1","<tpAmb>2"),"namespace"=>xml.Replace(NfeWireCodec.NfcomNamespace,"urn:invalid"),"company"=>xml.Replace("<CNPJ>12345678000195","<CNPJ>12345678000276"),"count"=>xml.Replace("<qtDfeRet>1","<qtDfeRet>2"),_=>xml.Replace("<ultNuNSURet>1","<ultNuNSURet>0")};Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Encoding.UTF8.GetBytes(xml),Request()));
 }
 [Fact]public void SoapFault_IsStaticAndDoesNotExposeServerMessage()
 {var bytes=Encoding.UTF8.GetBytes("<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault><faultstring>SENSITIVE</faultstring></s:Fault></s:Body></s:Envelope>");var e=Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,bytes,Request()));Assert.Equal("SOAP_FAULT",e.Code);Assert.DoesNotContain("SENSITIVE",e.Message);}
 [Fact]public void Dtd_IsRejected()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><x>&a;</x>"),Request()));
 [Theory][InlineData("badgzip")][InlineData("truncated")][InlineData("depth")]
 public void MalformedTransportPayload_IsRejected(string change)
 {
  string ret=Encoding.UTF8.GetString(Sc());if(change=="depth")ret=new string(' ',1)+string.Concat(Enumerable.Repeat("<a>",90))+string.Concat(Enumerable.Repeat("</a>",90));else{var x=XDocument.Parse(ret);var e=x.Descendants(XName.Get("loteDistComp",NfeWireCodec.NfcomNamespace)).Single();e.Value=change=="badgzip"?"bm90LWd6aXA=":Convert.ToBase64String(Convert.FromBase64String(e.Value)[..15]);ret=x.ToString();}Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Encoding.UTF8.GetBytes(ret),Request()));
 }
 [Fact]public async Task ExternalRequestsDisabled_NeverStartsHttp()
 {using var gate=new NfeNetworkGate();var transport=new NfeTransport(new ProviderOptions(),gate);using var cert=Certificate();var e=await Assert.ThrowsAsync<NfeException>(()=>transport.SendAsync(NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(),"12345678000195","42"),cert,default));Assert.Equal("EXTERNAL_REQUESTS_DISABLED",e.Code);}
 [Fact]public async Task TransportRejectsUnofficialEndpoint()
 {using var gate=new NfeNetworkGate();var transport=new NfeTransport(new ProviderOptions{EnableExternalRequests=true},gate);using var cert=Certificate();await Assert.ThrowsAsync<NfeException>(()=>transport.SendAsync(new(new Uri("https://example.invalid"),"x",[]),cert,default));}
 [Fact]public async Task Sync_CommitsAcervoLinksReceiptAndCursor()
 {
  await using var h=await Harness.Create();h.Transport.Enqueue(Sc());var r=await h.Run();var row=Assert.Single(r.Rows);Assert.Equal("NFE_SYNCHRONIZED",row.Code);Assert.Equal("1",row.Nsu);Assert.Equal(1,row.Documents);
  var d=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal(NfeSources.SantaCatarinaNfcom,d.SourceProviderCode);Assert.Equal(1,await h.Db.FiscalDocumentCompanies.CountAsync());Assert.True((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Equal(TimeSpan.FromHours(12),(await h.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc-h.Clock.GetUtcNow().UtcDateTime);Assert.Equal("SEF_SC_LINK_ACCEPTED",(await h.Db.Companies.SingleAsync()).AccountantLinkStatusCode);
 }
 [Fact]public async Task RepeatBeforeCooldown_DoesNotSendAnotherRequest()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();var r=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task AfterCooldown_UsesSavedCursorAndNeverResetsToZero()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(117));var r=await h.Run();Assert.Equal("1",Assert.Single(r.Rows).Nsu);Assert.Contains("<ultNuNSU>1</ultNuNSU>",Encoding.UTF8.GetString(h.Transport.Requests.Last().Body));}
 [Fact]public async Task KeysOnly_CommitsKnownKeysButNoFakeFullDocument()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(keys:true));var r=await h.Run(mode:PayloadMode.KeysOnly);Assert.Equal(1,Assert.Single(r.Rows).Summaries);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(Availability.Pending,(await h.Db.KnownDocumentKeys.SingleAsync()).Availability);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task InvalidFiscalXml_RetainsInboxAndCursorForOfflineRetry()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:Doc.Replace("123456789.01","invalid")));var r=await h.Run();Assert.Contains("INBOX_RETAINED",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());await h.Run();Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task WrongEnvironmentInPayload_HoldsEntirePage()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:DfeParserTests.Document(62)));var r=await h.Run();Assert.Equal("PAYLOAD_IDENTITY_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task DatabaseFailure_RollsBackPageAndReplaysWithoutNetwork()
 {
  await using var h=await Harness.Create();await h.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_cursor BEFORE UPDATE ON DocumentSyncStates WHEN NEW.LastNsu <> '0' BEGIN SELECT RAISE(ABORT,'synthetic'); END;");h.Transport.Enqueue(Sc());await Assert.ThrowsAsync<DbUpdateException>(()=>h.Run());h.Db.ChangeTracker.Clear();Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Single(h.Files.EnumeratePhysicalFiles());await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_cursor;");await h.Run();Assert.Equal(1,h.Transport.Calls);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());
 }
 [Fact]public async Task PendingInboxHashTampering_PreventsCursorAdvance()
 {await using var h=await Harness.Create();h.Db.NfeDistributionReceipts.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,ProviderCode=h.Source,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,ResponseBytes=Sc(),ResponseHash="BAD"});await h.Db.SaveChangesAsync();var r=await h.Run();Assert.Equal("INBOX_HASH_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task KeyUsesLocalCacheBeforeNetworkOrCooldown()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();var r=await h.Run(key:Key);Assert.Equal("LOCAL_XML_PRESENT",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task KeyLookup_DoesNotAdvanceDistributionNsu()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(nsu:"999"));var r=await h.Run(key:Key);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task NetworkUncertainty_IsPersistedAndPreventsImmediateRetry()
 {await using var h=await Harness.Create();h.Transport.Error="NETWORK_TIMEOUT";var r=await h.Run();Assert.Equal("NETWORK_TIMEOUT",Assert.Single(r.Rows).Code);h.Transport.Error=null;r=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task OfficialLinkRejection_IsDistinctFromNoDocuments()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(8002));var r=await h.Run();Assert.Equal("NFE_ACCOUNTANT_LINK_DENIED",Assert.Single(r.Rows).Code);Assert.Equal("SEF_SC_LINK_DENIED",(await h.Db.Companies.SingleAsync()).AccountantLinkStatusCode);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task ConcurrentSyncCalls_AreSerializedAndSecondUsesCooldown()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());var first=h.Run();var second=h.Run();await Task.WhenAll(first,second);Assert.Equal(1,h.Transport.Calls);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());}
 [Fact]public async Task AnotherOrganizationCompanyId_IsRejectedBeforeNetwork()
 {await using var h=await Harness.Create();await Assert.ThrowsAsync<NfeException>(()=>h.Service.SynchronizeAsync(new([Guid.NewGuid()],h.Source,FiscalEnvironment.Production)));Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task CancelAfterReceive_DurablyRetainsPageForNextRun()
 {await using var h=await Harness.Create();using var cts=new CancellationTokenSource();h.Transport.Enqueue(Sc());h.Transport.AfterReceive=()=>cts.Cancel();var r=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production),ct:cts.Token);Assert.True(r.Cancelled);h.Db.ChangeTracker.Clear();Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);h.Transport.AfterReceive=null;await h.Run();Assert.Equal(1,h.Transport.Calls);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());}

 [Fact]public async Task FiftyScRecords_ContinueThenWaitAndDeduplicateDocument()
 {await using var h=await Harness.Create();string lot="<loteDistNFComSC versao='1.00'>"+string.Concat(Enumerable.Range(1,50).Select(n=>$"<distNFComSC NSU='{n}' chAcesso='{Key}'>{Doc}</distNFComSC>"))+"</loteDistNFComSC>";var x=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"50",count:50)));x.Descendants().Single(x=>x.Name.LocalName=="loteDistComp").Value=Zip(lot);h.Transport.Enqueue(Encoding.UTF8.GetBytes(x.ToString()));h.Transport.Enqueue(Sc(117));var r=await h.Run();Assert.Equal(2,Assert.Single(r.Rows).Pages);Assert.Equal("50",r.Rows[0].Nsu);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(2,await h.Db.NfeDistributionReceipts.CountAsync());Assert.Equal(1,await h.Db.ArchiveFiles.CountAsync());}
 [Fact]public async Task MixedValidAndInvalidRecords_CommitNothingFromPage()
 {await using var h=await Harness.Create();string lot=$"<loteDistNFComSC versao='1.00'><distNFComSC NSU='1' chAcesso='{Key}'>{Doc}</distNFComSC><distNFComSC NSU='2' chAcesso='{Key}'>{Doc.Replace("123456789.01","invalid")}</distNFComSC></loteDistNFComSC>";var x=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"2",count:2)));x.Descendants().Single(x=>x.Name.LocalName=="loteDistComp").Value=Zip(lot);h.Transport.Enqueue(Encoding.UTF8.GetBytes(x.ToString()));await h.Run();Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);}
 [Fact]public async Task ConflictingRemoteNote_NeverOverwritesOrAdvancesCursor()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();string hash=(await h.Db.FiscalDocuments.SingleAsync()).XmlHash;h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2",document:Doc.Replace("123456789.01","2.00")));var r=await h.Run();Assert.Equal("PAYLOAD_CONFLICT_INBOX_RETAINED",Assert.Single(r.Rows).Code);Assert.Equal(hash,(await h.Db.FiscalDocuments.SingleAsync()).XmlHash);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Single(await h.Db.ArchiveFiles.ToListAsync());Assert.Equal(2,h.Files.EnumeratePhysicalFiles().Count());}
 [Fact]public async Task EventAndFullNote_ArriveInDifferentPages()
 {await using var h=await Harness.Create();string ev=DfeParserTests.Event(62).Replace("<tpAmb>2","<tpAmb>1");h.Transport.Enqueue(Sc(document:ev));await h.Run();Assert.Null((await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(Availability.Pending,(await h.Db.KnownDocumentKeys.SingleAsync()).Availability);h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2"));await h.Run();Assert.Equal((await h.Db.FiscalDocuments.SingleAsync()).Id,(await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(DocumentStatus.Cancelled,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task DifferentCompanies_HaveIndependentCursors()
 {await using var h=await Harness.Create();var other=new Company{OrganizationId=h.Org,Cnpj="12345678000276",LegalName="Outra empresa",State="SC"};h.Db.Companies.Add(other);await h.Db.SaveChangesAsync();h.Transport.Enqueue(Sc());var second=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"2")));second.Descendants().Single(x=>x.Name.LocalName=="CNPJ").Value=other.Cnpj;h.Transport.Enqueue(Encoding.UTF8.GetBytes(second.ToString()));var r=await h.Service.SynchronizeAsync(new([h.Company,other.Id],h.Source,FiscalEnvironment.Production));Assert.Equal(2,r.Rows.Count);Assert.Equal(new[]{"1","2"},(await h.Db.DocumentSyncStates.OrderBy(x=>x.LastNsu).ToListAsync()).Select(x=>x.LastNsu));Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(2,await h.Db.FiscalDocumentCompanies.CountAsync());}
 [Fact]public async Task CertificatePreflightError_DoesNotReserveTwelveHourBlock()
 {await using var h=await Harness.Create();h.Transport.Error="PROVIDER_CERTIFICATE_REQUIRED";await h.Run();Assert.Null((await h.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc);h.Transport.Error=null;h.Transport.Enqueue(Sc());await h.Run();Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}

 [Fact]public async Task Restart_MarksOnlyNfeJobsInterruptedAndDoesNotDiscardInbox()
 {
  string path=Path.Combine(Path.GetTempPath(),"wgs-nfcom-restart-"+Guid.NewGuid().ToString("N"));Guid org=Guid.NewGuid(),jobId=Guid.NewGuid(),otherId=Guid.NewGuid();var options=new FoundationOptions{Application=new(){OrganizationId=org,OrganizationName="Synthetic",DataDirectory=path}};
  try
  {
   await using(var runtime=new FoundationRuntime(options))
   {
    await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();var company=new Company{OrganizationId=org,Cnpj="12345678000195",LegalName="Synthetic"};db.Companies.Add(company);db.ProcessingJobs.AddRange(new ProcessingJob{Id=jobId,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running},new ProcessingJob{Id=otherId,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running});db.AuditLogs.Add(new(){OrganizationId=org,Action=AuditAction.DownloadStarted,EntityId=jobId,ResultCode="NFE_STARTED"});var bytes=Sc();db.NfeDistributionReceipts.Add(new(){OrganizationId=org,CompanyId=company.Id,ProviderCode=NfeSources.SantaCatarinaNfcom,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,ResponseBytes=bytes,ResponseHash=Convert.ToHexString(SHA256.HashData(bytes))});await db.SaveChangesAsync();
   }
   await using(var runtime=new FoundationRuntime(options))
   {await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();Assert.Equal(JobStatus.Failed,(await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId)).Status);Assert.Equal(JobStatus.Running,(await db.ProcessingJobs.SingleAsync(x=>x.Id==otherId)).Status);Assert.False((await db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await db.DocumentSyncStates.ToListAsync());Assert.False(runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests);}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(path,true);}
 }

 [Fact]public void RequestMatchesCapturedWsdlWithoutPXml()
 {
  var wire=NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(),"12345678000195","42");
  Assert.Equal(NfeWireCodec.NfcomEndpoint,wire.Endpoint.AbsoluteUri);Assert.Equal(NfeWireCodec.NfcomNamespace+"/nfcomDownloadContab",wire.SoapAction);Assert.False(wire.Soap12);
  XNamespace ns=NfeWireCodec.NfcomNamespace;var doc=XDocument.Parse(Encoding.UTF8.GetString(wire.Body));var operation=doc.Descendants(ns+"nfcomDownloadContab").Single();
  var payload=Assert.Single(operation.Elements());Assert.Equal(ns+"distNFComSC",payload.Name);Assert.Equal("1.00",(string?)payload.Attribute("versao"));Assert.DoesNotContain(doc.Descendants(),x=>x.Name.LocalName=="pXml");
  var wsdl=XDocument.Load(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../docs/sources/phase9/nfcom.wsdl")));
  XNamespace xs="http://www.w3.org/2001/XMLSchema";var schemas=new XmlSchemaSet();using var reader=wsdl.Descendants(xs+"schema").Single().CreateReader();schemas.Add(null,reader);schemas.Compile();var declaration=(XmlSchemaElement)schemas.GlobalElements[new XmlQualifiedName("nfcomDownloadContab",ns.NamespaceName)]!;var type=Assert.IsType<XmlSchemaComplexType>(declaration.ElementSchemaType);var sequence=Assert.IsType<XmlSchemaSequence>(type.ContentTypeParticle);Assert.IsType<XmlSchemaAny>(Assert.Single(sequence.Items.Cast<XmlSchemaObject>()));
 }
 [Theory][InlineData(ActorFilter.Issuer)][InlineData(ActorFilter.Recipient)][InlineData(ActorFilter.Either)][InlineData(ActorFilter.Both)]
 public void ActorFilterAndKeysAreSerialized(ActorFilter actor)
 {var wire=NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(mode:PayloadMode.KeysOnly) with{Actor=actor},"12345678000195","42");string xml=Encoding.UTF8.GetString(wire.Body);Assert.Contains($"<indAtor>{(int)actor}</indAtor>",xml);Assert.Contains("<indXML>0</indXML>",xml);}
 [Theory][InlineData(55)][InlineData(57)][InlineData(65)][InlineData(67)]public void OtherModelsCannotUseNfcomService(int model)
 {Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(key:DfeParserTests.Key(model)),"12345678000195","42"));}
 [Fact]public void UnsupportedOtherParticipantIsRejectedBeforeSending()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request() with{Actor=ActorFilter.OtherParticipant},"12345678000195","42"));
 [Fact]public void NonScStateIsRejected()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(),"12345678000195","35"));
 [Fact]public void AccountantCredentialIsRequired()
 {Assert.Throws<NfeException>(()=>NfeCredentialSource.ValidateIdentity(NfeSources.SantaCatarinaNfcom,"12345678000195","12345678000195",CertificateUse.Participant));NfeCredentialSource.ValidateIdentity(NfeSources.SantaCatarinaNfcom,"12345678000195","12345678901",CertificateUse.Accountant);}
 [Fact]public async Task NfcomFlagAndStateAreCheckedBeforeNetwork()
 {await using var h=await Harness.Create();var c=await h.Db.Companies.SingleAsync();c.EnabledDocuments=EnabledDocuments.NFe;await h.Db.SaveChangesAsync();Assert.Equal("COMPANY_NFE_DISABLED",Assert.Single((await h.Run()).Rows).Code);Assert.Equal(0,h.Transport.Calls);c=await h.Db.Companies.SingleAsync();c.State="SP";c.EnabledDocuments=EnabledDocuments.NFCom;await h.Db.SaveChangesAsync();Assert.Equal("SC_REQUIRES_SC_COMPANY_AND_PRODUCTION",Assert.Single((await h.Run()).Rows).Code);}
 [Fact]public async Task ProviderModelCursorAndCompanyListsAreIsolated()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();Assert.Equal(DocumentModel.NFCom,(await h.Db.DocumentSyncStates.SingleAsync()).Model);Assert.Equal(DocumentModel.NFCom,(await h.Db.ProcessingJobItems.SingleAsync()).Model);Assert.Single(await h.Service.StatesAsync(source:NfeSources.SantaCatarinaNfcom));Assert.Empty(await h.Service.StatesAsync());var c=await h.Db.Companies.SingleAsync();c.EnabledDocuments=EnabledDocuments.NFCom;await h.Db.SaveChangesAsync();Assert.Single(await h.Service.CompaniesAsync(source:NfeSources.SantaCatarinaNfcom));Assert.Empty(await h.Service.CompaniesAsync());}
 [Fact]public async Task SubstitutionEventIsIndexedWithoutCancellingOriginal()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2",document:DfeParserTests.Event(62).Replace("110111","240140").Replace("<tpAmb>2","<tpAmb>1")));await h.Run();Assert.Equal(DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);Assert.Equal("240140",(await h.Db.FiscalDocumentEvents.SingleAsync()).EventTypeCode);}
 [Fact]public async Task CrossModelPayloadHoldsPageAndKeepsNfcomCursor()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:DfeParserTests.Document(55).Replace("<tpAmb>2","<tpAmb>1")));await h.Run();Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public void NfcomKeyLookupHasNoCursorOrActor()
 {string xml=Encoding.UTF8.GetString(NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(key:Key),"12345678000195","42").Body);Assert.Contains(Key,xml);Assert.DoesNotContain("ultNuNSU",xml);Assert.DoesNotContain("indAtor",xml);}

 [Fact]public void AlphaCnpjAndKeyAreNotConvertedToNumbers()
 {string tax="12ABC34501DE35",key=DfeParserTests.Key(62,tax:tax);var wire=NfeWireCodec.Build(NfeSources.SantaCatarinaNfcom,Request(key:key),tax,"42");string xml=Encoding.UTF8.GetString(wire.Body);Assert.Contains("<CNPJ>"+tax+"</CNPJ>",xml);Assert.Contains(key,xml);}
 [Theory][InlineData(ActorFilter.Recipient)][InlineData(ActorFilter.Both)]
 public async Task ReturnedFullNoteMustMatchSelectedRole(ActorFilter actor)
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());var r=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,Actor:actor));Assert.Equal("PAYLOAD_COMPANY_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task DisabledNetworkStillReplaysDurableNfcomReceipt()
 {await using var h=await Harness.Create();var bytes=Sc();h.Db.NfeDistributionReceipts.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,ProviderCode=h.Source,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,ResponseBytes=bytes,ResponseHash=Convert.ToHexString(SHA256.HashData(bytes))});await h.Db.SaveChangesAsync();h.Transport.Error="EXTERNAL_REQUESTS_DISABLED";await h.Run();Assert.True((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task Block657CannotBeBypassedByActorOrKey()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(657));await h.Run();var r=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,Actor:ActorFilter.Issuer));Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single((await h.Run(key:Key)).Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task UnexpectedNfcomEventStaysPending()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:DfeParserTests.Event(62).Replace("110111","110110").Replace("<tpAmb>2","<tpAmb>1")));var r=await h.Run();Assert.Equal("PAYLOAD_EVENT_UNSUPPORTED_INBOX_RETAINED",Assert.Single(r.Rows).Code);Assert.Empty(await h.Db.FiscalDocumentEvents.ToListAsync());Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Theory][InlineData("version")][InlineData("batch-version")][InlineData("batch-model")][InlineData("batch-cursor")]
 public void NfeLayoutsCannotBeMistakenForNfcom(string field)
 {var response=XDocument.Parse(Encoding.UTF8.GetString(Sc()));if(field=="version")response.Descendants().Single(x=>x.Name.LocalName=="retDistNFComSC").SetAttributeValue("versao","2.02");else {string lot=$"<loteDistNFComSC versao='{(field=="batch-version"?"2.02":"1.00")}'><distNFComSC NSU='{(field=="batch-cursor"?"2":"1")}' chAcesso='{(field=="batch-model"?DfeParserTests.Key(55):Key)}'>{Doc}</distNFComSC></loteDistNFComSC>";response.Descendants().Single(x=>x.Name.LocalName=="loteDistComp").Value=Zip(lot);}Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Encoding.UTF8.GetBytes(response.ToString()),Request()));}

 [Theory][InlineData("cUF","35")][InlineData("CPF","52998224725")][InlineData("ultNuNSU","999")]
 public void ContradictoryReturnEchoIsRejected(string name,string value)
 {var x=XDocument.Parse(Encoding.UTF8.GetString(Sc()));x.Descendants().Single(x=>x.Name.LocalName=="retDistNFComSC").Add(new XElement(XName.Get(name,NfeWireCodec.NfcomNamespace),value));Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarinaNfcom,Encoding.UTF8.GetBytes(x.ToString()),Request()));}

 [Theory][InlineData("nfcomProc")][InlineData("NFComProc")]
 public async Task BothPublishedProcessedWrappersAreArchivedWithoutRenaming(string root)
 {await using var h=await Harness.Create();string xml=Doc.Replace("nfcomProc",root);h.Transport.Enqueue(Sc(document:xml));var result=await h.Run();Assert.Equal("NFE_SYNCHRONIZED",Assert.Single(result.Rows).Code);var doc=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal(DocumentModel.NFCom,doc.Model);Assert.Equal("1422600000000001",doc.ProtocolNumber);string stored=Encoding.UTF8.GetString(await h.Files.ReadAsync(Assert.Single(h.Files.EnumeratePhysicalFiles()),doc.XmlHash,default));Assert.Contains("<"+root,stored);Assert.Equal(doc.XmlHash,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stored)))) ;Assert.Equal("LOCAL_XML_PRESENT",Assert.Single((await h.Run(key:Key)).Rows).Code);}
 [Theory][InlineData("110111",135,true)][InlineData("110111",136,false)][InlineData("240150",134,false)][InlineData("240140",134,false)][InlineData("240151",134,false)][InlineData("240161",134,false)][InlineData("240162",134,false)]
 public async Task DistributedEventsKeepTheirMeaning(string type,int status,bool cancels)
 {await using var h=await Harness.Create();string ev=DfeParserTests.Event(62,code:status,width:3).Replace("110111",type).Replace("<tpAmb>2","<tpAmb>1");h.Transport.Enqueue(Sc(document:ev));await h.Run();var saved=await h.Db.FiscalDocumentEvents.SingleAsync();Assert.Equal(type,saved.EventTypeCode);Assert.Equal(status,saved.ResponseCode);Assert.Null(saved.FiscalDocumentId);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2"));await h.Run();Assert.Equal(cancels?DocumentStatus.Cancelled:DocumentStatus.Authorized,(await h.Db.FiscalDocuments.SingleAsync()).Status);Assert.Equal((await h.Db.FiscalDocuments.SingleAsync()).Id,(await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);}
 [Theory][InlineData("site")][InlineData("protocol")][InlineData("fiscal-namespace")][InlineData("inner-name")]
 public async Task NfcomFiscalIdentityFailuresPreserveReceiptAndCursor(string field)
 {await using var h=await Harness.Create();string xml=field switch{"site"=>Doc.Replace("<nSiteAutoriz>1","<nSiteAutoriz>2"),"protocol"=>Doc.Replace("1422600000000001","142260000000001"),"fiscal-namespace"=>Doc.Replace(DfeParser.NfcomNamespace,DfeParser.NfeNamespace),_=>Doc.Replace("<NFCom ","<NFCOM ").Replace("</NFCom>","</NFCOM>")};h.Transport.Enqueue(Sc(document:xml));await h.Run();Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);}
 [Fact]public void NfcomCapabilitiesDoNotClaimNationalOrDateRangeSupport()
 {var p=new NfeSoapProvider(NfeSources.SantaCatarinaNfcom,new Credentials(),new Transport());Assert.Equal(DocumentModel.NFCom,Assert.Single(p.Capabilities.Models));Assert.Equal(CertificateRequirement.AccountantWithLink,p.Capabilities.CertificateRequirement);Assert.True(p.Capabilities.SupportsNsu);Assert.True(p.Capabilities.SupportsKeyLookup);Assert.False(p.Capabilities.SupportsDateRange);Assert.False(p.CanHandle(Request() with{Environment=FiscalEnvironment.Homologation}));Assert.False(p.CanHandle(Request() with{Model=DocumentModel.NFCe}));Assert.True(p.CanHandle(Request()));}
 [Fact]public async Task NfeAndNfceReceiptsAreNotReplayedAsNfcom()
 {await using var h=await Harness.Create();h.Db.NfeDistributionReceipts.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,ProviderCode=NfeSources.SantaCatarinaNfce,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,ResponseBytes=[1,2,3],ResponseHash="INVALID"});await h.Db.SaveChangesAsync();h.Transport.Enqueue(Sc());var result=await h.Run();Assert.Equal("NFE_SYNCHRONIZED",Assert.Single(result.Rows).Code);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync(x=>x.ProviderCode==NfeSources.SantaCatarinaNfce)).Applied);Assert.True((await h.Db.NfeDistributionReceipts.SingleAsync(x=>x.ProviderCode==NfeSources.SantaCatarinaNfcom)).Applied);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task NfcomAutoSelectsAccountantWithoutOwnLinkButStillValidatesActualPfx()
 {await using var h=await Harness.Create();var c=new WgsDfe.Domain.Certificate{OrganizationId=h.Org,Label="Synthetic",HolderTaxId="12345678901",Use=CertificateUse.Accountant,ValidFromUtc=DateTime.UtcNow.AddDays(-1),ValidToUtc=DateTime.UtcNow.AddDays(1),SecureStoreReference="SYNTHETIC",Thumbprint=new string('A',64)};h.Db.Certificates.Add(c);h.Db.CertificateAssociations.Add(new(){OrganizationId=h.Org,CompanyId=h.Company,CertificateId=c.Id,ProviderCode=NfeSources.SantaCatarinaNfce});await h.Db.SaveChangesAsync();var keys=new SelectedKeys();var source=new NfeCredentialSource(h.Db,keys,new ProviderOptions{EnableExternalRequests=true});var e=await Assert.ThrowsAsync<NfeException>(()=>source.OpenAsync(Request() with{OrganizationId=h.Org,CompanyId=h.Company},NfeSources.SantaCatarinaNfcom,default));Assert.Equal(c.Id,keys.Opened);Assert.Equal("ACCOUNTANT_CERTIFICATE_REQUIRED",e.Code);}
 private sealed class SelectedKeys:WgsDfe.Application.Certificates.ICertificateKeySource
 {public Guid? Opened;public Task<X509Certificate2> OpenAsync(Guid id,CancellationToken ct=default){Opened=id;return Task.FromResult(Certificate());}}
 private static X509Certificate2 Certificate(){using var rsa=RSA.Create(2048);return new CertificateRequest("CN=SYNTHETIC",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));}
 private sealed class Credentials:INfeCredentialSource{public Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct)=>Task.FromResult(new NfeCredential(request.CompanyTaxId??"12345678000195","42",Certificate()));}
 private sealed class FakeClock:TimeProvider{private DateTimeOffset time=new(2026,10,16,13,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>time;public void Advance(TimeSpan span)=>time+=span;}
 private sealed class Transport:INfeTransport
 {
  private readonly Queue<byte[]> responses=new();public int Calls;public List<NfeWireRequest> Requests{get;}=[];public string? Error;public Action? AfterReceive;
  public void Enqueue(byte[] bytes)=>responses.Enqueue(bytes);
  public Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct){Calls++;Requests.Add(request);if(Error is not null)throw new NfeException(Error);var bytes=responses.Dequeue();AfterReceive?.Invoke();return Task.FromResult(bytes);}
 }
 [Theory][InlineData(false,10,10,1)][InlineData(false,1,30,1)][InlineData(false,11,11,0)]
 async Task Period_is_local_bounded_and_never_resets_nsu(bool national,int first,int last,int expected)
 {
  Assert.False(national);await using var h=await Harness.Create();
  h.Transport.Enqueue(Sc());
  var from=new DateOnly(2026,9,first);var to=new DateOnly(2026,9,last);
  var progress=new List<NfeProgress>();
  var request=new NfeSyncRequest([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0,From:from,To:to);
  var result=await h.Service.SynchronizeAsync(request,new PeriodProgress(progress));
  Assert.Equal("NFE_SYNCHRONIZED",Assert.Single(result.Rows).Code);
  var committed=Assert.Single(progress,x=>x.Code=="NFE_PAGE_COMMITTED");
  Assert.Equal(expected,committed.InPeriod);Assert.Equal(1-expected,committed.OutsidePeriod);
  using var gate=new ArchiveGate();var archive=new ArchiveService(h.Db,new DfeParser(),h.Files,gate);
  var page=await archive.SearchAsync(new WgsDfe.Application.Archive.ArchiveQuery(CompanyId:h.Company,From:from,To:to));
  Assert.Equal(expected,page.Total);Assert.All(page.Items,x=>Assert.InRange(DateOnly.Parse(x.IssueDate!),from,to));
  Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
  int calls=h.Transport.Calls;await h.Service.SynchronizeAsync(request);Assert.Equal(calls,h.Transport.Calls);
  h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(117));
  await h.Service.SynchronizeAsync(request);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);
  Assert.DoesNotContain("2026-09-",Encoding.UTF8.GetString(h.Transport.Requests.Last().Body));
 }
 [Fact]public async Task Full_backlog_page_keeps_period_count_unique_and_advances_all_nsus()
 {
  await using var h=await Harness.Create();var soap=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"50",count:50)));
  var compressed=soap.Descendants().Single(x=>x.Name.LocalName=="loteDistComp");
  using(var bytes=new MemoryStream(Convert.FromBase64String(compressed.Value)))using(var gzip=new GZipStream(bytes,CompressionMode.Decompress))
  {var lot=XDocument.Load(gzip);var entry=Assert.Single(lot.Root!.Elements());entry.Remove();for(int i=1;i<=50;i++){var clone=new XElement(entry);clone.SetAttributeValue("NSU",i.ToString());lot.Root.Add(clone);}compressed.Value=Zip(lot.ToString());}
  h.Transport.Enqueue(Encoding.UTF8.GetBytes(soap.ToString()));h.Transport.Enqueue(Sc(117));var progress=new List<NfeProgress>();
  var result=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0,From:new(2026,9,10),To:new(2026,9,10)),new PeriodProgress(progress));
  Assert.Equal(2,Assert.Single(result.Rows).Pages);Assert.Equal("50",result.Rows[0].Nsu);Assert.Equal(50,result.Rows[0].Documents);
  var final=progress.Last(x=>x.Code=="NFE_PAGE_COMMITTED");Assert.Equal(1,final.InPeriod);Assert.Equal(1,final.NewXml);Assert.Equal(49,final.ExistingXml);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());
 }
 private sealed class PeriodProgress(List<NfeProgress> values):IProgress<NfeProgress>{public void Report(NfeProgress value)=>values.Add(value);}
 private sealed class Harness:IAsyncDisposable
 {
  public string Directory{get;}=Path.Combine(Path.GetTempPath(),"wgs-nfcom-test-"+Guid.NewGuid().ToString("N"));public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public string Source{get;private set;}=NfeSources.SantaCatarinaNfcom;
  public WgsDbContext Db{get;private set;}=null!;public ArchiveFileStore Files{get;private set;}=null!;public NfeSyncService Service{get;private set;}=null!;public Transport Transport{get;}=new();public FakeClock Clock{get;}=new();private ArchiveGate archiveGate=new();private NfeSyncGate syncGate=new();
  public static async Task<Harness> Create(string source=NfeSources.SantaCatarinaNfcom)
  {var h=new Harness{Source=source};System.IO.Directory.CreateDirectory(h.Directory);h.Db=DatabaseFactory.Create(Path.Combine(h.Directory,"test.db"),h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Synthetic"});var company=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Empresa sintética",State="SC"};h.Company=company.Id;h.Db.Companies.Add(company);await h.Db.SaveChangesAsync();h.Files=new(new(h.Directory,h.Org));var parser=new DfeParser();var archive=new ArchiveService(h.Db,parser,h.Files,h.archiveGate);var provider=new NfeSoapProvider(source,new Credentials(),h.Transport);h.Service=new(h.Db,[provider],parser,archive,h.archiveGate,h.syncGate,h.Clock);return h;}
  public Task<NfeSyncResult> Run(PayloadMode mode=PayloadMode.FullXml,string? key=null)=>Service.SynchronizeAsync(new([Company],Source,FiscalEnvironment.Production,PayloadMode:mode,AccessKey:key));
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();archiveGate.Dispose();syncGate.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(Directory,true);}
 }
}
