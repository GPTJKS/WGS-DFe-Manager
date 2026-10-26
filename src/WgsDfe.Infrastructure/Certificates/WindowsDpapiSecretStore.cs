using System.Security.Cryptography;
using System.Text;
using WgsDfe.Application;
using WgsDfe.Application.Certificates;
namespace WgsDfe.Infrastructure.Certificates;
public sealed class WindowsDpapiSecretStore(string directory,Guid organizationId):ISecretStore
{
 public const int MaximumSecretBytes=6*1024*1024;
 private string FilePath(string reference)
 {
  if(!Guid.TryParseExact(reference,"N",out var id)||id==Guid.Empty)throw new CertificateException(CertificateError.InvalidInput);
  return Path.Combine(directory,reference+".vault");
 }
 private byte[] Entropy(string reference)=>Encoding.UTF8.GetBytes($"WGS-DFe/v1/{organizationId:N}/{reference}");
 public async Task StoreAsync(string reference,ReadOnlyMemory<byte> secret,CancellationToken cancellationToken)
 {
  if(!OperatingSystem.IsWindows())throw new CertificateException(CertificateError.StoreUnavailable);
  var path=FilePath(reference);if(secret.Length is <1 or >MaximumSecretBytes)throw new CertificateException(CertificateError.InvalidInput);
  Directory.CreateDirectory(directory);var plain=secret.ToArray();byte[]? encrypted=null;var staging=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try
  {
   encrypted=ProtectedData.Protect(plain,Entropy(reference),DataProtectionScope.CurrentUser);
   await using(var stream=new FileStream(staging,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await stream.WriteAsync(encrypted,cancellationToken);stream.Flush(true);}
   cancellationToken.ThrowIfCancellationRequested();File.Move(staging,path,false);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException){throw new CertificateException(CertificateError.StoreUnavailable);}
  finally{CryptographicOperations.ZeroMemory(plain);if(encrypted is not null)CryptographicOperations.ZeroMemory(encrypted);try{File.Delete(staging);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
 public async Task<byte[]> ReadAsync(string reference,CancellationToken cancellationToken)
 {
  if(!OperatingSystem.IsWindows())throw new CertificateException(CertificateError.StoreUnavailable);var path=FilePath(reference);byte[]? encrypted=null;
  try
  {
   await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length is <1 or >MaximumSecretBytes+65536)throw new CertificateException(CertificateError.StoreUnavailable);
   encrypted=new byte[(int)stream.Length];await stream.ReadExactlyAsync(encrypted,cancellationToken);
   return ProtectedData.Unprotect(encrypted,Entropy(reference),DataProtectionScope.CurrentUser);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or CryptographicException){throw new CertificateException(CertificateError.StoreUnavailable);}
  finally{if(encrypted is not null)CryptographicOperations.ZeroMemory(encrypted);}
 }
 public Task RemoveAsync(string reference,CancellationToken cancellationToken)
 {
  if(!OperatingSystem.IsWindows())throw new CertificateException(CertificateError.StoreUnavailable);cancellationToken.ThrowIfCancellationRequested();
  try{File.Delete(FilePath(reference));return Task.CompletedTask;}catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new CertificateException(CertificateError.StoreUnavailable);}
 }
}
