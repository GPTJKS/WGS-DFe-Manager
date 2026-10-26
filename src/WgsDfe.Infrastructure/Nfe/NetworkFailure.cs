using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Authentication;
namespace WgsDfe.Infrastructure.Nfe;
// Only enums and numeric native codes leave this boundary. Never expose exception
// messages: they can contain request details, credentials or proxy addresses.
public static class NetworkFailure
{
 public static string Code(HttpRequestException error)
 {
  var chain=new List<Exception>();for(Exception? e=error;e is not null&&chain.Count<16;e=e.InnerException)chain.Add(e);
  if(error.HttpRequestError==HttpRequestError.SecureConnectionError||chain.Any(e=>e is AuthenticationException))
  {
   var native=chain.OfType<Win32Exception>().LastOrDefault();
   uint? code=native is null?null:unchecked((uint)native.NativeErrorCode);
   return code switch
   {
    0x8009030E=>"TLS_CLIENT_CREDENTIAL_UNAVAILABLE",
    0x80090325 or 0x800B0109=>"TLS_CERTIFICATE_UNTRUSTED",
    0x80090328 or 0x800B0101=>"TLS_CERTIFICATE_EXPIRED",
    0x80092010 or 0x800B010C=>"TLS_CERTIFICATE_REVOKED",
    null=>"TLS_HANDSHAKE_FAILED",
    _=>$"TLS_HANDSHAKE_FAILED_0x{code.Value:X8}"
   };
  }
  if(error.HttpRequestError==HttpRequestError.ProxyTunnelError)return "NETWORK_PROXY_FAILED";
  if(error.HttpRequestError==HttpRequestError.NameResolutionError||chain.OfType<SocketException>().Any(e=>e.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain))return "NETWORK_DNS_FAILED";
  if(error.HttpRequestError==HttpRequestError.ConnectionError||chain.Any(e=>e is SocketException))return "NETWORK_CONNECTION_FAILED";
  return "NETWORK_OR_TLS_FAILED";
 }
}
