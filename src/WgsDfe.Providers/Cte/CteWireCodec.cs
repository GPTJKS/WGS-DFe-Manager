using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
using WgsDfe.Providers.Nfe;
namespace WgsDfe.Providers.Cte;
public static class CteWireCodec
{
 public const string Namespace="http://www.portalfiscal.inf.br/cte",WsNamespace="http://www.portalfiscal.inf.br/cte/wsdl/CTeDistribuicaoDFe";
 public const string Production="https://www1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx",Homologation="https://hom1.cte.fazenda.gov.br/CTeDistribuicaoDFe/CTeDistribuicaoDFe.asmx";
 private static readonly XNamespace Soap="http://www.w3.org/2003/05/soap-envelope",Ns=Namespace,Ws=WsNamespace;
 public static NfeWireRequest Build(DocumentRequest request,string taxId,string stateCode)
 {
  if(request.AccessKey is not null)throw new NfeException("CTE_KEY_LOOKUP_UNSUPPORTED");
  if(request.Model is not (DocumentModel.CTe or DocumentModel.CTeOS)||!Enum.IsDefined(request.Environment)||request.Actor!=ActorFilter.Either||request.PayloadMode!=PayloadMode.FullXml||!Cnpj.TryNormalize(taxId,out var normalized)||taxId!=normalized||!new[]{"11","12","13","14","15","16","17","21","22","23","24","25","26","27","28","29","31","32","33","35","41","42","43","50","51","52","53"}.Contains(stateCode))throw new NfeException("CTE_INVALID_REQUEST");
  string nsu=ValidateNsu(request.SpecificNsu??request.LastNsu,request.SpecificNsu is not null);
  var data=new XElement(Ns+"distDFeInt",new XAttribute("versao","1.00"),new XElement(Ns+"tpAmb",(int)request.Environment),new XElement(Ns+"cUFAutor",stateCode),new XElement(Ns+"CNPJ",taxId),new XElement(Ns+(request.SpecificNsu is null?"distNSU":"consNSU"),new XElement(Ns+(request.SpecificNsu is null?"ultNSU":"NSU"),nsu.PadLeft(15,'0'))));
  var envelope=new XElement(Soap+"Envelope",new XAttribute(XNamespace.Xmlns+"soap12",Soap),new XElement(Soap+"Body",new XElement(Ws+"cteDistDFeInteresse",new XElement(Ws+"cteDadosMsg",data))));
  return new(new Uri(request.Environment==FiscalEnvironment.Production?Production:Homologation),WsNamespace+"/cteDistDFeInteresse",Encoding.UTF8.GetBytes(envelope.ToString(SaveOptions.DisableFormatting)),true);
 }
 public static string ValidateNsu(string text,bool positive=false)
 {try{var value=Nsu.Parse(text);if(text.Length>15||positive&&value.Value==0)throw new ArgumentException();return value.ToString();}catch(ArgumentException){throw new NfeException("CTE_NSU_INVALID");}}
 public static XDocument ReadPayload(ReadOnlyMemory<byte> xml)=>NfeWireCodec.Load(xml,10*1024*1024);
 public static NfePage Decode(ReadOnlyMemory<byte> bytes,DocumentRequest request)
 {
  try{return DecodeCore(bytes,request);}
  catch(NfeException){throw;}
  catch(Exception e)when(e is XmlException or ArgumentException or FormatException or InvalidOperationException or OverflowException or InvalidDataException){throw new NfeException("CTE_INVALID_RESPONSE");}
 }
 private static NfePage DecodeCore(ReadOnlyMemory<byte> bytes,DocumentRequest request)
 {
  var root=NfeWireCodec.Load(bytes,NfeWireCodec.MaxResponseBytes).Root!;
  if(root.Name!=Soap+"Envelope")throw new NfeException("CTE_SOAP_ENVELOPE_INVALID");
  var body=One(root,Soap+"Body");if(body.Elements(Soap+"Fault").Any())throw new NfeException("SOAP_FAULT");if(body.Elements().Count()!=1)throw new NfeException("CTE_SOAP_AMBIGUOUS");
  var response=One(body,Ws+"cteDistDFeInteresseResponse");var result=One(response,Ws+"cteDistDFeInteresseResult");var ret=One(result,Ns+"retDistDFeInt");
  if(response.Elements().Count()!=1||result.Elements().Count()!=1||(string?)ret.Attribute("versao")!="1.00")throw new NfeException("CTE_RESPONSE_LAYOUT_INVALID");
  int status=int.Parse(Value(ret,Ns+"cStat"),NumberStyles.None,CultureInfo.InvariantCulture);
  if(status is <0 or >9999||Value(ret,Ns+"tpAmb")!=((int)request.Environment).ToString(CultureInfo.InvariantCulture))throw new NfeException("CTE_RESPONSE_ENVIRONMENT_OR_STATUS_INVALID");
  var items=new List<NfeItem>();var lots=ret.Elements(Ns+"loteDistDFeInt").ToArray();if(lots.Length>1)throw new NfeException("CTE_BATCH_AMBIGUOUS");
  if(status is not (137 or 138))
  {if(lots.Length!=0)throw new NfeException("CTE_REJECTED_RESPONSE_WITH_DOCUMENTS");return new(status,null,false,TimeSpan.FromHours(1),items);}
  string? next=Optional(ret,Ns+"ultNSU"),max=Optional(ret,Ns+"maxNSU");ulong old=Nsu.Parse(ValidateNsu(request.LastNsu)).Value,cursor=next is null?old:Nsu.Parse(ValidateNsu(next)).Value;ulong? maximum=max is null?null:Nsu.Parse(ValidateNsu(max)).Value;
  if(request.SpecificNsu is null&&(cursor<old||status==138&&next is null))throw new NfeException("CTE_CURSOR_INVALID");
  long expanded=0;ulong previous=old;
  foreach(var entry in lots.SelectMany(x=>x.Elements()))
  {
   if(entry.Name!=Ns+"docZip"||entry.HasElements||items.Count>=50)throw new NfeException("CTE_BATCH_LAYOUT_INVALID");
   string n=ValidateNsu((string?)entry.Attribute("NSU")??throw new NfeException("CTE_NSU_MISSING"),true);ulong value=Nsu.Parse(n).Value;
   if(request.SpecificNsu is null&&(value<=previous||value>cursor))throw new NfeException("CTE_NSU_ORDER_INVALID");previous=value;
   if(request.SpecificNsu is not null&&n!=ValidateNsu(request.SpecificNsu,true))throw new NfeException("CTE_POINT_RETURN_MISMATCH");
   string schema=(string?)entry.Attribute("schema")??throw new NfeException("CTE_SCHEMA_MISSING");
   if(schema.Length is <1 or >128||schema.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '_' or '.')))throw new NfeException("CTE_SCHEMA_INVALID");
   byte[] xml=NfeWireCodec.Unzip(entry.Value,10*1024*1024);expanded+=xml.Length;if(expanded>NfeWireCodec.MaxExpandedBytes)throw new NfeException("EXPANSION_LIMIT");
   // Validate even retained/unknown payloads as safe XML before accepting a checkpoint.
   NfeWireCodec.Load(xml,10*1024*1024);items.Add(new(null,n,xml,schema));
  }
  if(status==137&&items.Count!=0||status==138&&items.Count==0||request.SpecificNsu is not null&&items.Count>1)throw new NfeException("CTE_BATCH_COUNT_INVALID");
  if(request.SpecificNsu is null&&status==138&&(cursor<=old||previous!=cursor))throw new NfeException("CTE_CURSOR_MISMATCH");
  if(request.SpecificNsu is null&&maximum.HasValue&&cursor>maximum.Value)throw new NfeException("CTE_MAX_NSU_INVALID");
  bool more=request.SpecificNsu is null&&status==138&&maximum.HasValue&&cursor<maximum.Value;
  return new(status,request.SpecificNsu is null?next??request.LastNsu:null,more,more?TimeSpan.Zero:TimeSpan.FromHours(1),items.AsReadOnly());
 }
 private static XElement One(XElement parent,XName name){var list=parent.Elements(name).ToArray();if(list.Length!=1)throw new NfeException("CTE_RESPONSE_FIELD_CARDINALITY");return list[0];}
 private static string Value(XElement parent,XName name){var e=One(parent,name);if(e.HasElements)throw new NfeException("CTE_RESPONSE_FIELD_INVALID");return e.Value;}
 private static string? Optional(XElement parent,XName name){var list=parent.Elements(name).ToArray();if(list.Length>1||list.Any(x=>x.HasElements))throw new NfeException("CTE_RESPONSE_FIELD_CARDINALITY");return list.SingleOrDefault()?.Value;}
}
