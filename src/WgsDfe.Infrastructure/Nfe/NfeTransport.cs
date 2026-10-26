using System.Net;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using WgsDfe.Application.Nfe;
using WgsDfe.Providers.Nfe;
namespace WgsDfe.Infrastructure.Nfe;
public sealed class NfeNetworkGate:IDisposable
{
 internal SemaphoreSlim Semaphore{get;}=new(1,1);
 public void Dispose()=>Semaphore.Dispose();
}
public sealed class NfeTransport(ProviderOptions options,NfeNetworkGate gate):INfeTransport
{
 public async Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct)
 {
  if(!options.EnableExternalRequests)throw new NfeException("EXTERNAL_REQUESTS_DISABLED");
  // Only audited official endpoints. Redirects must never forward a client certificate elsewhere.
  if(request.Endpoint.AbsoluteUri is not ("https://satnfe.sef.sc.gov.br/ws/distribuicao/nfedownloadV2.asmx" or "https://www1.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx" or "https://hom.nfe.fazenda.gov.br/NFeDistribuicaoDFe/NFeDistribuicaoDFe.asmx" or NfeWireCodec.NfcomEndpoint or NfeWireCodec.NfceEndpoint or WgsDfe.Providers.Cte.CteWireCodec.Production or WgsDfe.Providers.Cte.CteWireCodec.Homologation))throw new NfeException("ENDPOINT_NOT_ALLOWED");
  await gate.Semaphore.WaitAsync(ct);
  try
  {
   using var handler=new HttpClientHandler{AllowAutoRedirect=false,ClientCertificateOptions=ClientCertificateOption.Manual,CheckCertificateRevocationList=true,SslProtocols=SslProtocols.Tls12,AutomaticDecompression=DecompressionMethods.None};
   handler.ClientCertificates.Add(certificate);
   using var http=new HttpClient(handler){Timeout=TimeSpan.FromSeconds(60)};
   using var message=new HttpRequestMessage(HttpMethod.Post,request.Endpoint){Content=new ByteArrayContent(request.Body)};
   if(request.Soap12){message.Content.Headers.ContentType=new("application/soap+xml"){CharSet="utf-8"};message.Content.Headers.ContentType.Parameters.Add(new("action","\""+request.SoapAction+"\""));}
   else{message.Content.Headers.ContentType=new("text/xml"){CharSet="utf-8"};message.Headers.Add("SOAPAction","\""+request.SoapAction+"\"");}
   using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct);
   if(response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)throw new NfeException("MTLS_ACCESS_DENIED");
   if(!response.IsSuccessStatusCode)throw new NfeException("HTTP_SERVICE_UNAVAILABLE");
   if(response.Content.Headers.ContentLength>NfeWireCodec.MaxResponseBytes)throw new NfeException("RESPONSE_SIZE_LIMIT");
   await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[32768];int count;
   while((count=await input.ReadAsync(buffer,ct))!=0){if(output.Length+count>NfeWireCodec.MaxResponseBytes)throw new NfeException("RESPONSE_SIZE_LIMIT");await output.WriteAsync(buffer.AsMemory(0,count),ct);}
   return output.ToArray();
  }
  catch(HttpRequestException ex){throw new NfeException(NetworkFailure.Code(ex));}
  catch(OperationCanceledException)when(!ct.IsCancellationRequested){throw new NfeException("NETWORK_TIMEOUT");}
  finally
  {
   // Globally serial; below SC's 8 requests/second limit, across all local companies/providers.
   await Task.Delay(TimeSpan.FromMilliseconds(250),CancellationToken.None);gate.Semaphore.Release();
  }
 }
}
