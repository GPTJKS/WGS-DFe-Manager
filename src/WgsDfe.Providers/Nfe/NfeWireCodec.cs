using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
namespace WgsDfe.Providers.Nfe;
public static class NfeWireCodec
{
 public const string NfceNamespace="http://www.satnfce.sef.sc.gov.br/ws/distribuicao-v1",NfceEndpoint="https://dfe.sat.sef.sc.gov.br/nfce/ws/distribuicao/DistribuicaoNfceDownload.asmx";
 public const string NfcomNamespace="http://www.satnfcom.sef.sc.gov.br/ws/distribuicao-v1",NfcomEndpoint="https://dfe.sat.sef.sc.gov.br/nfcom/ws/distribuicao/DistribuicaoNfcomDownload.asmx",NfcomFiscalNamespace="http://www.portalfiscal.inf.br/nfcom";
 public const int MaxResponseBytes=32*1024*1024,MaxExpandedBytes=64*1024*1024;
 public const string ScNamespace="http://www.satnfe.sef.sc.gov.br/ws/distribuicao-v2",NfeNamespace="http://www.portalfiscal.inf.br/nfe",NationalNamespace="http://www.portalfiscal.inf.br/nfe/wsdl/NFeDistribuicaoDFe";
 // The SC contracts share paging rules, but have distinct wire names and fiscal payloads.
 private sealed record ScProfile(string Namespace,string Operation,string RequestRoot,string ReturnRoot,string BatchRoot,string EntryRoot,string Version,string Endpoint,bool DirectPayload,string FiscalNamespace,string[] PayloadRoots)
 {
  public bool AcceptsVersion(string? version)=>version==Version||(Version=="2.02"&&version=="2.00");
 }
 private static ScProfile? Profile(string source)=>source switch
 {
  NfeSources.SantaCatarina=>new(ScNamespace,"NfeDownloadContab","distNFeSC","retDistNFeSC","loteDistNFeSC","distNFeSC","2.02","https://satnfe.sef.sc.gov.br/ws/distribuicao/nfedownloadV2.asmx",false,NfeNamespace,["nfeProc","procEventoNFe"]),
  NfeSources.SantaCatarinaNfce=>new(NfceNamespace,"nfceDownloadContab","distNFCeSC","retDistNFCeSC","loteDistNFCeSC","distNFCeSC","1.00",NfceEndpoint,true,NfeNamespace,["nfeProc","procEventoNFe"]),
  NfeSources.SantaCatarinaNfcom=>new(NfcomNamespace,"nfcomDownloadContab","distNFComSC","retDistNFComSC","loteDistNFComSC","distNFComSC","1.00",NfcomEndpoint,true,NfcomFiscalNamespace,["nfcomProc","NFComProc","procEventoNFCom"]),
  _=>null
 };
 private static readonly XNamespace Soap="http://schemas.xmlsoap.org/soap/envelope/";
 public static NfeWireRequest Build(string source,DocumentRequest r,string taxId,string stateCode)
 {
  var profile=Profile(source);bool sc=profile is not null;if(!NfeSources.IsKnown(source)||!Cnpj.TryNormalize(taxId,out _)||!Enum.IsDefined(r.Environment)||r.Actor is not (ActorFilter.Issuer or ActorFilter.Recipient or ActorFilter.Either or ActorFilter.Both)||!Enum.IsDefined(r.PayloadMode)||r.Model!=NfeSources.Model(source))throw new NfeException("INVALID_REQUEST");
  if(sc&&(r.Environment!=FiscalEnvironment.Production||stateCode!="42"))throw new NfeException("SC_PRODUCTION_ONLY");
  if(!sc&&(r.Actor!=ActorFilter.Either||r.PayloadMode!=PayloadMode.FullXml))throw new NfeException("NATIONAL_FILTER_NOT_SUPPORTED");
  string? key=r.AccessKey is null?null:DfeAccessKey.Parse(r.AccessKey).Value;if(key is not null&&DfeAccessKey.Parse(key).Model!=NfeSources.Model(source))throw new NfeException("KEY_MODEL_MISMATCH");
  if(!new[]{"11","12","13","14","15","16","17","21","22","23","24","25","26","27","28","29","31","32","33","35","41","42","43","50","51","52","53"}.Contains(stateCode))throw new NfeException("INVALID_STATE_CODE");
  XNamespace ws=profile?.Namespace??NationalNamespace,ns=profile?.Namespace??NfeNamespace;string operation=profile?.Operation??"nfeDistDFeInteresse";
  var data=new XElement(ns+(profile?.RequestRoot??"distDFeInt"),new XAttribute("versao",profile?.Version??"1.01"),new XElement(ns+"tpAmb",(int)r.Environment));
  if(sc)data.Add(new XElement(ns+"verAplic","WGS_0.9.0"),new XElement(ns+"cUF",stateCode));else data.Add(new XElement(ns+"cUFAutor",stateCode));
  data.Add(new XElement(ns+"CNPJ",taxId));
  if(key is not null)data.Add(new XElement(ns+(sc?"solDFe":"consChNFe"),new XElement(ns+(sc?"chAcesso":"chNFe"),key)));
  else data.Add(sc?new XElement(ns+"solRel",new XElement(ns+"indXML",(int)r.PayloadMode),new XElement(ns+"indAtor",(int)r.Actor),new XElement(ns+"ultNuNSU",Nsu.Parse(r.LastNsu).ToString())):
   new XElement(ns+"distNSU",new XElement(ns+"ultNSU",Nsu.Parse(r.LastNsu).ToString().PadLeft(15,'0'))));
  if(!sc&&Nsu.Parse(r.LastNsu).Value>999999999999999UL)throw new NfeException("NATIONAL_NSU_LIMIT");
  var envelope=new XElement(Soap+"Envelope",new XAttribute(XNamespace.Xmlns+"soap",Soap),new XElement(Soap+"Body",new XElement(ws+operation,profile?.DirectPayload==true?data:new XElement(ws+(sc?"pXml":"nfeDadosMsg"),data))));
  return new(new Uri(profile?.Endpoint??(r.Environment==FiscalEnvironment.Production?"https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx":"https://hom.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx")),ws.NamespaceName+"/"+operation,Encoding.UTF8.GetBytes(envelope.ToString(SaveOptions.DisableFormatting)));
 }
 public static NfePage Decode(string source,ReadOnlyMemory<byte> bytes,DocumentRequest request)
 {
  try{return DecodeCore(source,bytes,request);}
  catch(NfeException){throw;}
  catch(Exception e)when(e is XmlException or InvalidOperationException or ArgumentException or FormatException or InvalidDataException or OverflowException){throw new NfeException("INVALID_DISTRIBUTION_RESPONSE");}
 }
 private static NfePage DecodeCore(string source,ReadOnlyMemory<byte> bytes,DocumentRequest request)
 {
  var profile=Profile(source);bool sc=profile is not null;if(!NfeSources.IsKnown(source))throw new NfeException("INVALID_PROVIDER");
  XNamespace ws=profile?.Namespace??NationalNamespace,ns=profile?.Namespace??NfeNamespace;string op=profile?.Operation??"nfeDistDFeInteresse";
  var doc=Load(bytes,MaxResponseBytes);var root=doc.Root!;
  if(root.Name!=Soap+"Envelope")throw new NfeException("SOAP_ENVELOPE_INVALID");
  var body=One(root,Soap+"Body");if(body.Elements(Soap+"Fault").Any())throw new NfeException("SOAP_FAULT");
  if(body.Elements().Count()!=1)throw new NfeException("SOAP_BODY_AMBIGUOUS");
  var result=One(One(body,ws+(op+"Response")),ws+(op+"Result"));
  var ret=One(result,ns+(profile?.ReturnRoot??"retDistDFeInt"));if(result.Elements().Count()!=1)throw new NfeException("SOAP_RESULT_AMBIGUOUS");
  if(sc?!profile!.AcceptsVersion((string?)ret.Attribute("versao")):(string?)ret.Attribute("versao")!="1.01")throw new NfeException("RESPONSE_VERSION_UNSUPPORTED");
  int status=Number(Value(ret,ns+"cStat"));if(status is <0 or >9999)throw new NfeException("RESPONSE_STATUS_INVALID");
  string? env=Optional(ret,ns+"tpAmb");if(env is not null&&env!=((int)request.Environment).ToString(CultureInfo.InvariantCulture))throw new NfeException("RESPONSE_ENVIRONMENT_MISMATCH");
  bool success=status==(sc?118:138),empty=status==(sc?117:137);
  if(!success&&!empty)return new(status,null,false,Delay(sc,status,0,false),Array.Empty<NfeItem>());
  if(env is null)throw new NfeException("RESPONSE_ENVIRONMENT_MISSING");
  var items=new List<NfeItem>();string? next=Optional(ret,ns+(sc?"ultNuNSURet":"ultNSU"));
  ulong old=Nsu.Parse(request.LastNsu).Value;ulong cursor=next is null?old:Nsu.Parse(next).Value;
  if(empty&&next is null)next=request.LastNsu;
  if(request.AccessKey is null&&(next is null||cursor<old))throw new NfeException("CURSOR_INVALID");
  if(sc)
  {
   string? uf=Optional(ret,ns+"cUF"),cpf=Optional(ret,ns+"CPF"),echoNsu=Optional(ret,ns+"ultNuNSU");
   if(uf is not null&&uf!="42"||cpf is not null||echoNsu is not null&&Nsu.Parse(echoNsu).Value!=old)throw new NfeException("RESPONSE_FILTER_MISMATCH");
   string? tax=Optional(ret,ns+"CNPJ"); // Echoes are optional according to the official return XSD.
   if(tax is not null&&tax.Length>0&&tax!=request.CompanyTaxId)throw new NfeException("RESPONSE_COMPANY_MISMATCH");
   foreach(var echo in new[]{("indAtor",((int)request.Actor).ToString(CultureInfo.InvariantCulture)),("indXML",((int)request.PayloadMode).ToString(CultureInfo.InvariantCulture)),("chAcesso",request.AccessKey)})
   {string? value=Optional(ret,ns+echo.Item1);if(value is not null&&value!=echo.Item2)throw new NfeException("RESPONSE_FILTER_MISMATCH");}
   var countText=Optional(ret,ns+"qtDfeRet");int count=countText is null?0:Number(countText);
   string? batch=Optional(ret,ns+"loteDistComp");
   if(batch is not null)
   {
    var lot=Load(Unzip(batch,MaxExpandedBytes),MaxExpandedBytes).Root!;
    if(lot.Name!=XName.Get(profile!.BatchRoot)||!profile!.AcceptsVersion((string?)lot.Attribute("versao")))throw new NfeException("SC_BATCH_LAYOUT_INVALID");
    if(lot.Elements().Any(x=>x.Name!=XName.Get(profile!.EntryRoot)))throw new NfeException("SC_BATCH_LAYOUT_INVALID");
    foreach(var entry in lot.Elements())
    {
     if(items.Count>=50)throw new NfeException("BATCH_COUNT_LIMIT");string n=Attribute(entry,"NSU");string? key=(string?)entry.Attribute("chAcesso");
     var xml=entry.Elements().ToArray();if(xml.Length>1)throw new NfeException("SC_ENTRY_AMBIGUOUS");
     if(request.PayloadMode==PayloadMode.KeysOnly&&request.AccessKey is null){if(xml.Length!=0||key is null)throw new NfeException("SC_KEYS_LAYOUT_INVALID");}
     else if(xml.Length!=1||xml[0].Name.NamespaceName!=profile!.FiscalNamespace||!profile.PayloadRoots.Contains(xml[0].Name.LocalName))throw new NfeException("SC_ENTRY_LAYOUT_UNSUPPORTED");
     if(key is not null&&DfeAccessKey.Parse(key).Model!=NfeSources.Model(source))throw new NfeException("KEY_MODEL_MISMATCH");
     items.Add(new(key,n,xml.Length==0?null:Encoding.UTF8.GetBytes(xml[0].ToString(SaveOptions.DisableFormatting)),xml.Length==0?"chAcesso":xml[0].Name.LocalName));
    }
   }
   if(items.Count!=count||success&&count==0||empty&&count!=0)throw new NfeException("BATCH_COUNT_MISMATCH");
  }
  else
  {
   var lots=ret.Elements(ns+"loteDistDFeInt").ToArray();if(lots.Length>1)throw new NfeException("BATCH_AMBIGUOUS");long expanded=0;
   foreach(var entry in lots.SelectMany(x=>x.Elements()))
   {
    if(entry.Name!=ns+"docZip"||items.Count>=50)throw new NfeException("NATIONAL_BATCH_LAYOUT_INVALID");
    var xml=Unzip(entry.Value,10*1024*1024);expanded+=xml.Length;if(expanded>MaxExpandedBytes)throw new NfeException("EXPANSION_LIMIT");
    string schema=Attribute(entry,"schema");if(schema.Length>128||schema.Contains('/')||schema.Contains('\\'))throw new NfeException("SCHEMA_INVALID");
    items.Add(new(null,(string?)entry.Attribute("NSU")??(request.AccessKey is not null?"0":throw new NfeException("RESPONSE_NSU_MISSING")),xml,schema));
   }
   if(success&&items.Count==0||empty&&items.Count!=0)throw new NfeException("BATCH_COUNT_MISMATCH");
  }
  ulong previous=old;
  foreach(var item in items){ulong n=Nsu.Parse(item.Nsu).Value;if(request.AccessKey is null&&(n<=previous||n>cursor))throw new NfeException("NSU_ORDER_INVALID");previous=n;}
  if(request.AccessKey is null&&success&&cursor<=old)throw new NfeException("CURSOR_NO_PROGRESS");
  // SC states that this is the last NSU returned, not an independently advanced watermark.
  if(sc&&request.AccessKey is null&&success&&previous!=cursor)throw new NfeException("SC_CURSOR_MISMATCH");
  string? maxText=sc?null:Optional(ret,ns+"maxNSU");
  ulong? maxNsu=maxText is null?null:Nsu.Parse(maxText).Value;
  bool more=request.AccessKey is null&&(sc?success&&items.Count==50:success&&maxNsu.HasValue&&cursor<maxNsu.Value);
  if(!sc&&request.AccessKey is null&&maxNsu.HasValue&&cursor>maxNsu.Value)throw new NfeException("MAX_NSU_INVALID");
  return new(status,request.AccessKey is null?next:null,more,Delay(sc,status,items.Count,more),items.AsReadOnly());
 }
 
