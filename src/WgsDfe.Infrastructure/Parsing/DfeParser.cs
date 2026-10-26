using System.Globalization;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Parsing;
public sealed partial class DfeParser:IDfeParser
{
 public const int MaximumBytes=10*1024*1024;
 public const string NfeNamespace="http://www.portalfiscal.inf.br/nfe",CteNamespace="http://www.portalfiscal.inf.br/cte",NfcomNamespace="http://www.portalfiscal.inf.br/nfcom";
 private sealed record Profile(string Namespace,string Raw,string Processed,string Info,string Prefix,string Protocol,string KeyTag,string NumberTag,string CodeTag,string TotalGroup,string TotalTag,string[] Versions,DocumentModel[] Models);
 // NFComProc is the wrapper spelling published by SC distribution; nfcomProc is the national XSD spelling.
 private static readonly Profile NfcomProfile=new(NfcomNamespace,"NFCom","nfcomProc","infNFCom","NFCom","protNFCom","chNFCom","nNF","cNF","total","vNF",["1.00"],[DocumentModel.NFCom]);
 private static readonly Profile[] Profiles=
 [new(NfeNamespace,"NFe","nfeProc","infNFe","NFe","protNFe","chNFe","nNF","cNF","ICMSTot","vNF",["4.00","3.10"],[DocumentModel.NFe,DocumentModel.NFCe]),
  new(CteNamespace,"CTe","cteProc","infCte","CTe","protCTe","chCTe","nCT","cCT","vPrest","vTPrest",["4.00","3.00"],[DocumentModel.CTe]),
  new(CteNamespace,"CTeOS","cteOSProc","infCte","CTe","protCTe","chCTe","nCT","cCT","vPrest","vTPrest",["4.00","3.00"],[DocumentModel.CTeOS]),
  NfcomProfile,NfcomProfile with{Processed="NFComProc"}];
 public async Task<DfeParseResult> ParseFileAsync(string path,CancellationToken ct=default)
 {
  try
  {
   await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length is <1 or >MaximumBytes)throw Error(DfeParseError.SizeLimit);
   var bytes=new byte[(int)file.Length];await file.ReadExactlyAsync(bytes,ct);return Parse(bytes,ct);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException){throw Error(DfeParseError.FileUnavailable);}
 }
 public DfeParseResult Parse(ReadOnlyMemory<byte> xml,CancellationToken ct=default)
 {
  ct.ThrowIfCancellationRequested();if(xml.Length is <1 or >MaximumBytes)throw Error(DfeParseError.SizeLimit);
  var bytes=xml.ToArray();
  try
  {
   var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=MaximumBytes,MaxCharactersFromEntities=1024,IgnoreComments=true,CloseInput=false};
   using(var stream=new MemoryStream(bytes,false))using(var reader=XmlReader.Create(stream,settings))
   {
    int nodes=0;while(reader.Read())
    {ct.ThrowIfCancellationRequested();if(reader.Depth>64||++nodes>200000||reader.AttributeCount>64)throw Error(DfeParseError.ComplexityLimit);if(reader.NodeType==XmlNodeType.ProcessingInstruction)throw Error(DfeParseError.UnsafeOrMalformedXml);}
   }
   using var second=new MemoryStream(bytes,false);using var safeReader=XmlReader.Create(second,settings);var doc=XDocument.Load(safeReader,LoadOptions.None);var root=doc.Root??throw Error(DfeParseError.InvalidStructure);
   string hash=Convert.ToHexString(SHA256.HashData(bytes));
   var profile=Profiles.FirstOrDefault(p=>p.Namespace==root.Name.NamespaceName&&(p.Raw==root.Name.LocalName||p.Processed==root.Name.LocalName));
   if(profile is not null)return Full(root,profile,hash);
   if(root.Name==XName.Get("resNFe",NfeNamespace))return Summary(root,hash);
   if(root.Name==XName.Get("resEvento",NfeNamespace))return EventSummary(root,hash);
   if((root.Name.NamespaceName is NfeNamespace or CteNamespace or NfcomNamespace)&&root.Name.LocalName is "evento" or "procEventoNFe" or "eventoCTe" or "procEventoCTe" or "eventoNFCom" or "procEventoNFCom")return Event(root,hash);
   throw Error(DfeParseError.UnsupportedRoot);
  }
  catch(XmlException){throw Error(DfeParseError.UnsafeOrMalformedXml);}
 }
 private static DfeParseResult Full(XElement root,Profile p,string hash)
 {
  bool processed=root.Name.LocalName==p.Processed;var raw=processed?One(root,p.Raw)!:root;var info=One(raw,p.Info)!;
  if(root.Descendants(info.Name).Count()!=1)throw Error(DfeParseError.InvalidStructure);
  string version=Attribute(info,"versao");if(!p.Versions.Contains(version))throw Error(DfeParseError.UnsupportedVersion);
  if(processed&&Attribute(root,"versao")!=version)throw Error(DfeParseError.InconsistentData);
  string id=Attribute(info,"Id");if(!id.StartsWith(p.Prefix,StringComparison.Ordinal))throw Error(DfeParseError.InvalidKey);var key=Key(id[p.Prefix.Length..]);
  if(!p.Models.Contains(key.Model))throw Error(DfeParseError.InconsistentData);
  var ide=One(info,"ide")!;Match(Text(ide,"mod"),((int)key.Model).ToString(CultureInfo.InvariantCulture));Match(Text(ide,"cUF"),key.StateCode);
  Match(Padded(Text(ide,"serie"),3),key.Series);Match(Padded(Text(ide,p.NumberTag),9),key.Number);Match(Text(ide,"tpEmis"),key.EmissionType.ToString(CultureInfo.InvariantCulture));
  Match(Text(ide,p.CodeTag),key.NumericCode);Match(Text(ide,"cDV"),key.CheckDigit.ToString(CultureInfo.InvariantCulture));
  if(key.Model==DocumentModel.NFCom)Match(Text(ide,"nSiteAutoriz"),key.AuthorizerSite!.Value.ToString(CultureInfo.InvariantCulture));
  var environment=Environment(Text(ide,"tpAmb"));var issue=Date(Text(ide,"dhEmi"),false);
  if(issue.Year!=key.Year||issue.Month!=key.Month)throw Error(DfeParseError.InconsistentData);
  var emit=One(info,"emit")!;string issuer=TaxId(emit,true)!;Match(issuer,key.IssuerTaxId);
  var dest=One(info,"dest",false);string? recipient=dest is null?null:TaxId(dest,false);string? recipientName=dest is null?null:Text(dest,"xNome",false);
  var totalParent=p.Namespace==NfeNamespace?One(One(info,"total")!,p.TotalGroup)!:One(info,p.TotalGroup)!;decimal total=Money(Text(totalParent,p.TotalTag));
  int? response=null;string? protocol=null;DateTimeOffset? receipt=null;var status=DocumentStatus.Unknown;
  if(processed)
  {
   var prot=One(root,p.Protocol)!;var inf=One(prot,"infProt")!;Match(Text(inf,p.KeyTag),key.Value);Match(Text(inf,"tpAmb"),((int)environment).ToString(CultureInfo.InvariantCulture));
   response=Code(Text(inf,"cStat"));protocol=Protocol(Text(inf,"nProt",false),key.Model);receipt=OptionalDate(Text(inf,"dhRecbto",false));status=DocumentState(response.Value);
   if(status==DocumentStatus.Authorized&&(protocol is null||receipt is null))throw Error(DfeParseError.InvalidStructure);
  }
  return new(DfePayloadKind.FullDocument,key,version,environment,issuer,Text(emit,"xNome"),recipient,recipientName,issue,total,status,protocol,response,receipt,null,null,null,hash);
 }
 private static DfeParseResult Summary(XElement root,string hash)
 {
  string version=Version(root,"1.00","1.01");var key=Key(Text(root,"chNFe"));if(key.Model is not(DocumentModel.NFe or DocumentModel.NFCe))throw Error(DfeParseError.InconsistentData);
  string issuer=TaxId(root,true)!;Match(issuer,key.IssuerTaxId);var issue=Date(Text(root,"dhEmi"),false);if(issue.Year!=key.Year||issue.Month!=key.Month)throw Error(DfeParseError.InconsistentData);
  // resNFe não contém tpAmb. O ambiente deve vir do envelope da distribuição, não da chave.
  return new(DfePayloadKind.DocumentSummary,key,version,null,issuer,Text(root,"xNome"),null,null,issue,Money(Text(root,"vNF")),Text(root,"cSitNFe") switch{"1"=>DocumentStatus.Authorized,"2"=>DocumentStatus.Denied,"3"=>DocumentStatus.Cancelled,_=>throw Error(DfeParseError.InvalidValue)},Protocol(Text(root,"nProt"),key.Model),null,Date(Text(root,"dhRecbto"),false),null,null,null,hash);
 }
 private static DfeParseResult EventSummary(XElement root,string hash)
 {
  string version=Version(root,"1.00","1.01");var key=Key(Text(root,"chNFe"));if(key.Model is not(DocumentModel.NFe or DocumentModel.NFCe))throw Error(DfeParseError.InconsistentData);
  string type=EventCode(Text(root,"tpEvento"));int sequence=Sequence(Text(root,"nSeqEvento"));
  return new(DfePayloadKind.EventSummary,key,version,null,null,null,null,null,null,null,DocumentStatus.Unknown,Protocol(Text(root,"nProt"),key.Model),null,Date(Text(root,"dhRecbto"),false),type,sequence,Date(Text(root,"dhEvento"),false),hash);
 }
 private static DfeParseResult Event(XElement root,string hash)
 {
  string ns=root.Name.NamespaceName;bool nfe=ns==NfeNamespace,cte=ns==CteNamespace;
  string rawName=nfe?"evento":cte?"eventoCTe":"eventoNFCom";string processedName=nfe?"procEventoNFe":cte?"procEventoCTe":"procEventoNFCom";
  if(root.Name.LocalName!=rawName&&root.Name.LocalName!=processedName)throw Error(DfeParseError.UnsupportedRoot);
  bool processed=root.Name.LocalName==processedName;string version=Version(root,nfe?["1.00"]:cte?["3.00","4.00"]:["1.00"]);
  var raw=processed?One(root,rawName)!:root;if(processed)Version(raw,version);var info=One(raw,"infEvento")!;
  if(root.Descendants(info.Name).Count()!=(processed?2:1))throw Error(DfeParseError.InvalidStructure);
  string keyTag=nfe?"chNFe":cte?"chCTe":"chNFCom";var key=Key(Text(info,keyTag));if(nfe?key.Model is not(DocumentModel.NFe or DocumentModel.NFCe):cte?key.Model is not(DocumentModel.CTe or DocumentModel.CTeOS):key.Model!=DocumentModel.NFCom)throw Error(DfeParseError.InconsistentData);
  string type=EventCode(Text(info,"tpEvento"));int sequence=Sequence(Text(info,"nSeqEvento"));var environment=Environment(Text(info,"tpAmb"));var date=Date(Text(info,"dhEvento"),false);
  string eventId=Attribute(info,"Id");string idBase="ID"+type+key.Value;
  if(eventId!=idBase+sequence.ToString("D2",CultureInfo.InvariantCulture)&&(nfe||eventId!=idBase+sequence.ToString("D3",CultureInfo.InvariantCulture)))throw Error(DfeParseError.InconsistentData);
  int? response=null;string? protocol=null;DateTimeOffset? receipt=null;
  if(processed)
  {
   var returned=One(root,nfe?"retEvento":cte?"retEventoCTe":"retEventoNFCom")!;var result=One(returned,"infEvento")!;
   Match(Text(result,keyTag),key.Value);Match(Text(result,"tpEvento"),type);Match(Padded(Text(result,"nSeqEvento"),3),sequence.ToString("D3",CultureInfo.InvariantCulture));Match(Text(result,"tpAmb"),((int)environment).ToString(CultureInfo.InvariantCulture));
   response=Code(Text(result,"cStat"));protocol=Protocol(Text(result,"nProt",false),key.Model);receipt=OptionalDate(Text(result,"dhRegEvento",false));
   if(response is 134 or 135 or 136 or 155&&(protocol is null||receipt is null))throw Error(DfeParseError.InvalidStructure);
  }
  // Evento nunca converte o XML principal em nota autorizada/cancelada; acervo fará a conciliação.
  return new(DfePayloadKind.Event,key,version,environment,null,null,null,null,null,null,DocumentStatus.Unknown,protocol,response,receipt,type,sequence,date,hash);
 }
 private static XElement? One(XElement parent,string name,bool required=true)
 {
  var matches=parent.Elements().Where(x=>x.Name.LocalName==name).ToArray();if(matches.Length>1)throw Error(DfeParseError.InvalidStructure);
  if(matches.Length==0){if(required)throw Error(DfeParseError.InvalidStructure);return null;}
  if(matches[0].Name.Namespace!=parent.Name.Namespace)throw Error(DfeParseError.InvalidStructure);return matches[0];
 }
 private static string? Text(XElement parent,string name,bool required)
 {
  var node=One(parent,name,required);if(node is null)return null;if(node.HasElements||node.Value.Length is <1 or >1024)throw Error(DfeParseError.InvalidValue);return node.Value;
 }
 private static string Text(XElement parent,string name)=>Text(parent,name,true)!;
 private static string Attribute(XElement node,string name)=>node.Attribute(name)?.Value is {Length:>0 and <=128} value?value:throw Error(DfeParseError.InvalidStructure);
 private static string Version(XElement root,params string[] versions){string v=Attribute(root,"versao");if(!versions.Contains(v))throw Error(DfeParseError.UnsupportedVersion);return v;}
 private static DfeAccessKey Key(string text)
 {try{var key=DfeAccessKey.Parse(text);if(text!=key.Value)throw Error(DfeParseError.InvalidKey);return key;}catch(AccessKeyException){throw Error(DfeParseError.InvalidKey);}}
 private static FiscalEnvironment Environment(string value)=>value switch{"1"=>FiscalEnvironment.Production,"2"=>FiscalEnvironment.Homologation,_=>throw Error(DfeParseError.InvalidValue)};
 private static void Match(string? actual,string expected){if(actual!=expected)throw Error(DfeParseError.InconsistentData);}
 private static string Padded(string value,int length){if(value.Length>length||value.Length==0||value.Any(c=>!char.IsAsciiDigit(c)))throw Error(DfeParseError.InvalidValue);return value.PadLeft(length,'0');}
 private static string? TaxId(XElement parent,bool required)
 {
  string? cnpj=Text(parent,"CNPJ",false),cpf=Text(parent,"CPF",false);if(cnpj is not null&&cpf is not null)throw Error(DfeParseError.InvalidStructure);
  if(cnpj is not null){if(cnpj.Length!=14||!Cnpj.TryNormalize(cnpj,out var n)||n!=cnpj)throw Error(DfeParseError.InvalidValue);return cnpj;}
  if(cpf is not null){if(!Cpf.IsValid(cpf))throw Error(DfeParseError.InvalidValue);return cpf;}
  if(required)throw Error(DfeParseError.InvalidStructure);return null;
 }
 private static DateTimeOffset Date(string value,bool allowDateOnly)
 {
  if(allowDateOnly&&DateOnly.TryParseExact(value,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day))return new(day.ToDateTime(TimeOnly.MinValue,DateTimeKind.Utc));
  if(!Timestamp().IsMatch(value)||!DateTimeOffset.TryParse(value,CultureInfo.InvariantCulture,DateTimeStyles.None,out var date))throw Error(DfeParseError.InvalidValue);return date;
 }
 private static DateTimeOffset? OptionalDate(string? value)=>value is null?null:Date(value,false);
 private static decimal Money(string value){if(!Amount().IsMatch(value)||!decimal.TryParse(value,NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out var amount))throw Error(DfeParseError.InvalidValue);return amount;}
 private static int Code(string value){if(value.Length is <3 or >4||value.Any(c=>!char.IsAsciiDigit(c)))throw Error(DfeParseError.InvalidValue);return int.Parse(value,CultureInfo.InvariantCulture);}
 private static string? Protocol(string? value,DocumentModel model){if(value is null)return null;bool validLength=model==DocumentModel.NFCom?value.Length==16:model is DocumentModel.NFe or DocumentModel.NFCe?value.Length is 15 or 17:value.Length==15;if(!validLength||value.Any(c=>!char.IsAsciiDigit(c)))throw Error(DfeParseError.InvalidValue);return value;}
 private static string EventCode(string value){if(value.Length!=6||value.Any(c=>!char.IsAsciiDigit(c)))throw Error(DfeParseError.InvalidValue);return value;}
 private static int Sequence(string value){string v=Padded(value,3);int n=int.Parse(v,CultureInfo.InvariantCulture);return n>0?n:throw Error(DfeParseError.InvalidValue);}
 private static DocumentStatus DocumentState(int code)=>code switch{100 or 150=>DocumentStatus.Authorized,101 or 151=>DocumentStatus.Cancelled,110 or 301 or 302 or 303=>DocumentStatus.Denied,_=>DocumentStatus.Other};
 private static DfeParseException Error(DfeParseError code)=>new(code);
 [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}(?:\.[0-9]{1,7})?(?:Z|[+-][0-9]{2}:[0-9]{2})$",RegexOptions.CultureInvariant)]private static partial Regex Timestamp();
 [GeneratedRegex(@"^[0-9]{1,15}\.[0-9]{2}$",RegexOptions.CultureInvariant)]private static partial Regex Amount();
}
