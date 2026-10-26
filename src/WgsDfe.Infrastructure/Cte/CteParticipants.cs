using System.Xml;
using System.Xml.Linq;
using WgsDfe.Application.Nfe;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Cte;
public sealed record CteParticipant(string TaxId,string Role);
public static class CteParticipants
{
 private static readonly XNamespace Ns="http://www.portalfiscal.inf.br/cte";
 // Call after IDfeParser (which performs safe XML preflight). This method also prohibits DTD/resolution.
 public static IReadOnlyList<CteParticipant> Read(byte[] xml)
 {
  if(xml.Length is <1 or >10*1024*1024)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");
  using var input=new MemoryStream(xml);using var reader=XmlReader.Create(input,new(){DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=10*1024*1024});var root=XDocument.Load(reader).Root!;
  var raw=Optional(root,"CTe")??Optional(root,"CTeOS")??(root.Name==Ns+"CTe"||root.Name==Ns+"CTeOS"?root:null);
  if(raw is null)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");var info=One(raw,"infCte");var roles=new List<CteParticipant>();
  foreach(var (tag,role) in new[]{("emit","Emitente"),("dest","Destinatário"),("rem","Remetente"),("exped","Expedidor"),("receb","Recebedor"),("toma","Tomador")})
  {var group=Optional(info,tag);if(group is not null)Add(group,role);}
  foreach(var auth in info.Elements(Ns+"autXML"))Add(auth,"Autorizado no XML");
  var ide=One(info,"ide");var toma3=Optional(ide,"toma3");var toma4=Optional(ide,"toma4");
  if(toma3 is not null&&toma4 is not null)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");
  if(toma4 is not null){if(One(toma4,"toma").Value!="4")throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");Add(toma4,"Tomador");}
  if(toma3 is not null)
  {
   string tag=One(toma3,"toma").Value switch{"0"=>"rem","1"=>"exped","2"=>"receb","3"=>"dest",_=>throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED")};
   Add(One(info,tag),"Tomador");
  }
  return roles.Distinct().ToArray();
  void Add(XElement parent,string role)
  {
   var cnpj=Optional(parent,"CNPJ");var cpf=Optional(parent,"CPF");if(cnpj is not null&&cpf is not null)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");
   if(cnpj is not null){if(!Cnpj.TryNormalize(cnpj.Value,out var tax)||tax!=cnpj.Value)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");roles.Add(new(tax,role));}
   else if(cpf is not null){if(!Cpf.IsValid(cpf.Value))throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");roles.Add(new(cpf.Value,role));}
  }
 }
 private static XElement One(XElement parent,string name)=>Optional(parent,name)??throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");
 private static XElement? Optional(XElement parent,string name){var list=parent.Elements(Ns+name).ToArray();if(list.Length>1)throw new NfeException("CTE_PAYLOAD_INVALID_RETAINED");return list.SingleOrDefault();}
}
