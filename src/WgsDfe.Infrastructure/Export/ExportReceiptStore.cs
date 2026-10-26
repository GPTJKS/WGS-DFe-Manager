using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Parsing;
namespace WgsDfe.Infrastructure.Export;
public sealed record ExportReceipt(string Source,string Hash,string Output,string Entry,ExportFormat Format,DateTime PublishedAtUtc);
/// <summary>Local receipts are evidence of copying only; fiscal status stays in the database.</summary>
public sealed class ExportReceiptStore(ArchiveLocation location)
{
 private string Root=>Path.Combine(location.DataDirectory,"ExportReceipts");
 private string ReceiptPath(string hash)
 {
  if(hash.Length!=64||hash.Any(c=>!char.IsAsciiHexDigit(c)))throw new ArchiveException(ArchiveError.UnsafePath);
  return Path.Combine(Root,hash+".json");
 }
 public async Task SaveAsync(ExportReceipt receipt,CancellationToken ct)
 {
  Directory.CreateDirectory(Root);string path=ReceiptPath(receipt.Hash),temp=path+"."+Guid.NewGuid().ToString("N")+".partial";
  try{await File.WriteAllTextAsync(temp,JsonSerializer.Serialize(receipt),ct);File.Move(temp,path,true);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
 public async Task<ExportReceipt?> FindAsync(string source,string hash,CancellationToken ct)
 {
  var path=ReceiptPath(hash);if(!File.Exists(path))return null;
  try{var receipt=JsonSerializer.Deserialize<ExportReceipt>(await File.ReadAllTextAsync(path,ct));return receipt?.Hash==hash&&receipt.Source==source?receipt:null;}
  catch(Exception e)when(e is IOException or JsonException or UnauthorizedAccessException){return null;}
 }
 public async Task<byte[]> ReadAsync(ExportReceipt receipt,CancellationToken ct)
 {
  try
  {
   if(!Path.IsPathFullyQualified(receipt.Output)||receipt.Entry.Contains('\\')||receipt.Entry.Split('/').Any(p=>p is "" or "." or ".."||p.Contains(':')))throw new ArchiveException(ArchiveError.UnsafePath);
   var data=Path.GetFullPath(location.DataDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
   if(Path.GetFullPath(receipt.Output).StartsWith(data,StringComparison.OrdinalIgnoreCase))throw new ArchiveException(ArchiveError.UnsafePath);
   byte[] bytes;
   CheckLinks(receipt.Output);
   if(receipt.Format==ExportFormat.Folder)
   {
    var file=Path.GetFullPath(Path.Combine(receipt.Output,receipt.Entry.Replace('/',Path.DirectorySeparatorChar)));
    if(!file.StartsWith(Path.GetFullPath(receipt.Output).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new ArchiveException(ArchiveError.UnsafePath);
    CheckLinks(file);await using var stream=File.OpenRead(file);bytes=await ReadBounded(stream,stream.Length,ct);
   }
   else if(receipt.Format==ExportFormat.Zip)
   {
    using var zip=ZipFile.OpenRead(receipt.Output);var entry=zip.GetEntry(receipt.Entry)??throw new ArchiveException(ArchiveError.FileMissing);
    await using var stream=entry.Open();bytes=await ReadBounded(stream,entry.Length,ct);
   }
   else throw new ArchiveException(ArchiveError.UnsafePath);
   if(Convert.ToHexString(SHA256.HashData(bytes))!=receipt.Hash)throw new ArchiveException(ArchiveError.IntegrityMismatch);
   return bytes;
  }
  catch(ArchiveException){throw;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException){throw new ArchiveException(ArchiveError.StorageUnavailable);}
 }
 private static async Task<byte[]> ReadBounded(Stream input,long length,CancellationToken ct)
 {
  if(length is <1 or >DfeParser.MaximumBytes)throw new ArchiveException(ArchiveError.IntegrityMismatch);
  var result=new byte[(int)length];await input.ReadExactlyAsync(result,ct);if(input.ReadByte()!=-1)throw new ArchiveException(ArchiveError.IntegrityMismatch);return result;
 }
 internal static void CheckLinks(string path)
 {
  string full=Path.GetFullPath(path),current=Path.GetPathRoot(full)!;
  foreach(var part in full[current.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
  {current=Path.Combine(current,part);if((File.Exists(current)||Directory.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ArchiveException(ArchiveError.UnsafePath);}
 }
}
