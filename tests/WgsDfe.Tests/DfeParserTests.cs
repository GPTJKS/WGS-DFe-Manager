using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Parsing;
using Xunit;
namespace WgsDfe.Tests;
public sealed class DfeParserTests
{
 private readonly DfeParser parser=new();
 // Fixtures de metadados sintéticas: não são XMLs autorizados nem validação XSD.
 internal static string Key(int model=55,string tax="12345678000195",string series="001",string number="000000123",string uf="42",string ym="2609",string emission="1",string code="12345678")
 {
  string body=uf+ym+tax+model.ToString("D2",CultureInfo.InvariantCulture)+series+number+emission+code;
  Assert.Equal(43,body.Length);int sum=0;for(int i=0;i<43;i++)sum+=(body[i]-48)*(2+(42-i)%8);int rem=sum%11;int digit=rem is 0 or 1?0:11-rem;return body+digit.ToString(CultureInfo.InvariantCulture);
 }
 internal static string Document(int model=55,bool processed=true,string tax="12345678000195",string series="001",string? protocol=null)
 {
  bool nfe=model is 55 or 65,cte=model is 57 or 67;string key=Key(model,tax,series);string ns=nfe?DfeParser.NfeNamespace:cte?DfeParser.CteNamespace:DfeParser.NfcomNamespace;
  string raw=nfe?"NFe":model==57?"CTe":model==67?"CTeOS":"NFCom",info=nfe?"infNFe":cte?"infCte":"infNFCom",prefix=nfe?"NFe":cte?"CTe":"NFCom",ver=model==62?"1.00":"4.00";
  string issuer=series is "910" or "920"?$"<CPF>{tax[3..]}</CPF>":$"<CNPJ>{tax}</CNPJ>";
  string xml=$"<{raw} xmlns='{ns}'><{info} Id='{prefix}{key}' versao='{ver}'><ide><cUF>42</cUF><mod>{model}</mod><serie>{int.Parse(series,CultureInfo.InvariantCulture)}</serie><{(cte?"nCT":"nNF")}>123</{(cte?"nCT":"nNF")}><tpEmis>1</tpEmis><{(cte?"cCT":"cNF")}>{(model==62?"2345678":"12345678")}</{(cte?"cCT":"cNF")}><cDV>{key[^1]}</cDV><tpAmb>2</tpAmb><dhEmi>2026-09-10T11:12:13-03:00</dhEmi>{(model==62?"<nSiteAutoriz>1</nSiteAutoriz>":"")}</ide><emit>{issuer}<xNome>Empresa sintética</xNome></emit><dest><CNPJ>12345678000276</CNPJ><xNome>Cliente sintético</xNome></dest>{(nfe?"<total><ICMSTot><vNF>123456789.01</vNF></ICMSTot></total>":cte?"<vPrest><vTPrest>123456789.01</vTPrest></vPrest>":"<total><vNF>123456789.01</vNF></total>")}</{info}></{raw}>";
  if(!processed)return xml;string proc=nfe?"nfeProc":model==57?"cteProc":model==67?"cteOSProc":"nfcomProc",prot=nfe?"protNFe":cte?"protCTe":"protNFCom",keyTag=nfe?"chNFe":cte?"chCTe":"chNFCom";
  protocol??=model==62?"1422600000000001":"142260000000001";
  return $"<{proc} xmlns='{ns}' versao='{ver}'>{xml}<{prot} versao='{ver}'><infProt><tpAmb>2</tpAmb><{keyTag}>{key}</{keyTag}><cStat>100</cStat><nProt>{protocol}</nProt><dhRecbto>2026-09-10T11:12:14-03:00</dhRecbto></infProt></{prot}></{proc}>";
 }
 private DfeParseResult Parse(string xml)=>parser.Parse(Encoding.UTF8.GetBytes(xml));
 [Theory][InlineData(55)][InlineData(57)][InlineData(62)][InlineData(65)][InlineData(67)]public void FiveModels_KeyFields(int model)
 {
  var value=Key(model);var key=DfeAccessKey.Parse(value);Assert.Equal((DocumentModel)model,key.Model);Assert.Equal("SC",key.State);Assert.Equal(2026,key.Year);Assert.Equal(9,key.Month);Assert.Equal("12345678000195",key.IssuerTaxId);Assert.Equal("001",key.Series);Assert.Equal("000000123",key.Number);Assert.Equal(model==62?"2345678":"12345678",key.NumericCode);Assert.Equal(model==62?(int?)1:null,key.AuthorizerSite);
 }
 [Theory][InlineData(55)][InlineData(57)][InlineData(62)][InlineData(65)][InlineData(67)]public void AlphaCnpj_IsSupportedInAllModels(int model)
 {var key=DfeAccessKey.Parse(Key(model,"12ABC34501DE35"));Assert.Equal("12ABC34501DE35",key.IssuerTaxId);var result=Parse(Document(model,tax:"12ABC34501DE35"));Assert.Equal(key,result.Key);}
 [Fact]public void SyntheticFixedNumericNfe_Vector(){var k=DfeAccessKey.Parse("35240312345678000195550010000001231123456789");Assert.Equal(55,(int)k.Model);}
 [Fact]public void ManualNfcomIllustration_WithMonth18_IsRejected(){Assert.Equal(AccessKeyError.Month,Assert.Throws<AccessKeyException>(()=>DfeAccessKey.Parse("43081808467115000100620010757245731000000010")).Code);}
 [Fact]public void PasteNormalization_OnlyAsciiGrouping(){string key=Key();Assert.Equal(key,DfeAccessKey.Parse(" \t"+string.Join(" ",Enumerable.Range(0,11).Select(i=>key.Substring(i*4,4)))+"\r\n").Value);Assert.Equal(Key(tax:"12ABC34501DE35"),DfeAccessKey.Parse(Key(tax:"12ABC34501DE35").ToLowerInvariant()).Value);}
 [Theory][InlineData(55,"910")][InlineData(65,"920")]public void ReservedCpfSeries(int model,string series){var key=Key(model,"00052998224725",series);Assert.True(DfeAccessKey.Parse(key).IsCpfIssuer);Assert.Equal("52998224725",Parse(Document(model,tax:"00052998224725",series:series)).IssuerTaxId);}
 [Theory][InlineData(55,"001")][InlineData(65,"001")][InlineData(57,"910")][InlineData(62,"910")]public void CpfIsNotGuessedOutsideReservedSeries(int model,string series)=>Assert.Equal(AccessKeyError.Issuer,Assert.Throws<AccessKeyException>(()=>DfeAccessKey.Parse(Key(model,"00052998224725",series))).Code);
 [Theory][InlineData(null)][InlineData("")][InlineData(" ")][InlineData("123")][InlineData("00000000000000000000000000000000000000000000")]public void InvalidKeyInput(string? text)=>Assert.Throws<AccessKeyException>(()=>DfeAccessKey.Parse(text));
 [Theory][InlineData("state")][InlineData("month")][InlineData("model")][InlineData("issuer")][InlineData("number")][InlineData("emission")][InlineData("dv")][InlineData("letter")][InlineData("unicode")][InlineData("punctuation")]
 public void InvalidKeyComponents_AreRejected(string component)
 {
  string value=component switch{"state"=>Key(uf:"99"),"month"=>Key(ym:"2613"),"model"=>Key(66),"issuer"=>Key(tax:"12345678000196"),"number"=>Key(number:"000000000"),"emission"=>Key(emission:"0"),"dv"=>Key()[..43]+((Key()[^1]-'0'+1)%10),"letter"=>"A"+Key()[1..],"unicode"=>"４"+Key()[1..],_=>Key()[..43]+"-"};Assert.Throws<AccessKeyException>(()=>DfeAccessKey.Parse(value));
 }
 [Theory][InlineData(55,true)][InlineData(57,true)][InlineData(62,true)][InlineData(65,true)][InlineData(67,true)][InlineData(55,false)][InlineData(57,false)][InlineData(62,false)][InlineData(65,false)][InlineData(67,false)]public void FullXml_AllModels(int model,bool processed)
 {
  var xml=Document(model,processed);var r=Parse(xml);Assert.Equal((DocumentModel)model,r.Key.Model);Assert.Equal(DfePayloadKind.FullDocument,r.Kind);Assert.Equal(FiscalEnvironment.Homologation,r.Environment);Assert.Equal(123456789.01m,r.Total);Assert.Equal(TimeSpan.FromHours(-3),r.IssueDate!.Value.Offset);Assert.Equal(processed?DocumentStatus.Authorized:DocumentStatus.Unknown,r.Status);Assert.Equal(processed?(model==62?16:15):(int?)null,r.ProtocolNumber?.Length);Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(xml))),r.Sha256);Assert.Contains("Não verifica",r.Notice);
 }
 [Fact]public void Nfe17DigitProtocol_IsPreserved(){var r=Parse(Document(protocol:"14226000000000001"));Assert.Equal(17,r.ProtocolNumber!.Length);}
 [Fact]public void NamespacedPrefix_IsAccepted(){var doc=XDocument.Parse(Document());XNamespace ns=DfeParser.NfeNamespace;doc.Root!.Add(new XAttribute(XNamespace.Xmlns+"f",ns.NamespaceName));Assert.Equal(Key(),Parse(doc.ToString()).Key.Value);}
 [Theory][InlineData("3.10",55)][InlineData("3.00",57)][InlineData("3.00",67)]public void EarlierSupportedVersions(string version,int model)=>Assert.Equal(version,Parse(Document(model).Replace("4.00",version)).LayoutVersion);
 [Theory][InlineData("model")][InlineData("issuer")][InlineData("series")][InlineData("number")][InlineData("uf")][InlineData("month")][InlineData("code")][InlineData("dv")][InlineData("env")][InlineData("protocol-key")][InlineData("site")]
 public void XmlMustAgreeWithKeyAndProtocol(string field)
 {
  string xml=Document(field=="site"?62:55);xml=field switch{"model"=>xml.Replace("<mod>55</mod>","<mod>65</mod>"),"issuer"=>xml.Replace("<emit><CNPJ>12345678000195","<emit><CNPJ>12345678000276"),"series"=>xml.Replace("<serie>1</serie>","<serie>2</serie>"),"number"=>xml.Replace("<nNF>123</nNF>","<nNF>124</nNF>"),"uf"=>xml.Replace("<cUF>42</cUF>","<cUF>43</cUF>"),"month"=>xml.Replace("2026-09-10T11:12:13","2026-08-10T11:12:13"),"code"=>xml.Replace("<cNF>12345678</cNF>","<cNF>12345679</cNF>"),"dv"=>xml.Replace($"<cDV>{Key()[^1]}</cDV>",$"<cDV>{(Key()[^1]-'0'+1)%10}</cDV>"),"env"=>xml.Replace("<infProt><tpAmb>2","<infProt><tpAmb>1"),"protocol-key"=>xml.Replace("<chNFe>"+Key(),"<chNFe>"+Key(number:"000000124")),_=>xml.Replace("<nSiteAutoriz>1","<nSiteAutoriz>2")};Assert.Equal(DfeParseError.InconsistentData,Assert.Throws<DfeParseException>(()=>Parse(xml)).Code);
 }
 [Theory][InlineData("123,45")][InlineData("-1.00")][InlineData("1e2")][InlineData(" 1.00")][InlineData("12.345")][InlineData("NaN")][InlineData("1")]
 public void InvalidAmounts_AreRejected(string value)=>Assert.Equal(DfeParseError.InvalidValue,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("123456789.01",value))).Code);
 [Theory][InlineData("2026-09-10T11:12:13")][InlineData("2026-02-30T11:12:13-03:00")][InlineData("10/09/2026")][InlineData("2026-09-10")]
 public void DateMustHaveValidExplicitOffset(string value)=>Assert.Equal(DfeParseError.InvalidValue,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("2026-09-10T11:12:13-03:00",value))).Code);
 [Theory][InlineData(100,DocumentStatus.Authorized)][InlineData(150,DocumentStatus.Authorized)][InlineData(110,DocumentStatus.Denied)][InlineData(101,DocumentStatus.Cancelled)][InlineData(999,DocumentStatus.Other)]
 public void ProtocolStatusIsReportedAsDeclared(int code,DocumentStatus status)=>Assert.Equal(status,Parse(Document().Replace("<cStat>100",$"<cStat>{code}")).Status);
 [Fact]public void MissingAuthorizationProtocol_IsRejected()=>Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("<nProt>142260000000001</nProt>","")));
 [Fact]public void UnsupportedVersion_IsExplicit()=>Assert.Equal(DfeParseError.UnsupportedVersion,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("4.00","9.99"))).Code);
 [Theory][InlineData("<html><body>Erro SEFAZ</body></html>")][InlineData("<NFe/>")][InlineData("<NFe xmlns='https://evil.invalid'/>")][InlineData("<CTeSimp xmlns='http://www.portalfiscal.inf.br/cte'/>")][InlineData("<inutNFe xmlns='http://www.portalfiscal.inf.br/nfe'/>")]
 public void UnsupportedRoots_AreNotDocuments(string xml)=>Assert.Equal(DfeParseError.UnsupportedRoot,Assert.Throws<DfeParseException>(()=>Parse(xml)).Code);
 [Fact]public void DuplicateCriticalFields_AreRejected()=>Assert.Equal(DfeParseError.InvalidStructure,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("<mod>55</mod>","<mod>55</mod><mod>55</mod>"))).Code);
 [Fact]public void NamespaceCollision_IsRejected()=>Assert.Equal(DfeParseError.InvalidStructure,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("<mod>55</mod>","<mod xmlns='https://evil.invalid'>55</mod>"))).Code);
 [Fact]public void NestedFakeDocument_IsRejected(){var doc=XDocument.Parse(Document());var info=doc.Descendants(XName.Get("infNFe",DfeParser.NfeNamespace)).Single();info.Add(new XElement("extra",new XElement(info)));Assert.Equal(DfeParseError.InvalidStructure,Assert.Throws<DfeParseException>(()=>Parse(doc.ToString())).Code);}
 [Fact]public void Summary_HasNoInferredEnvironmentOrFullXml()
 {string xml=$"<resNFe xmlns='{DfeParser.NfeNamespace}' versao='1.01'><chNFe>{Key()}</chNFe><CNPJ>12345678000195</CNPJ><xNome>Empresa</xNome><dhEmi>2026-09-10T11:12:13-03:00</dhEmi><vNF>0.00</vNF><cSitNFe>3</cSitNFe><nProt>142260000000001</nProt><dhRecbto>2026-09-10T11:12:14-03:00</dhRecbto></resNFe>";var r=Parse(xml);Assert.Equal(DfePayloadKind.DocumentSummary,r.Kind);Assert.Null(r.Environment);Assert.Null(r.RecipientTaxId);Assert.Equal(DocumentStatus.Cancelled,r.Status);Assert.Equal(0m,r.Total);}
 internal static string Event(int model=55,bool processed=true,int code=135,int width=2)
 {
  bool nfe=model is 55 or 65,cte=model is 57 or 67;string ns=nfe?DfeParser.NfeNamespace:cte?DfeParser.CteNamespace:DfeParser.NfcomNamespace,keyTag=nfe?"chNFe":cte?"chCTe":"chNFCom",raw=nfe?"evento":cte?"eventoCTe":"eventoNFCom",returned=nfe?"retEvento":cte?"retEventoCTe":"retEventoNFCom",proc=nfe?"procEventoNFe":cte?"procEventoCTe":"procEventoNFCom",version=cte?"4.00":"1.00",key=Key(model);
  string xml=$"<{raw} xmlns='{ns}' versao='{version}'><infEvento Id='ID110111{key}{1.ToString("D"+width,CultureInfo.InvariantCulture)}'><tpAmb>2</tpAmb><{keyTag}>{key}</{keyTag}><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><dhEvento>2026-09-11T12:13:14-03:00</dhEvento></infEvento></{raw}>";
  return processed?$"<{proc} xmlns='{ns}' versao='{version}'>{xml}<{returned} versao='{version}'><infEvento><tpAmb>2</tpAmb><{keyTag}>{key}</{keyTag}><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><cStat>{code}</cStat><nProt>{(model==62?"1422600000000001":"142260000000001")}</nProt><dhRegEvento>2026-09-11T12:13:15-03:00</dhRegEvento></infEvento></{returned}></{proc}>":xml;
 }
 [Theory][InlineData(55,true,2)][InlineData(57,true,2)][InlineData(62,true,3)][InlineData(65,true,2)][InlineData(67,true,3)][InlineData(55,false,2)][InlineData(62,false,3)]public void EventsRemainSeparate(int model,bool processed,int width){var r=Parse(Event(model,processed,width:width));Assert.Equal(DfePayloadKind.Event,r.Kind);Assert.Equal("110111",r.EventType);Assert.Equal(1,r.EventSequence);Assert.Equal(DocumentStatus.Unknown,r.Status);Assert.Null(r.Total);Assert.Null(r.IssueDate);Assert.Equal(processed?135:(int?)null,r.ResponseCode);}
 [Fact]public void UnlinkedEvent136_IsNotCancellation(){var r=Parse(Event(code:136));Assert.Equal(DocumentStatus.Unknown,r.Status);Assert.Equal(136,r.ResponseCode);}
 [Fact]public void EventReturnMustMatchRequest()=>Assert.Throws<DfeParseException>(()=>Parse(Event().Replace("<retEvento versao='1.00'><infEvento><tpAmb>2","<retEvento versao='1.00'><infEvento><tpAmb>1")));
 [Fact]public void EventIdMustMatchKey()=>Assert.Throws<DfeParseException>(()=>Parse(Event().Replace("ID110111","ID110112")));
 [Fact]public void EventSummary_DoesNotAuthorizeDocument(){string xml=$"<resEvento xmlns='{DfeParser.NfeNamespace}' versao='1.01'><chNFe>{Key()}</chNFe><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><dhEvento>2026-09-11T12:13:14-03:00</dhEvento><dhRecbto>2026-09-11T12:13:15-03:00</dhRecbto><nProt>142260000000001</nProt></resEvento>";var r=Parse(xml);Assert.Equal(DfePayloadKind.EventSummary,r.Kind);Assert.Null(r.Environment);Assert.Equal(DocumentStatus.Unknown,r.Status);}
 [Theory][InlineData("<!DOCTYPE NFe [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]><NFe>&xxe;</NFe>")][InlineData("<!DOCTYPE NFe SYSTEM 'https://example.invalid/evil.dtd'><NFe/>")][InlineData("<?xml-stylesheet href='https://example.invalid/evil.xsl'?><NFe/>")][InlineData("<NFe>")][InlineData("<a/><b/>")]
 public void UnsafeXml_IsRejectedWithoutEcho(string xml){var e=Assert.Throws<DfeParseException>(()=>Parse(xml));Assert.Equal(DfeParseError.UnsafeOrMalformedXml,e.Code);Assert.DoesNotContain("etc/passwd",e.ToString());Assert.DoesNotContain("evil",e.ToString());}
 [Fact]public void XmlSizeAndDepth_AreBounded(){Assert.Equal(DfeParseError.SizeLimit,Assert.Throws<DfeParseException>(()=>parser.Parse(new byte[DfeParser.MaximumBytes+1])).Code);string xml=string.Concat(Enumerable.Repeat("<a>",70))+string.Concat(Enumerable.Repeat("</a>",70));Assert.Equal(DfeParseError.ComplexityLimit,Assert.Throws<DfeParseException>(()=>Parse(xml)).Code);}
 [Fact]public void NodeAndAttributeCounts_AreBounded()
 {
  string manyNodes="<r>"+string.Concat(Enumerable.Repeat("<x/>",200001))+"</r>";
  Assert.Equal(DfeParseError.ComplexityLimit,Assert.Throws<DfeParseException>(()=>Parse(manyNodes)).Code);
  string manyAttributes="<r "+string.Join(" ",Enumerable.Range(0,65).Select(i=>$"a{i}='x'"))+"/>";
  Assert.Equal(DfeParseError.ComplexityLimit,Assert.Throws<DfeParseException>(()=>Parse(manyAttributes)).Code);
 }
 [Fact]public void RecipientMayBeAbsent_AndZeroTotalIsKept()
 {var d=XDocument.Parse(Document());XNamespace ns=DfeParser.NfeNamespace;d.Descendants(ns+"dest").Remove();d.Descendants(ns+"vNF").Single().Value="0.00";var r=Parse(d.ToString());Assert.Null(r.RecipientTaxId);Assert.Equal(0m,r.Total);}
 [Fact]public void TwoIssuerIdentities_AreRejected()=>Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("<emit>","<emit><CPF>52998224725</CPF>")));
 [Fact]public void XmlKeyCannotHavePasteFormatting()=>Assert.Equal(DfeParseError.InvalidKey,Assert.Throws<DfeParseException>(()=>Parse(Document().Replace("Id='NFe","Id='NFe "))).Code);
 [Fact]public void InvalidProtocolLength_IsRejected()=>Assert.Equal(DfeParseError.InvalidValue,Assert.Throws<DfeParseException>(()=>Parse(Document(62,protocol:"142260000000001"))).Code);
 [Fact]public void MalformedBytes_HaveSafeXmlError()=>Assert.Equal(DfeParseError.UnsafeOrMalformedXml,Assert.Throws<DfeParseException>(()=>parser.Parse(new byte[]{0xff,0xfe,0xff})).Code);
 [Fact]public void CancelledParse_Stops(){using var c=new CancellationTokenSource();c.Cancel();Assert.Throws<OperationCanceledException>(()=>parser.Parse(Encoding.UTF8.GetBytes(Document()),c.Token));}
 [Fact]public void Utf16OriginalBytes_AreHashed(){var bytes=Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("<?xml version='1.0' encoding='utf-16'?>"+Document())).ToArray();var r=parser.Parse(bytes);Assert.Equal(Key(),r.Key.Value);Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)),r.Sha256);}
 [Fact]public async Task FileParsing_DoesNotModifyOriginal()
 {string path=Path.Combine(Path.GetTempPath(),"wgs-parser-"+Guid.NewGuid().ToString("N")+".xml");var bytes=Encoding.UTF8.GetBytes(Document());try{await File.WriteAllBytesAsync(path,bytes);var r=await parser.ParseFileAsync(path);Assert.Equal(Key(),r.Key.Value);Assert.Equal(bytes,await File.ReadAllBytesAsync(path));}finally{File.Delete(path);}}
 [Fact]public async Task MissingFile_HasSafeError()=>Assert.Equal(DfeParseError.FileUnavailable,(await Assert.ThrowsAsync<DfeParseException>(()=>parser.ParseFileAsync(Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".xml")))).Code);
}
