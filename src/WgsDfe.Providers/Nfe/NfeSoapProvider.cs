using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
namespace WgsDfe.Providers.Nfe;
public sealed class NfeSoapProvider(string code,INfeCredentialSource credentials,INfeTransport transport):INfeDistributionProvider
{
 public ProviderCapabilities Capabilities{get;}=new(code,new HashSet<DocumentModel>{NfeSources.Model(code)},NfeSources.IsSantaCatarina(code)?CertificateRequirement.AccountantWithLink:CertificateRequirement.Participant,true,true,false,false,true);
 public bool CanHandle(DocumentRequest r)=>r.Model==NfeSources.Model(code)&&Enum.IsDefined(r.Environment)&&(r.Actor is ActorFilter.Issuer or ActorFilter.Recipient or ActorFilter.Either or ActorFilter.Both)&&Enum.IsDefined(r.PayloadMode)&&
  (NfeSources.IsSantaCatarina(code)?r.Environment==FiscalEnvironment.Production:r.Actor==ActorFilter.Either&&r.PayloadMode==PayloadMode.FullXml);
 public async Task<byte[]> ReceiveAsync(DocumentRequest request,CancellationToken ct)
 {
  if(!CanHandle(request))throw new NfeException("UNSUPPORTED_FILTER_OR_ENVIRONMENT");
  using var credential=await credentials.OpenAsync(request,code,ct);
  return await transport.SendAsync(NfeWireCodec.Build(code,request,credential.TaxId,credential.StateCode),credential.Certificate,ct);
 }
 public NfePage Decode(ReadOnlyMemory<byte> response,DocumentRequest request)=>NfeWireCodec.Decode(code,response,request);
 public async Task<ProviderResult> GetDocumentsAsync(DocumentRequest request,CancellationToken cancellationToken)
 {
  var page=Decode(await ReceiveAsync(request,cancellationToken),request);
  return new(page.Items.Count>0?Availability.Available:Availability.NotFoundInSource,"NFE_"+page.Status,
   page.Items.Where(x=>x.Xml is not null).Select(x=>new RetrievedPayload(x.Key,x.Nsu,x.Xml!,x.Schema)).ToArray(),page.NextNsu,DateTime.UtcNow+page.Delay,page.Status);
 }
}