 public static TimeSpan Delay(bool sc,int status,int count,bool more)=>sc?status switch{117=>TimeSpan.FromHours(12),118=>more?TimeSpan.Zero:TimeSpan.FromHours(12),657=>TimeSpan.FromHours(12),_=>TimeSpan.FromHours(1)}:
  status switch{137 or 656=>TimeSpan.FromHours(1),138=>more?TimeSpan.Zero:TimeSpan.FromHours(1),_=>TimeSpan.FromHours(1)};
 internal static XDocument Load(ReadOnlyMemory<byte> bytes,int max)
 {
  if(bytes.Length==0||bytes.Length>max)throw new NfeException("RESPONSE_SIZE_LIMIT");
  var settings=new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=max,IgnoreComments=true};
  using(var stream=new MemoryStream(bytes.ToArray()))using(var reader=XmlReader.Create(stream,settings))
  {int nodes=0;while(reader.Read()){if(reader.Depth>80||++nodes>500000||reader.AttributeCount>64||reader.NodeType==XmlNodeType.ProcessingInstruction)throw new NfeException("RESPONSE_COMPLEXITY_LIMIT");}}
  using var input=new MemoryStream(bytes.ToArray());using var xml=XmlReader.Create(input,settings);return XDocument.Load(xml,LoadOptions.PreserveWhitespace);
 }
 internal static byte[] Unzip(string base64,int max)
 {
  if(base64.Length>MaxResponseBytes)throw new NfeException("COMPRESSED_SIZE_LIMIT");byte[] compressed=Convert.FromBase64String(base64);
  if(compressed.Length<18||compressed[0]!=0x1f||compressed[1]!=0x8b)throw new NfeException("GZIP_REQUIRED");
  using var input=new MemoryStream(compressed);using var gzip=new GZipStream(input,CompressionMode.Decompress);using var output=new MemoryStream();var buffer=new byte[32768];int count;
  while((count=gzip.Read(buffer))!=0){if(output.Length+count>max)throw new NfeException("EXPANSION_LIMIT");output.Write(buffer,0,count);}
  byte[] result=output.ToArray();
  uint crc=uint.MaxValue;foreach(byte b in result){crc^=b;for(int bit=0;bit<8;bit++)crc=(crc&1)!=0?0xEDB88320u^(crc>>1):crc>>1;}
  if(BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length-8))!=~crc||BinaryPrimitives.ReadUInt32LittleEndian(compressed.AsSpan(compressed.Length-4))!=(uint)result.Length)throw new NfeException("GZIP_INTEGRITY_FAILED");
  return result;
 }
 private static XElement One(XElement p,XName n){var children=p.Elements(n).ToArray();if(children.Length!=1)throw new NfeException("RESPONSE_FIELD_CARDINALITY");return children[0];}
 private static string Value(XElement p,XName n){var e=One(p,n);if(e.HasElements)throw new NfeException("RESPONSE_FIELD_INVALID");return e.Value;}
 private static string? Optional(XElement p,XName n){var all=p.Elements(n).ToArray();if(all.Length>1||all.Any(x=>x.HasElements))throw new NfeException("RESPONSE_FIELD_CARDINALITY");return all.SingleOrDefault()?.Value;}
 private static string Attribute(XElement e,string n)=>(string?)e.Attribute(n)??throw new NfeException("RESPONSE_ATTRIBUTE_MISSING");
 private static int Number(string value)=>int.Parse(value,NumberStyles.None,CultureInfo.InvariantCulture);
}
