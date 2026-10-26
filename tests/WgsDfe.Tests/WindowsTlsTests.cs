using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WgsDfe.Infrastructure.Certificates;
using Xunit;
namespace WgsDfe.Tests;
public sealed class WindowsTlsTests
{
 [WindowsTlsFact]public async Task UserProfileKeyCanAuthenticateClientWithSchannel()
 {
  using var rsa=RSA.Create(2048);
  var request=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  var san=new SubjectAlternativeNameBuilder();san.AddDnsName("localhost");request.CertificateExtensions.Add(san.Build());
  request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,true));
  using var original=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
  byte[] pfx=original.Export(X509ContentType.Pkcs12,"test-only");
  try
  {
   using var serverCertificate=PfxCodec.LoadForTls(pfx,"test-only");using var clientCertificate=PfxCodec.LoadForTls(pfx,"test-only");
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(15));
   using var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
   var port=((IPEndPoint)listener.LocalEndpoint).Port;
   var server=Task.Run(async()=>
   {
    using var connection=await listener.AcceptTcpClientAsync(deadline.Token);
    // Synthetic local test: pin the exact self-signed test certificate, never accept arbitrary peers.
    using var tls=new SslStream(connection.GetStream(),false,(_,peer,_,_)=>peer is not null&&peer.GetRawCertData().AsSpan().SequenceEqual(original.RawData));
    await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions{ServerCertificate=serverCertificate,ClientCertificateRequired=true,EnabledSslProtocols=SslProtocols.Tls12,CertificateRevocationCheckMode=X509RevocationMode.NoCheck},deadline.Token);
    Assert.True(tls.IsMutuallyAuthenticated);await tls.WriteAsync(new byte[]{42},deadline.Token);
   });
   using var client=new TcpClient();await client.ConnectAsync(IPAddress.Loopback,port,deadline.Token);
   using var stream=new SslStream(client.GetStream(),false,(_,peer,_,_)=>peer is not null&&peer.GetRawCertData().AsSpan().SequenceEqual(original.RawData));
   try
   {
    await stream.AuthenticateAsClientAsync(new SslClientAuthenticationOptions{TargetHost="localhost",ClientCertificates=new(){clientCertificate},EnabledSslProtocols=SslProtocols.Tls12,CertificateRevocationCheckMode=X509RevocationMode.NoCheck},deadline.Token);
    var buffer=new byte[1];Assert.Equal(1,await stream.ReadAsync(buffer,deadline.Token));Assert.Equal(42,buffer[0]);await server;
   }
   finally{deadline.Cancel();listener.Stop();try{await server;}catch(OperationCanceledException){}}
  }
  finally{CryptographicOperations.ZeroMemory(pfx);}
 }
}
public sealed class WindowsTlsFactAttribute:FactAttribute
{public WindowsTlsFactAttribute(){if(!OperatingSystem.IsWindows())Skip="Schannel requires Windows; run before fiscal homologation.";}}
