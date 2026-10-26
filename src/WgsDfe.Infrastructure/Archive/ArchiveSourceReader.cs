using System.IO.Compression;
using WgsDfe.Application.Archive;
using WgsDfe.Infrastructure.Parsing;
namespace WgsDfe.Infrastructure.Archive;
internal sealed record ArchiveSource(string Label,Func<CancellationToken,Task<byte[]>>? Read,ArchiveOutcome? Outcome=null,string Code="");
internal static class ArchiveSourceReader
{
 public const int MaxFiles=10000;
 public const long MaxZipBytes=250L*1024*1024,MaxExpandedBytes=512L*1024*1024;
 public static IEnumerable<ArchiveSource> Enumerate(IReadOnlyList<string> paths,IArchiveFileStore store,CancellationToken ct)
 {
  int count=0,visited=0;var pending=new Stack<(string Path,int Depth)>();foreach(string path in paths.Reverse())pending.Push((path,0));
  var seen=new HashSet<string>(OperatingSystem.IsWindows()?StringComparer.OrdinalIgnoreCase:StringComparer.Ordinal);
  while(pending.Count>0)
  {
   ct.ThrowIfCancellationRequested();if(++visited>MaxFiles){yield return new("Lote",null,ArchiveOutcome.LimitExceeded,"SOURCE_COUNT_LIMIT");yield break;}var item=pending.Pop();string full;
   try{full=Path.GetFullPath(item.Path);}catch(Exception e)when(e is ArgumentException or NotSupportedException){full="";}
   if(full==""){yield return new("Entrada",null,ArchiveOutcome.SourceUnavailable,"INVALID_PATH");continue;}
   if(!seen.Add(full))continue;
   var inspect=Inspect(full,item.Depth,store);
   if(inspect.Code is not null){yield return new(Label(full),null,inspect.Outcome,inspect.Code);continue;}
   if(inspect.Children is not null)
   {
    if(pending.Count+inspect.Children.Count>MaxFiles){yield return new("Lote",null,ArchiveOutcome.LimitExceeded,"SOURCE_COUNT_LIMIT");yield break;}
    foreach(var child in inspect.Children)pending.Push((child,item.Depth+1));continue;
   }
   if(++count>MaxFiles){yield return new("Lote",null,ArchiveOutcome.LimitExceeded,"SOURCE_COUNT_LIMIT");yield break;}
   if(Path.GetExtension(full).Equals(".zip",StringComparison.OrdinalIgnoreCase))
   {
    var zip=TryZip(full);if(zip.Error is not null){yield return new(Label(full),null,zip.Outcome,zip.Error);continue;}
    using var package=zip.Archive!;using var stream=zip.Stream!;
    foreach(var entry in package.Entries)
    {
     ct.ThrowIfCancellationRequested();if(entry.FullName.EndsWith('/')||entry.FullName.EndsWith('\\'))continue;
     if(++count>MaxFiles){yield return new("Lote",null,ArchiveOutcome.LimitExceeded,"SOURCE_COUNT_LIMIT");yield break;}
     string label=Label(full)+" ! "+Label(entry.FullName);
     if(!Path.GetExtension(entry.FullName).Equals(".xml",StringComparison.OrdinalIgnoreCase)){yield return new(label,null,ArchiveOutcome.Ignored,"NON_XML_OR_NESTED_ZIP");continue;}
     yield return new(label,async token=>
     {
      await using var content=entry.Open();var data=await ReadBounded(content,token);
      if(data.LongLength!=entry.Length||Crc(data)!=entry.Crc32)throw new InvalidDataException();return data;
     });
    }
   }
   else if(Path.GetExtension(full).Equals(".xml",StringComparison.OrdinalIgnoreCase))
    yield return new(Label(full),async token=>{await using var file=new FileStream(full,FileMode.Open,FileAccess.Read,FileShare.Read);return await ReadBounded(file,token);});
   else yield return new(Label(full),null,ArchiveOutcome.Ignored,"NON_XML_SOURCE");
  }
 }
 private static (IReadOnlyList<string>? Children,ArchiveOutcome Outcome,string? Code) Inspect(string path,int depth,IArchiveFileStore store)
 {
  try
  {
   if(store.IsInsideData(path))return(null,ArchiveOutcome.Ignored,"OWN_DATA_DIRECTORY");
   var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)return(null,ArchiveOutcome.Ignored,"LINK_SKIPPED");
   if((attributes&FileAttributes.Directory)!=0)
   {
    if(depth>=32)return(null,ArchiveOutcome.LimitExceeded,"DIRECTORY_DEPTH_LIMIT");
    // Listagem limitada; nunca segue links e não extrai ZIP no disco.
    var entries=Directory.EnumerateFileSystemEntries(path).Take(MaxFiles+1).ToArray();
    if(entries.Length>MaxFiles)return(null,ArchiveOutcome.LimitExceeded,"DIRECTORY_COUNT_LIMIT");
    return(entries,default,null);
   }
   return(null,default,null);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArgumentException){return(null,ArchiveOutcome.SourceUnavailable,"SOURCE_UNAVAILABLE");}
 }
 private static (ZipArchive? Archive,FileStream? Stream,ArchiveOutcome Outcome,string? Error) TryZip(string path)
 {
  FileStream? stream=null;ZipArchive? archive=null;
  try
  {
   stream=new(path,FileMode.Open,FileAccess.Read,FileShare.Read);if(stream.Length>MaxZipBytes)throw new ZipLimitException();
   archive=new(stream,ZipArchiveMode.Read,true);if(archive.Entries.Count>MaxFiles)throw new ZipLimitException();long total=0;
   foreach(var entry in archive.Entries)
   {
    string name=entry.FullName.Replace('\\','/');int unixType=(entry.ExternalAttributes>>16)&0xF000;
    if(name.Length is <1 or >512||name.StartsWith('/')||name.Contains(':')||name.Any(char.IsControl)||name.Split('/').Any(x=>x is "." or "..")||unixType is 0xA000 or 0x6000 or 0x2000||(entry.ExternalAttributes&0x400)!=0)throw new InvalidDataException();
    total=checked(total+entry.Length);if(total>MaxExpandedBytes)throw new ZipLimitException();
    if(Path.GetExtension(name).Equals(".xml",StringComparison.OrdinalIgnoreCase)&&(entry.Length>DfeParser.MaximumBytes||(entry.Length>1024*1024&&entry.Length/Math.Max(1,entry.CompressedLength)>200)))throw new ZipLimitException();
   }
   return(archive,stream,default,null);
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or OverflowException or ZipLimitException or NotSupportedException)
  {archive?.Dispose();stream?.Dispose();return(null,null,e is ZipLimitException or OverflowException?ArchiveOutcome.LimitExceeded:ArchiveOutcome.UnsafeArchive,e is ZipLimitException or OverflowException?"ZIP_LIMIT":"ZIP_INVALID_OR_UNSAFE");}
 }
 private sealed class ZipLimitException:Exception;
 private static async Task<byte[]> ReadBounded(Stream source,CancellationToken ct)
 {
  using var target=new MemoryStream();var buffer=new byte[65536];int n;
  while((n=await source.ReadAsync(buffer,ct))>0){if(target.Length+n>DfeParser.MaximumBytes)throw new ZipLimitException();await target.WriteAsync(buffer.AsMemory(0,n),ct);}
  if(target.Length==0)throw new InvalidDataException();return target.ToArray();
 }
 public static bool IsSizeError(Exception e)=>e is ZipLimitException;
 public static string Label(string value)=>new(value.Where(c=>!char.IsControl(c)).Take(300).ToArray());
 private static uint Crc(ReadOnlySpan<byte> bytes){uint crc=uint.MaxValue;foreach(byte b in bytes)crc=(crc>>8)^CrcTable[(crc^b)&255];return ~crc;}
 private static readonly uint[] CrcTable=Enumerable.Range(0,256).Select(i=>{uint v=(uint)i;for(int n=0;n<8;n++)v=(v&1)!=0?0xEDB88320U^(v>>1):v>>1;return v;}).ToArray();
}
