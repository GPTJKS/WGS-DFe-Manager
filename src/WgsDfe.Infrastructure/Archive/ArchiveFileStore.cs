using System.Globalization;
using System.Security.Cryptography;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Parsing;
namespace WgsDfe.Infrastructure.Archive;
public sealed class ArchiveGate:IDisposable
{
 internal SemaphoreSlim Semaphore {get;}=new(1,1);
 public void Dispose()=>Semaphore.Dispose();
}
public sealed record ArchiveLocation(string DataDirectory,Guid OrganizationId);
public interface IArchiveFileStore
{
 string GetRelativePath(DfeParseResult parsed);
 Task<bool> PutAsync(string relativePath,string hash,ReadOnlyMemory<byte> bytes,CancellationToken ct);
 Task<byte[]> ReadAsync(string relativePath,string hash,CancellationToken ct);
 bool IsInsideData(string path);
 IEnumerable<string> EnumeratePhysicalFiles();
}
public sealed class ArchiveFileStore(ArchiveLocation location):IArchiveFileStore
{
 private string Root=>Path.GetFullPath(location.DataDirectory);
 private string Prefix=>$"XML/{location.OrganizationId:N}/";
 private static StringComparison Comparison=>OperatingSystem.IsWindows()?StringComparison.OrdinalIgnoreCase:StringComparison.Ordinal;
 public string GetRelativePath(DfeParseResult parsed)
 {
  var k=parsed.Key;string environment=parsed.Environment==FiscalEnvironment.Production?"Producao":"Homologacao";
  return $"{Prefix}{environment}/{k.Year:D4}/{k.Month:D2}/{k.IssuerTaxId}/{k.Model}/{parsed.Sha256}.xml";
 }
 private string Resolve(string relativePath)
 {
  if(!relativePath.StartsWith(Prefix,StringComparison.Ordinal)||relativePath.Contains('\\')||relativePath.Split('/').Any(s=>s is "" or "." or ".."||s.Contains(':'))||Path.IsPathRooted(relativePath))throw new ArchiveException(ArchiveError.UnsafePath);
  string full=Path.GetFullPath(Path.Combine(Root,relativePath.Replace('/',Path.DirectorySeparatorChar)));
  if(!full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,Comparison))throw new ArchiveException(ArchiveError.UnsafePath);
  CheckLinks(full);return full;
 }
 private void CheckLinks(string path)
 {
  string current=Root;
  if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ArchiveException(ArchiveError.UnsafePath);
  foreach(string part in Path.GetRelativePath(Root,path).Split(Path.DirectorySeparatorChar))
  {current=Path.Combine(current,part);if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ArchiveException(ArchiveError.UnsafePath);}
 }
 public bool IsInsideData(string path)
 {string full=Path.GetFullPath(path);return string.Equals(full,Root,Comparison)||full.StartsWith(Root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,Comparison);}
 public async Task<bool> PutAsync(string relativePath,string hash,ReadOnlyMemory<byte> bytes,CancellationToken ct)
 {
  if(bytes.Length is <1 or >DfeParser.MaximumBytes||Convert.ToHexString(SHA256.HashData(bytes.Span))!=hash)throw new ArchiveException(ArchiveError.IntegrityMismatch);
  string path=Resolve(relativePath),temporary=path+"."+Guid.NewGuid().ToString("N",CultureInfo.InvariantCulture)+".part";
  try
  {
   if(File.Exists(path)){await ReadAsync(relativePath,hash,ct);return false;}
   Directory.CreateDirectory(Path.GetDirectoryName(path)!);CheckLinks(path);
   await using(var file=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){await file.WriteAsync(bytes,ct);file.Flush(true);}
   ct.ThrowIfCancellationRequested();CheckLinks(path);
   try{File.Move(temporary,path,false);return true;}
   catch(IOException)when(File.Exists(path)){await ReadAsync(relativePath,hash,ct);return false;}
  }
  catch(ArchiveException){throw;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new ArchiveException(ArchiveError.StorageUnavailable);}
  finally{try{File.Delete(temporary);}catch(IOException){}catch(UnauthorizedAccessException){}}
 }
 public async Task<byte[]> ReadAsync(string relativePath,string hash,CancellationToken ct)
 {
  string path=Resolve(relativePath);
  try
  {
   await using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(file.Length is <1 or >DfeParser.MaximumBytes)throw new ArchiveException(ArchiveError.IntegrityMismatch);
   var bytes=new byte[(int)file.Length];await file.ReadExactlyAsync(bytes,ct);
   if(Convert.ToHexString(SHA256.HashData(bytes))!=hash)throw new ArchiveException(ArchiveError.IntegrityMismatch);return bytes;
  }
  catch(FileNotFoundException){return await ReadExportedAsync(relativePath,hash,ct);}
  catch(DirectoryNotFoundException){return await ReadExportedAsync(relativePath,hash,ct);}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new ArchiveException(ArchiveError.StorageUnavailable);}
 }
 private async Task<byte[]> ReadExportedAsync(string source,string hash,CancellationToken ct)
 {
  var receipts=new WgsDfe.Infrastructure.Export.ExportReceiptStore(location);var receipt=await receipts.FindAsync(source,hash,ct);
  if(receipt is null)throw new ArchiveException(ArchiveError.FileMissing);
  return await receipts.ReadAsync(receipt,ct);
 }
 public IEnumerable<string> EnumeratePhysicalFiles()
 {
  string root=Resolve(Prefix+"inventory");root=Path.GetDirectoryName(root)!;
  if(!Directory.Exists(root))yield break;
  foreach(string path in Directory.EnumerateFiles(root,"*.xml",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,MaxRecursionDepth=32,IgnoreInaccessible=false}))
  {CheckLinks(path);yield return Path.GetRelativePath(Root,path).Replace(Path.DirectorySeparatorChar,'/');}
 }
}
