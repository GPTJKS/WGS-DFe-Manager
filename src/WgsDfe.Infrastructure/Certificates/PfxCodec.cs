using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WgsDfe.Application.Certificates;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Certificates;
public static class PfxCodec
{
 public const int MaximumPfxBytes=5*1024*1024;
 public static X509Certificate2 Load(ReadOnlySpan<byte> pfx,ReadOnlySpan<char> password)=>LoadCore(pfx,password,X509KeyStorageFlags.EphemeralKeySet);
 // Schannel cannot reliably use ephemeral keys. No PersistKeySet, MachineKeySet or Exportable:
 // the temporary user-profile key is owned by the returned certificate and released on disposal.
 public static X509Certificate2 LoadForTls(ReadOnlySpan<byte> pfx,ReadOnlySpan<char> password)=>LoadCore(pfx,password,OperatingSystem.IsWindows()?X509KeyStorageFlags.UserKeySet:X509KeyStorageFlags.EphemeralKeySet);
 private static X509Certificate2 LoadCore(ReadOnlySpan<byte> pfx,ReadOnlySpan<char> password,X509KeyStorageFlags storage)
 {
  if(pfx.Length is <1 or >MaximumPfxBytes||password.Length>4096)throw new CertificateException(CertificateError.InvalidInput);
  X509Certificate2Collection collection;
  try{collection=X509CertificateLoader.LoadPkcs12Collection(pfx,password,storage,Pkcs12LoaderLimits.Defaults);}
  catch(CryptographicException){throw new CertificateException(CertificateError.InvalidPfx);}
  X509Certificate2? selected=null;
  try
  {
   var keys=collection.Cast<X509Certificate2>().Where(c=>c.HasPrivateKey).ToArray();
   if(keys.Length==0)throw new CertificateException(CertificateError.NoPrivateKey);
   if(keys.Length!=1)throw new CertificateException(CertificateError.AmbiguousBundle);
   selected=keys[0];if(selected.Extensions.OfType<X509BasicConstraintsExtension>().Any(x=>x.CertificateAuthority))throw new CertificateException(CertificateError.NoPrivateKey);
   ProvePrivateKey(selected);return selected;
  }
  catch{selected=null;throw;}
  finally{foreach(var certificate in collection)if(!ReferenceEquals(certificate,selected))certificate.Dispose();}
 }
 public static void ProvePrivateKey(X509Certificate2 certificate)
 {
  var challenge=RandomNumberGenerator.GetBytes(32);byte[]? signature=null;
  try
  {
   using var rsa=certificate.GetRSAPrivateKey();
   if(rsa is not null)
   {
    signature=rsa.SignData(challenge,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);using var pub=certificate.GetRSAPublicKey();
    if(pub?.VerifyData(challenge,signature,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1)!=true)throw new CertificateException(CertificateError.NoPrivateKey);return;
   }
   using var ec=certificate.GetECDsaPrivateKey();if(ec is null)throw new CertificateException(CertificateError.UnsupportedKey);
   signature=ec.SignData(challenge,HashAlgorithmName.SHA256);using var ecPub=certificate.GetECDsaPublicKey();
   if(ecPub?.VerifyData(challenge,signature,HashAlgorithmName.SHA256)!=true)throw new CertificateException(CertificateError.NoPrivateKey);
  }
  catch(CryptographicException){throw new CertificateException(CertificateError.NoPrivateKey);}
  finally{CryptographicOperations.ZeroMemory(challenge);if(signature is not null)CryptographicOperations.ZeroMemory(signature);}
 }
 // Envelope temporário em memória. Só é escrito pelo cofre, após cifra DPAPI.
 public static byte[] Pack(ReadOnlySpan<byte> pfx,ReadOnlySpan<char> password)
 {
  int n=Encoding.UTF8.GetByteCount(password);var result=new byte[checked(8+n+pfx.Length)];"WGS1"u8.CopyTo(result);
  BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(4),n);Encoding.UTF8.GetBytes(password,result.AsSpan(8,n));pfx.CopyTo(result.AsSpan(8+n));return result;
 }
 public static X509Certificate2 Unpack(ReadOnlySpan<byte> secret,bool forTls=false)
 {
  if(secret.Length<9||!secret[..4].SequenceEqual("WGS1"u8))throw new CertificateException(CertificateError.StoreUnavailable);
  int n=BinaryPrimitives.ReadInt32LittleEndian(secret[4..]);if(n<0||n>16384||n>secret.Length-9)throw new CertificateException(CertificateError.StoreUnavailable);
  var chars=new char[Encoding.UTF8.GetCharCount(secret.Slice(8,n))];
  try{Encoding.UTF8.GetChars(secret.Slice(8,n),chars);return forTls?LoadForTls(secret[(8+n)..],chars):Load(secret[(8+n)..],chars);}finally{CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));}
 }
 private static bool ValidCpf(string cpf)
 {
  if(cpf.Length!=11||cpf.Any(c=>!char.IsAsciiDigit(c))||cpf.Distinct().Count()==1)return false;
  for(int n=9;n<=10;n++){int sum=0;for(int i=0;i<n;i++)sum+=(cpf[i]-'0')*(n+1-i);int digit=(sum*10)%11;if(digit==10)digit=0;if(cpf[n]-'0'!=digit)return false;}return true;
 }
 // DOC-ICP-04: SAN .3.3 CNPJ; .3.1 CPF titular após nascimento. .3.4 responsável não é titular.
 public static string? HolderTaxId(X509Certificate2 certificate)
 {
  try
  {
   var san=certificate.Extensions.FirstOrDefault(x=>x.Oid?.Value=="2.5.29.17");if(san is null)return null;
   var root=new AsnReader(san.RawData,AsnEncodingRules.DER);var names=root.ReadSequence();root.ThrowIfNotEmpty();var ids=new HashSet<string>();var cpfs=new HashSet<string>();
   while(names.HasData)
   {
    var tag=new Asn1Tag(TagClass.ContextSpecific,0,true);if(!names.PeekTag().HasSameClassAndValue(tag)){names.ReadEncodedValue();continue;}
    var other=names.ReadSequence(tag);var oid=other.ReadObjectIdentifier();if(oid is not ("2.16.76.1.3.3" or "2.16.76.1.3.1"))continue;
    var value=other.ReadSequence(tag);var vt=value.PeekTag();
    string? text=vt.TagClass==TagClass.Universal&&vt.TagValue is 12 or 19 or 22?value.ReadCharacterString((UniversalTagNumber)vt.TagValue):vt.TagClass==TagClass.Universal&&vt.TagValue==4?Encoding.ASCII.GetString(value.ReadOctetString()):null;
    value.ThrowIfNotEmpty();other.ThrowIfNotEmpty();
    if(oid=="2.16.76.1.3.3"&&text is not null&&Cnpj.TryNormalize(text,out var id))ids.Add(id);
    if(oid=="2.16.76.1.3.1"&&text is {Length:>=19}&&ValidCpf(text.Substring(8,11)))cpfs.Add(text.Substring(8,11));
   }
   return ids.Count>0?(ids.Count==1?ids.Single():null):cpfs.Count==1?cpfs.Single():null;
  }
  catch(AsnContentException){return null;}
 }
}
