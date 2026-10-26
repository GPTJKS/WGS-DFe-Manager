using WgsDfe.Application.Cte;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
namespace WgsDfe.Providers.Cte;
public sealed class CteSoapProvider(INfeCredentialSource credentials,INfeTransport transport):ICteDistributionProvider
{
 public ProviderCapabilities Capabilities{get;}=new(CteSource.Code,new HashSet<DocumentModel>{DocumentModel.CTe,DocumentModel.CTeOS},CertificateRequirement.Participant,true,false,false,false,true);
 public bool CanHandle(DocumentRequest r)=>r.Model is DocumentModel.CTe or DocumentModel.CTeOS&&Enum.IsDefined(r.Environment)&&r.Actor==ActorFilter.Either&&r.PayloadMode==PayloadMode.FullXml&&r.AccessKey is null;
 public async Task<byte[]> ReceiveAsync(DocumentRequest request,CancellationToken ct)
 {
  if(!CanHandle(request))throw new NfeException("CTE_INVALID_REQUEST");
  using var credential=await credentials.OpenAsync(request,CteSource.Code,ct);
  return await transport.SendAsync(CteWireCodec.Build(request,credential.TaxId,credential.StateCode),credential.Certificate,ct);
 }
 public NfePage Decode(ReadOnlyMemory<byte> bytes,DocumentRequest request)=>CteWireCodec.Decode(bytes,request);
 public async Task<ProviderResult> GetDocumentsAsync(DocumentRequest request,CancellationToken cancellationToken)
 {var page=Decode(await ReceiveAsync(request,cancellationToken),request);return new(page.Items.Count>0?Availability.Available:Availability.NotFoundInSource,"CTE_"+page.Status,page.Items.Select(x=>new RetrievedPayload(null,x.Nsu,x.Xml!,x.Schema)).ToArray(),page.NextNsu,DateTime.UtcNow+page.Delay,page.Status);}
}
