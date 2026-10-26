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
public sealed class NfeTests
{
 private static string Key=>DfeParserTests.Key();
 private static string Doc=>DfeParserTests.Document().Replace("<tpAmb>2","<tpAmb>1");
 private static DocumentRequest Request(string source=NfeSources.SantaCatarina,string nsu="0",string? key=null,PayloadMode mode=PayloadMode.FullXml)=>new(Guid.NewGuid(),Guid.NewGuid(),DocumentModel.NFe,FiscalEnvironment.Production,ActorFilter.Either,mode,nsu,key,null,null,Guid.NewGuid(),"12345678000195");
 private static string Zip(string xml){using var output=new MemoryStream();using(var gzip=new GZipStream(output,CompressionMode.Compress,true))gzip.Write(Encoding.UTF8.GetBytes(xml));return Convert.ToBase64String(output.ToArray());}
 private static byte[] Soap(string source,string ret)
 {
  string ws=source==NfeSources.SantaCatarina?NfeWireCodec.ScNamespace:NfeWireCodec.NationalNamespace,op=source==NfeSources.SantaCatarina?"NfeDownloadContab":"nfeDistDFeInteresse";
  return Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><{op}Response xmlns='{ws}'><{op}Result>{ret}</{op}Result></{op}Response></s:Body></s:Envelope>");
 }
 private static byte[] Sc(int status=118,string nsu="1",string? document=null,int? count=null,bool keys=false)
 {
  string lot=$"<loteDistNFeSC versao='2.00'><distNFeSC NSU='{nsu}' chAcesso='{Key}'>{(keys?"":document??Doc)}</distNFeSC></loteDistNFeSC>";
  string ret=$"<retDistNFeSC xmlns='{NfeWireCodec.ScNamespace}' versao='2.02'><tpAmb>1</tpAmb><cStat>{status}</cStat><xMotivo>Teste sintético</xMotivo><dhResp>2026-10-16T10:00:00-03:00</dhResp><CNPJ>12345678000195</CNPJ>{(status==118?$"<ultNuNSURet>{nsu}</ultNuNSURet><qtDfeRet>{count??1}</qtDfeRet><loteDistComp>{Zip(lot)}</loteDistComp>":"")}</retDistNFeSC>";
  return Soap(NfeSources.SantaCatarina,ret);
 }
 private static byte[] National(int status=138,string nsu="1",string max="1",string? xml=null,string schema="procNFe_v4.00.xsd")=>Soap(NfeSources.National,$"<retDistDFeInt xmlns='{NfeWireCodec.NfeNamespace}' versao='1.01'><tpAmb>1</tpAmb><cStat>{status}</cStat><xMotivo>Teste</xMotivo><dhResp>2026-10-16T10:00:00-03:00</dhResp><ultNSU>{nsu}</ultNSU><maxNSU>{max}</maxNSU>{(status==138?$"<loteDistDFeInt><docZip NSU='{nsu}' schema='{schema}'>{Zip(xml??Doc)}</docZip></loteDistDFeInt>":"")}</retDistDFeInt>");
 [Fact]public void ScRequest_UsesActualWsdlOperationAndOfficialXsd()
 {
  var wire=NfeWireCodec.Build(NfeSources.SantaCatarina,Request(),"12345678000195","42");Assert.EndsWith("/NfeDownloadContab",wire.SoapAction);Assert.Equal("https://satnfe.sef.sc.gov.br/ws/distribuicao/nfedownloadV2.asmx",wire.Endpoint.AbsoluteUri);
  var xml=XDocument.Parse(Encoding.UTF8.GetString(wire.Body));XNamespace ns=NfeWireCodec.ScNamespace;var payload=xml.Descendants(ns+"distNFeSC").Single();
  var schema=new XmlSchemaSet();string path=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../docs/sources/sefsc-nfe/schemas/distNFeSC_v2.0.xsd"));schema.Add(NfeWireCodec.ScNamespace,path);new XDocument(payload).Validate(schema,(_,e)=>throw new Exception(e.Message));
  Assert.Equal("3",payload.Descendants(ns+"indAtor").Single().Value);Assert.Equal("1",payload.Descendants(ns+"indXML").Single().Value);Assert.DoesNotContain(xml.Descendants(),x=>x.Name.LocalName.Contains("Certificate",StringComparison.Ordinal));
 }
 [Fact]public void NationalRequest_HasPaddedNsuAndNoInventedPeriod()
 {var wire=NfeWireCodec.Build(NfeSources.National,Request(NfeSources.National,"42"),"12345678000195","42");var xml=Encoding.UTF8.GetString(wire.Body);Assert.Contains("000000000000042",xml);Assert.Contains("nfeDistDFeInteresse",xml);Assert.DoesNotContain("indAtor",xml);Assert.DoesNotContain("dIni",xml);}
 [Theory][InlineData(NfeSources.SantaCatarina)][InlineData(NfeSources.National)]public void KeyRequest_DoesNotCarryDistributionCursor(string source)
 {var wire=NfeWireCodec.Build(source,Request(source,key:Key),"12345678000195","42");string xml=Encoding.UTF8.GetString(wire.Body);Assert.Contains(Key,xml);Assert.DoesNotContain("ultNSU",xml);Assert.DoesNotContain("ultNuNSU",xml);}
 [Fact]public void ScHomologation_IsExplicitlyUnsupported()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.SantaCatarina,Request() with{Environment=FiscalEnvironment.Homologation},"12345678000195","42"));
 [Theory][InlineData(ActorFilter.Issuer,PayloadMode.FullXml)][InlineData(ActorFilter.Either,PayloadMode.KeysOnly)]public void NationalFilterChangesCannotCreateNewStream(ActorFilter actor,PayloadMode mode)=>Assert.Throws<NfeException>(()=>NfeWireCodec.Build(NfeSources.National,Request() with{Actor=actor,PayloadMode=mode},"12345678000195","42"));
 [Fact]public void ScFullPage_ExtractsOriginalFiscalNamespace()
 {var page=NfeWireCodec.Decode(NfeSources.SantaCatarina,Sc(),Request());Assert.Equal(118,page.Status);Assert.Equal("1",page.NextNsu);Assert.Equal(TimeSpan.FromHours(12),page.Delay);Assert.False(page.HasMore);Assert.Equal(Key,new DfeParser().Parse(Assert.Single(page.Items).Xml!).Key.Value);}
 [Fact]public void ScKeysOnly_DoesNotInventXml()
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarina,Sc(keys:true),Request(mode:PayloadMode.KeysOnly));Assert.Null(Assert.Single(p.Items).Xml);Assert.Equal(Key,p.Items[0].Key);}
 [Fact]public void ScEmpty_DoesNotResetCursor()
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarina,Sc(117),Request(nsu:"42"));Assert.Equal("42",p.NextNsu);Assert.Empty(p.Items);}
 [Fact]public void NationalDocZip_AndCatchupWait()
 {var p=NfeWireCodec.Decode(NfeSources.National,National(),Request());Assert.Equal(Encoding.UTF8.GetBytes(Doc),Assert.Single(p.Items).Xml);Assert.Equal(TimeSpan.FromHours(1),p.Delay);Assert.False(p.HasMore);}
 [Fact]public void NationalMore_ContinuesWithoutOneHourDelay()
 {var p=NfeWireCodec.Decode(NfeSources.National,National(max:"2"),Request());Assert.True(p.HasMore);Assert.Equal(TimeSpan.Zero,p.Delay);}
 [Theory][InlineData(110,1)][InlineData(117,12)][InlineData(657,12)][InlineData(8002,1)][InlineData(632,1)]public void ScOfficialRejections_KeepCursorAndRespectWait(int status,int hours)
 {var p=NfeWireCodec.Decode(NfeSources.SantaCatarina,Sc(status),Request());Assert.Equal(status,p.Status);Assert.Equal(TimeSpan.FromHours(hours),p.Delay);if(status!=117)Assert.Null(p.NextNsu);}
 [Theory][InlineData(137)][InlineData(656)][InlineData(589)]public void NationalRejections_WaitOneHour(int status)
 {var p=NfeWireCodec.Decode(NfeSources.National,National(status,"0","0"),Request());Assert.Equal(TimeSpan.FromHours(1),p.Delay);}
 [Theory][InlineData("status")][InlineData("env")][InlineData("namespace")][InlineData("company")][InlineData("count")][InlineData("cursor")]
 public void InvalidEnvelopeOrCursor_IsRejected(string change)
 {
  string xml=Encoding.UTF8.GetString(Sc());xml=change switch{"status"=>xml.Replace("<cStat>118</cStat>","<cStat>118</cStat><cStat>118</cStat>"),"env"=>xml.Replace("<tpAmb>1","<tpAmb>2"),"namespace"=>xml.Replace(NfeWireCodec.ScNamespace,"urn:invalid"),"company"=>xml.Replace("<CNPJ>12345678000195","<CNPJ>12345678000276"),"count"=>xml.Replace("<qtDfeRet>1","<qtDfeRet>2"),_=>xml.Replace("<ultNuNSURet>1","<ultNuNSURet>0")};Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarina,Encoding.UTF8.GetBytes(xml),Request()));
 }
 [Fact]public void SoapFault_IsStaticAndDoesNotExposeServerMessage()
 {var bytes=Encoding.UTF8.GetBytes("<s:Envelope xmlns:s='http://schemas.xmlsoap.org/soap/envelope/'><s:Body><s:Fault><faultstring>SENSITIVE</faultstring></s:Fault></s:Body></s:Envelope>");var e=Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarina,bytes,Request()));Assert.Equal("SOAP_FAULT",e.Code);Assert.DoesNotContain("SENSITIVE",e.Message);}
 [Fact]public void Dtd_IsRejected()=>Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarina,Encoding.UTF8.GetBytes("<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///etc/passwd'>]><x>&a;</x>"),Request()));
 [Fact]public void ExpandedGzip_IsBounded()
 {string xml=new(' ',10*1024*1024+1);Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.National,National(xml:xml),Request()));}
 [Theory][InlineData("badgzip")][InlineData("truncated")][InlineData("depth")]
 public void MalformedTransportPayload_IsRejected(string change)
 {
  string ret=Encoding.UTF8.GetString(Sc());if(change=="depth")ret=new string(' ',1)+string.Concat(Enumerable.Repeat("<a>",90))+string.Concat(Enumerable.Repeat("</a>",90));else{var x=XDocument.Parse(ret);var e=x.Descendants(XName.Get("loteDistComp",NfeWireCodec.ScNamespace)).Single();e.Value=change=="badgzip"?"bm90LWd6aXA=":Convert.ToBase64String(Convert.FromBase64String(e.Value)[..15]);ret=x.ToString();}Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.SantaCatarina,Encoding.UTF8.GetBytes(ret),Request()));
 }
 [Theory][InlineData(NfeSources.SantaCatarina,CertificateUse.Participant,"12345678000195")][InlineData(NfeSources.National,CertificateUse.Accountant,"12345678000195")][InlineData(NfeSources.National,CertificateUse.Participant,"00000000000191")][InlineData(NfeSources.National,CertificateUse.Participant,"12345678901")]
 public void CertificatePurposeAndIdentity_AreNotInterchangeable(string source,CertificateUse use,string holder)=>Assert.Throws<NfeException>(()=>NfeCredentialSource.ValidateIdentity(source,"12345678000195",holder,use));
 [Fact]public void ParticipantBranch_SameCnpjRootAllowed()=>NfeCredentialSource.ValidateIdentity(NfeSources.National,"12345678000276","12345678000195",CertificateUse.Participant);
 [Fact]public async Task ExternalRequestsDisabled_NeverStartsHttp()
 {using var gate=new NfeNetworkGate();var transport=new NfeTransport(new ProviderOptions(),gate);using var cert=Certificate();var e=await Assert.ThrowsAsync<NfeException>(()=>transport.SendAsync(NfeWireCodec.Build(NfeSources.SantaCatarina,Request(),"12345678000195","42"),cert,default));Assert.Equal("EXTERNAL_REQUESTS_DISABLED",e.Code);}
 [Fact]public async Task TransportRejectsUnofficialEndpoint()
 {using var gate=new NfeNetworkGate();var transport=new NfeTransport(new ProviderOptions{EnableExternalRequests=true},gate);using var cert=Certificate();await Assert.ThrowsAsync<NfeException>(()=>transport.SendAsync(new(new Uri("https://example.invalid"),"x",[]),cert,default));}
 [Fact]public async Task AutomaticContinuationPassesTheOldTenBatchLimit()
 {
  await using var h=await Harness.Create(NfeSources.National);
  for(int n=1;n<=12;n++)h.Transport.Enqueue(National(nsu:n.ToString(),max:"12"));
  var result=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0));
  var row=Assert.Single(result.Rows);Assert.Equal("NFE_SYNCHRONIZED",row.Code);Assert.Equal(12,row.Pages);Assert.Equal("12",row.Nsu);Assert.Equal(12,h.Transport.Calls);
 }
 [Fact]public async Task ExplicitBatchLimitStillStopsAndCanResumeAutomatically()
 {
  await using var h=await Harness.Create(NfeSources.National);h.Transport.Enqueue(National(nsu:"1",max:"2"));h.Transport.Enqueue(National(nsu:"2",max:"2"));
  var first=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:1));Assert.Equal("NFE_PAGE_COMMITTED",Assert.Single(first.Rows).Code);
  var next=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0));Assert.Equal("NFE_SYNCHRONIZED",Assert.Single(next.Rows).Code);Assert.Equal("2",next.Rows[0].Nsu);Assert.Equal(2,h.Transport.Calls);
 }
 [Fact]public async Task LongDistributionReports115AdvancingBatchesAndDeduplicatesXml()
 {
  await using var h=await Harness.Create(NfeSources.National);for(int n=1;n<=115;n++)h.Transport.Enqueue(National(nsu:n.ToString(),max:"115"));
  var events=new List<NfeProgress>();var result=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0),new ImmediateProgress(events.Add));
  Assert.Equal(115,Assert.Single(result.Rows).Pages);var committed=events.Where(x=>x.Code=="NFE_PAGE_COMMITTED").ToArray();Assert.Equal(115,committed.Length);
  Assert.Equal(Enumerable.Range(1,115).Select(x=>x.ToString()),committed.Select(x=>x.Row!.Nsu));Assert.Equal(1,committed[^1].NewXml);Assert.Equal(114,committed[^1].ExistingXml);Assert.Equal(115,committed[^1].Row!.Documents);Assert.Equal(1,committed[^1].InPeriod);Assert.Equal(0,committed[^1].OutsidePeriod);
  Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(115,await h.Db.NfeDistributionReceipts.CountAsync());
  string diagnostic=await File.ReadAllTextAsync(Assert.Single(System.IO.Directory.GetFiles(Path.Combine(h.Directory,"Logs"),"sync-*.jsonl")));Assert.Equal(115,diagnostic.Split('\n',StringSplitOptions.RemoveEmptyEntries).Length);Assert.DoesNotContain("12345678000195",diagnostic);Assert.DoesNotContain("<NFe",diagnostic);Assert.DoesNotContain("Empresa sintética",diagnostic);Assert.Contains("NFE_SYNCHRONIZED",diagnostic);
 }
 [Fact]public async Task RepeatedResponseCannotLoopOrEraseCommittedProgress()
 {
  await using var h=await Harness.Create(NfeSources.National);h.Transport.Enqueue(National(nsu:"1",max:"2"));h.Transport.Enqueue(National(nsu:"1",max:"2"));
  var result=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production,MaxPages:0));
  Assert.InRange(h.Transport.Calls,1,2);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.True(result.Rows[0].Documents>=1);
 }
 [Fact]public async Task TransientFailureRequiresSafeRetryAndThenSucceedsAfterCooldown()
 {
  await using var h=await Harness.Create(NfeSources.National);h.Transport.Error="NETWORK_TIMEOUT";var failed=await h.Run();Assert.Equal("NETWORK_TIMEOUT",failed.Rows[0].Code);
  h.Transport.Error=null;h.Transport.Enqueue(National());var wait=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",wait.Rows[0].Code);Assert.NotNull(wait.Rows[0].NextAllowedAtLocal);
  h.Clock.Advance(TimeSpan.FromHours(1));var retry=await h.Run();Assert.Equal("NFE_SYNCHRONIZED",retry.Rows[0].Code);Assert.Equal(2,h.Transport.Calls);
 }
 private sealed class ImmediateProgress(Action<NfeProgress> report):IProgress<NfeProgress>{public void Report(NfeProgress value)=>report(value);}
 [Fact]public async Task Sync_CommitsAcervoLinksReceiptAndCursor()
 {
  await using var h=await Harness.Create();h.Transport.Enqueue(Sc());var r=await h.Run();var row=Assert.Single(r.Rows);Assert.Equal("NFE_SYNCHRONIZED",row.Code);Assert.Equal("1",row.Nsu);Assert.Equal(1,row.Documents);
  var d=await h.Db.FiscalDocuments.SingleAsync();Assert.Equal(NfeSources.SantaCatarina,d.SourceProviderCode);Assert.Equal(1,await h.Db.FiscalDocumentCompanies.CountAsync());Assert.True((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Equal(TimeSpan.FromHours(12),(await h.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc-h.Clock.GetUtcNow().UtcDateTime);Assert.Equal("SEF_SC_LINK_ACCEPTED",(await h.Db.Companies.SingleAsync()).AccountantLinkStatusCode);
 }
 [Fact]public async Task RepeatBeforeCooldown_DoesNotSendAnotherRequest()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();var r=await h.Run();Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task AfterCooldown_UsesSavedCursorAndNeverResetsToZero()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(117));var r=await h.Run();Assert.Equal("1",Assert.Single(r.Rows).Nsu);Assert.Contains("<ultNuNSU>1</ultNuNSU>",Encoding.UTF8.GetString(h.Transport.Requests.Last().Body));}
 [Fact]public async Task KeysOnly_CommitsKnownKeysButNoFakeFullDocument()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(keys:true));var r=await h.Run(mode:PayloadMode.KeysOnly);Assert.Equal(1,Assert.Single(r.Rows).Summaries);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(Availability.Pending,(await h.Db.KnownDocumentKeys.SingleAsync()).Availability);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}
 [Fact]public async Task SummaryNational_RemainsPendingWithDurableOriginal()
 {await using var h=await Harness.Create(NfeSources.National);string xml=$"<resNFe xmlns='{NfeWireCodec.NfeNamespace}' versao='1.01'><chNFe>{Key}</chNFe><CNPJ>12345678000195</CNPJ><xNome>Teste</xNome><dhEmi>2026-09-10T11:12:13-03:00</dhEmi><vNF>1.00</vNF><cSitNFe>1</cSitNFe><nProt>142260000000001</nProt><dhRecbto>2026-09-10T11:12:14-03:00</dhRecbto></resNFe>";h.Transport.Enqueue(National(xml:xml,schema:"resNFe_v1.01.xsd"));var r=await h.Run();Assert.Equal(1,Assert.Single(r.Rows).Summaries);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.NotEmpty((await h.Db.NfeDistributionReceipts.SingleAsync()).ResponseBytes);}
 [Fact]public async Task InvalidFiscalXml_RetainsInboxAndCursorForOfflineRetry()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:Doc.Replace("123456789.01","invalid")));var r=await h.Run();Assert.Contains("INBOX_RETAINED",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());await h.Run();Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task WrongEnvironmentInPayload_HoldsEntirePage()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc(document:DfeParserTests.Document()));var r=await h.Run();Assert.Equal("PAYLOAD_IDENTITY_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());}
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
 [Fact]public async Task NationalConsumptionBlock_PreventsKeyBypass()
 {await using var h=await Harness.Create(NfeSources.National);h.Transport.Enqueue(National(656,"0","0"));await h.Run();Assert.Single(await h.Db.ApplicationSettings.ToListAsync());var r=await h.Run(key:Key);Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(r.Rows).Code);Assert.Equal(1,h.Transport.Calls);}
 [Fact]public async Task ConcurrentSyncCalls_AreSerializedAndSecondUsesCooldown()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());var first=h.Run();var second=h.Run();await Task.WhenAll(first,second);Assert.Equal(1,h.Transport.Calls);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());}
 [Fact]public async Task AnotherOrganizationCompanyId_IsRejectedBeforeNetwork()
 {await using var h=await Harness.Create();await Assert.ThrowsAsync<NfeException>(()=>h.Service.SynchronizeAsync(new([Guid.NewGuid()],h.Source,FiscalEnvironment.Production)));Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task CancelAfterReceive_DurablyRetainsPageForNextRun()
 {await using var h=await Harness.Create();using var cts=new CancellationTokenSource();h.Transport.Enqueue(Sc());h.Transport.AfterReceive=()=>cts.Cancel();var r=await h.Service.SynchronizeAsync(new([h.Company],h.Source,FiscalEnvironment.Production),ct:cts.Token);Assert.True(r.Cancelled);h.Db.ChangeTracker.Clear();Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);h.Transport.AfterReceive=null;await h.Run();Assert.Equal(1,h.Transport.Calls);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());}

 [Fact]public void NationalKeyResponseWithoutNsu_IsValidAndDoesNotCreateCursor()
 {var x=XDocument.Parse(Encoding.UTF8.GetString(National()));x.Descendants().Where(x=>x.Name.LocalName is "ultNSU" or "maxNSU").Remove();x.Descendants().Single(x=>x.Name.LocalName=="docZip").Attribute("NSU")!.Remove();var p=NfeWireCodec.Decode(NfeSources.National,Encoding.UTF8.GetBytes(x.ToString()),Request(key:Key));Assert.Null(p.NextNsu);Assert.False(p.HasMore);Assert.Equal("0",Assert.Single(p.Items).Nsu);}
 [Fact]public void GzipWithMissingFooter_IsRejected()
 {var x=XDocument.Parse(Encoding.UTF8.GetString(National()));var zip=x.Descendants().Single(x=>x.Name.LocalName=="docZip");zip.Value=Convert.ToBase64String(Convert.FromBase64String(zip.Value)[..^8]);Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.National,Encoding.UTF8.GetBytes(x.ToString()),Request()));}
 [Fact]public void GzipWithModifiedChecksum_IsRejected()
 {var x=XDocument.Parse(Encoding.UTF8.GetString(National()));var zip=x.Descendants().Single(x=>x.Name.LocalName=="docZip");var bytes=Convert.FromBase64String(zip.Value);bytes[^8]^=0x80;zip.Value=Convert.ToBase64String(bytes);Assert.Throws<NfeException>(()=>NfeWireCodec.Decode(NfeSources.National,Encoding.UTF8.GetBytes(x.ToString()),Request()));}
 [Fact]public async Task FiftyScRecords_ContinueThenWaitAndDeduplicateDocument()
 {await using var h=await Harness.Create();string lot="<loteDistNFeSC versao='2.00'>"+string.Concat(Enumerable.Range(1,50).Select(n=>$"<distNFeSC NSU='{n}' chAcesso='{Key}'>{Doc}</distNFeSC>"))+"</loteDistNFeSC>";var x=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"50",count:50)));x.Descendants().Single(x=>x.Name.LocalName=="loteDistComp").Value=Zip(lot);h.Transport.Enqueue(Encoding.UTF8.GetBytes(x.ToString()));h.Transport.Enqueue(Sc(117));var r=await h.Run();Assert.Equal(2,Assert.Single(r.Rows).Pages);Assert.Equal("50",r.Rows[0].Nsu);Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(2,await h.Db.NfeDistributionReceipts.CountAsync());Assert.Equal(1,await h.Db.ArchiveFiles.CountAsync());}
 [Fact]public async Task MixedValidAndInvalidRecords_CommitNothingFromPage()
 {await using var h=await Harness.Create();string lot=$"<loteDistNFeSC versao='2.00'><distNFeSC NSU='1' chAcesso='{Key}'>{Doc}</distNFeSC><distNFeSC NSU='2' chAcesso='{Key}'>{Doc.Replace("123456789.01","invalid")}</distNFeSC></loteDistNFeSC>";var x=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"2",count:2)));x.Descendants().Single(x=>x.Name.LocalName=="loteDistComp").Value=Zip(lot);h.Transport.Enqueue(Encoding.UTF8.GetBytes(x.ToString()));await h.Run();Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.False((await h.Db.NfeDistributionReceipts.SingleAsync()).Applied);}
 [Fact]public async Task NationalSchemaMismatch_HoldsPageAndPreservesOriginal()
 {await using var h=await Harness.Create(NfeSources.National);h.Transport.Enqueue(National(schema:"resNFe_v1.01.xsd"));var r=await h.Run();Assert.Equal("DOCZIP_SCHEMA_MISMATCH",Assert.Single(r.Rows).Code);Assert.Equal("0",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.NotEmpty((await h.Db.NfeDistributionReceipts.SingleAsync()).ResponseBytes);}
 [Fact]public async Task ConflictingRemoteNote_NeverOverwritesOrAdvancesCursor()
 {await using var h=await Harness.Create();h.Transport.Enqueue(Sc());await h.Run();string hash=(await h.Db.FiscalDocuments.SingleAsync()).XmlHash;h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2",document:Doc.Replace("123456789.01","2.00")));var r=await h.Run();Assert.Equal("PAYLOAD_CONFLICT_INBOX_RETAINED",Assert.Single(r.Rows).Code);Assert.Equal(hash,(await h.Db.FiscalDocuments.SingleAsync()).XmlHash);Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Single(await h.Db.ArchiveFiles.ToListAsync());Assert.Equal(2,h.Files.EnumeratePhysicalFiles().Count());}
 [Fact]public async Task EventAndFullNote_ArriveInDifferentPages()
 {await using var h=await Harness.Create();string ev=DfeParserTests.Event().Replace("<tpAmb>2","<tpAmb>1");h.Transport.Enqueue(Sc(document:ev));await h.Run();Assert.Null((await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(Availability.Pending,(await h.Db.KnownDocumentKeys.SingleAsync()).Availability);h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(Sc(nsu:"2"));await h.Run();Assert.Equal((await h.Db.FiscalDocuments.SingleAsync()).Id,(await h.Db.FiscalDocumentEvents.SingleAsync()).FiscalDocumentId);Assert.Equal(DocumentStatus.Cancelled,(await h.Db.FiscalDocuments.SingleAsync()).Status);}
 [Fact]public async Task DifferentCompanies_HaveIndependentCursors()
 {await using var h=await Harness.Create();var other=new Company{OrganizationId=h.Org,Cnpj="12345678000276",LegalName="Outra empresa",State="SC"};h.Db.Companies.Add(other);await h.Db.SaveChangesAsync();h.Transport.Enqueue(Sc());var second=XDocument.Parse(Encoding.UTF8.GetString(Sc(nsu:"2")));second.Descendants().Single(x=>x.Name.LocalName=="CNPJ").Value=other.Cnpj;h.Transport.Enqueue(Encoding.UTF8.GetBytes(second.ToString()));var r=await h.Service.SynchronizeAsync(new([h.Company,other.Id],h.Source,FiscalEnvironment.Production));Assert.Equal(2,r.Rows.Count);Assert.Equal(new[]{"1","2"},(await h.Db.DocumentSyncStates.OrderBy(x=>x.LastNsu).ToListAsync()).Select(x=>x.LastNsu));Assert.Equal(1,await h.Db.FiscalDocuments.CountAsync());Assert.Equal(2,await h.Db.FiscalDocumentCompanies.CountAsync());}
 [Fact]public async Task CertificatePreflightError_DoesNotReserveTwelveHourBlock()
 {await using var h=await Harness.Create();h.Transport.Error="PROVIDER_CERTIFICATE_REQUIRED";await h.Run();Assert.Null((await h.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc);h.Transport.Error=null;h.Transport.Enqueue(Sc());await h.Run();Assert.Equal("1",(await h.Db.DocumentSyncStates.SingleAsync()).LastNsu);}

 [Fact]public void NationalEmptyResponseWithoutOptionalMaxNsu_IsAccepted()
 {var x=XDocument.Parse(Encoding.UTF8.GetString(National(137,"0","0")));x.Descendants().Where(x=>x.Name.LocalName is "ultNSU" or "maxNSU").Remove();var p=NfeWireCodec.Decode(NfeSources.National,Encoding.UTF8.GetBytes(x.ToString()),Request());Assert.Equal("0",p.NextNsu);Assert.Equal(TimeSpan.FromHours(1),p.Delay);}
 [Fact]public async Task NationalKeyHourlyLimit_IsCheckedBeforeHttp()
 {await using var h=await Harness.Create(NfeSources.National);for(int n=0;n<20;n++)h.Db.AuditLogs.Add(new(){OrganizationId=h.Org,Action=AuditAction.NfeRequestStarted,EntityId=h.Company,ResultCode="NFE_KEY_1",CreatedAtUtc=h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10)});await h.Db.SaveChangesAsync();var r=await h.Run(key:Key);Assert.Equal("NATIONAL_KEY_HOURLY_LIMIT",Assert.Single(r.Rows).Code);Assert.Equal(0,h.Transport.Calls);}
 [Fact]public async Task Restart_MarksOnlyNfeJobsInterruptedAndDoesNotDiscardInbox()
 {
  string path=Path.Combine(Path.GetTempPath(),"wgs-nfe-restart-"+Guid.NewGuid().ToString("N"));Guid org=Guid.NewGuid(),jobId=Guid.NewGuid(),otherId=Guid.NewGuid();var options=new FoundationOptions{Application=new(){OrganizationId=org,OrganizationName="Synthetic",DataDirectory=path}};
  try
  {
   await using(var runtime=new FoundationRuntime(options))
   {
    await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();var company=new Company{OrganizationId=org,Cnpj="12345678000195",LegalName="Synthetic"};db.Companies.Add(company);db.ProcessingJobs.AddRange(new ProcessingJob{Id=jobId,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running},new ProcessingJob{Id=otherId,OrganizationId=org,Kind=JobKind.Download,Status=JobStatus.Running});db.AuditLogs.Add(new(){OrganizationId=org,Action=AuditAction.DownloadStarted,EntityId=jobId,ResultCode="NFE_STARTED"});var bytes=Sc();db.NfeDistributionReceipts.Add(new(){OrganizationId=org,CompanyId=company.Id,ProviderCode=NfeSources.SantaCatarina,Environment=FiscalEnvironment.Production,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml,ResponseBytes=bytes,ResponseHash=Convert.ToHexString(SHA256.HashData(bytes))});await db.SaveChangesAsync();
   }
   await using(var runtime=new FoundationRuntime(options))
   {await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();Assert.Equal(JobStatus.Failed,(await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId)).Status);Assert.Equal(JobStatus.Running,(await db.ProcessingJobs.SingleAsync(x=>x.Id==otherId)).Status);Assert.False((await db.NfeDistributionReceipts.SingleAsync()).Applied);Assert.Empty(await db.DocumentSyncStates.ToListAsync());Assert.False(runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests);}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(path,true);}
 }
 private static X509Certificate2 Certificate(){using var rsa=RSA.Create(2048);return new CertificateRequest("CN=SYNTHETIC",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));}
 private sealed class Credentials:INfeCredentialSource{public Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct)=>Task.FromResult(new NfeCredential(request.CompanyTaxId??"12345678000195","42",Certificate()));}
 private sealed class FakeClock:TimeProvider{private DateTimeOffset time=new(2026,10,16,13,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>time;public void Advance(TimeSpan span)=>time+=span;}
 private sealed class Transport:INfeTransport
 {
  private readonly Queue<byte[]> responses=new();public int Calls;public List<NfeWireRequest> Requests{get;}=[];public string? Error;public Action? AfterReceive;
  public void Enqueue(byte[] bytes)=>responses.Enqueue(bytes);
  public Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct){Calls++;Requests.Add(request);if(Error is not null)throw new NfeException(Error);var bytes=responses.Dequeue();AfterReceive?.Invoke();return Task.FromResult(bytes);}
 }
 [Theory][InlineData(false,10,10,1)][InlineData(false,1,30,1)][InlineData(false,11,11,0)][InlineData(true,10,10,1)][InlineData(true,1,30,1)][InlineData(true,11,11,0)]
 async Task Period_is_local_bounded_and_never_resets_nsu(bool national,int first,int last,int expected)
 {
  await using var h=await Harness.Create(national?NfeSources.National:NfeSources.SantaCatarina);
  h.Transport.Enqueue(national?National():Sc());
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
  h.Clock.Advance(TimeSpan.FromHours(12));h.Transport.Enqueue(national?National(137,"1","1"):Sc(117));
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
  public string Directory{get;}=Path.Combine(Path.GetTempPath(),"wgs-nfe-test-"+Guid.NewGuid().ToString("N"));public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public string Source{get;private set;}=NfeSources.SantaCatarina;
  public WgsDbContext Db{get;private set;}=null!;public ArchiveFileStore Files{get;private set;}=null!;public NfeSyncService Service{get;private set;}=null!;public Transport Transport{get;}=new();public FakeClock Clock{get;}=new();private ArchiveGate archiveGate=new();private NfeSyncGate syncGate=new();
  public static async Task<Harness> Create(string source=NfeSources.SantaCatarina)
  {var h=new Harness{Source=source};System.IO.Directory.CreateDirectory(h.Directory);h.Db=DatabaseFactory.Create(Path.Combine(h.Directory,"test.db"),h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Synthetic"});var company=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Empresa sintética",State="SC"};h.Company=company.Id;h.Db.Companies.Add(company);await h.Db.SaveChangesAsync();h.Files=new(new(h.Directory,h.Org));var parser=new DfeParser();var archive=new ArchiveService(h.Db,parser,h.Files,h.archiveGate);var provider=new NfeSoapProvider(source,new Credentials(),h.Transport);h.Service=new(h.Db,[provider],parser,archive,h.archiveGate,h.syncGate,h.Clock,new SyncDiagnosticLog(Path.Combine(h.Directory,"Logs")));return h;}
  public Task<NfeSyncResult> Run(PayloadMode mode=PayloadMode.FullXml,string? key=null)=>Service.SynchronizeAsync(new([Company],Source,FiscalEnvironment.Production,PayloadMode:mode,AccessKey:key));
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();archiveGate.Dispose();syncGate.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(Directory,true);}
 }
}
