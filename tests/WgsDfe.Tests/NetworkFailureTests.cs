using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Authentication;
using WgsDfe.Infrastructure.Nfe;
using Xunit;
namespace WgsDfe.Tests;
public sealed class NetworkFailureTests
{
 [Theory]
 [InlineData(HttpRequestError.NameResolutionError,"NETWORK_DNS_FAILED")]
 [InlineData(HttpRequestError.ConnectionError,"NETWORK_CONNECTION_FAILED")]
 [InlineData(HttpRequestError.ProxyTunnelError,"NETWORK_PROXY_FAILED")]
 [InlineData(HttpRequestError.SecureConnectionError,"TLS_HANDSHAKE_FAILED")]
 [InlineData(HttpRequestError.Unknown,"NETWORK_OR_TLS_FAILED")]
 public void ClassifiesNetworkStageWithoutLeakingMessages(HttpRequestError stage,string expected)
 {Assert.Equal(expected,NetworkFailure.Code(new HttpRequestException(stage,"password=secret; private proxy address")));}
 [Theory]
 [InlineData(0x8009030Eu,"TLS_CLIENT_CREDENTIAL_UNAVAILABLE")]
 [InlineData(0x80090325u,"TLS_CERTIFICATE_UNTRUSTED")]
 [InlineData(0x80090328u,"TLS_CERTIFICATE_EXPIRED")]
 [InlineData(0x80092010u,"TLS_CERTIFICATE_REVOKED")]
 [InlineData(0x80090326u,"TLS_HANDSHAKE_FAILED_0x80090326")]
 public void KeepsSafeNativeTlsReason(uint native,string expected)
 {var inner=new AuthenticationException("secret",new Win32Exception(unchecked((int)native),"secret"));Assert.Equal(expected,NetworkFailure.Code(new HttpRequestException(HttpRequestError.SecureConnectionError,"secret",inner)));}
 [Fact]public void DetectsLegacyWrappedDnsError()
 {Assert.Equal("NETWORK_DNS_FAILED",NetworkFailure.Code(new HttpRequestException("secret",new SocketException((int)SocketError.HostNotFound))));}
}
