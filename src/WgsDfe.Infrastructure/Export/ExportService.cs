using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Export;
public sealed partial class ExportService
{
 private sealed record VerifiedFile(string Path,string Hash,long Bytes);
 public async Task<ExportResult> ExportAsync(ExportRequest request,IProgress<ExportProgress>? progress=null,CancellationToken ct=default)
 {
  if(request.Format==ExportFormat.Folder)return await ExportFolderAsync(request,progress,ct);
  await exportGate.Semaphore.WaitAsync(ct);
  var metadata=new List<VerifiedFile>();List<PlannedXml>? plan=null;var rows=new List<ExportRow>();var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Export,Status=JobStatus.Running,StartedAtUtc=Now};
  string? stage=null,output=null;bool stageOwned=false,published=false,cancelled=false,failed=false,jobSaved=false;string code="EXPORT_COMPLETED";int written=0,skipped=0;long bytes=0;
  try
  {
   plan=await Plan(request,ct);string parent=Destination(request.Destination);
   string name=ReadableExportPath.Segment(plan.Select(x=>x.Details.Folder).Distinct().Count()==1?plan[0].Details.Folder:"XMLs de várias empresas",80)+" - "+ReadableExportPath.Interval(request.Filter.From,request.Filter.To);output=Path.Combine(parent,name+(request.Format==ExportFormat.Zip?".zip":""));stage=Path.Combine(parent,".exportacao-"+job.Id.ToString("N")+".partial");
   CheckOutsideData(output);CheckPath(output);CheckPath(stage);if(Directory.Exists(output)||File.Exists(stage)||Directory.Exists(stage))throw new ExportException("EXPORT_DESTINATION_INVALID");
   job.TotalItems=plan.Count;db.ProcessingJobs.Add(job);Audit(AuditAction.ExportStarted,job.Id,"EXPORT_STARTED");await db.SaveChangesAsync(ct);jobSaved=true;
   ZipArchive? zip=null;FileStream? zipFile=null;
   try
   {
    if(request.Format==ExportFormat.Zip){zipFile=new FileStream(stage,FileMode.CreateNew,FileAccess.ReadWrite,FileShare.None);stageOwned=true;zip=new ZipArchive(zipFile,ZipArchiveMode.Create,true,Encoding.UTF8);}
    else{Directory.CreateDirectory(stage);stageOwned=true;CheckPath(stage);}
    foreach(var item in plan)
    {
     ct.ThrowIfCancellationRequested();byte[]? payload=null;string rowCode="EXPORTED";
     try
     {
      payload=await files.ReadAsync(item.SourcePath,item.Hash,ct);var parsed=parser.Parse(payload,ct);
      if(parsed.Key.Value!=item.Key||parsed.Environment!=item.Environment||parsed.Key.Model!=item.Model||
       (item.Event?(parsed.Kind!=DfePayloadKind.Event||parsed.EventType!=item.EventType||parsed.EventSequence!=item.Sequence):parsed.Kind!=DfePayloadKind.FullDocument))throw new ExportException("EXPORT_SOURCE_INVALID");
     }
     catch(ArchiveException e){rowCode=e.Code.ToString();}
     catch(DfeParseException){rowCode="XML_INVALID";}
     catch(ExportException){rowCode="XML_INVALID";}
     if(rowCode!="EXPORTED")
     {
      rows.Add(new(item.Destination,item.Key,item.Model,item.Environment,item.Event?"Evento":"Documento",item.Hash,0,rowCode){Details=item.Details});skipped++;AddJobItem(item,rowCode,false);
      if(!request.SkipInvalid)throw new ExportException("EXPORT_SOURCE_INVALID");
     }
     else
     {
      bytes=checked(bytes+payload!.LongLength);if(bytes>limits.MaxBytes)throw new ExportException("EXPORT_LIMIT");
      await Write(item.Destination,payload,zip,stage,ct);written++;
      rows.Add(new(item.Destination,item.Key,item.Model,item.Environment,item.Event?"Evento":"Documento",item.Hash,payload.Length,"EXPORTED"){Details=item.Details});AddJobItem(item,"EXPORTED",true);
     }
     if(rows.Count%200==0){job.ProcessedItems=rows.Count;await db.SaveChangesAsync(ct);db.ChangeTracker.Clear();job=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,ct);}
     progress?.Report(new(rows.Count,plan.Count,written,skipped,"EXPORT_COPYING"));
    }
    if(written==0)throw new ExportException("EXPORT_NO_VALID_FILES");

   }
   finally{zip?.Dispose();if(zipFile is not null){zipFile.Flush(true);await zipFile.DisposeAsync();}}
   progress?.Report(new(rows.Count,plan.Count,written,skipped,"EXPORT_VERIFYING"));
   await Verify(stage,request.Format,rows,metadata,ct);ct.ThrowIfCancellationRequested();
   job.ProcessedItems=rows.Count;job.LastErrorCode="EXPORT_PREPARED";Audit(AuditAction.ExportPrepared,job.Id,"EXPORT_PREPARED");await db.SaveChangesAsync(ct);
   CheckPath(stage);CheckPath(output);CheckOutsideData(output);ct.ThrowIfCancellationRequested();
   if(File.Exists(output)){string stageHash;await using(var staged=File.OpenRead(stage))stageHash=Convert.ToHexString(await SHA256.HashDataAsync(staged,ct));if(!await Matches(output,stageHash,new FileInfo(stage).Length,ct))throw new ExportException("EXPORT_DESTINATION_CONFLICT");File.Delete(stage);skipped=rows.Count;written=0;for(int i=0;i<rows.Count;i++)if(rows[i].Code=="EXPORTED")rows[i]=rows[i] with{Code="ALREADY_EXISTS"};}else File.Move(stage,output,false);
   published=true;code=rows.Any(x=>x.Code is not ("EXPORTED" or "ALREADY_EXISTS"))?"EXPORT_WARNINGS":"EXPORT_COMPLETED";
   try
   {
    var receipts=new ExportReceiptStore(location);
    var exportedPaths=rows.Where(r=>r.Code is "EXPORTED" or "ALREADY_EXISTS").Select(r=>r.Path).ToHashSet(StringComparer.Ordinal);
    foreach(var item in plan.Where(x=>exportedPaths.Contains(x.Destination)).DistinctBy(x=>(x.SourcePath,x.Hash)))
     await receipts.SaveAsync(new(item.SourcePath,item.Hash,output,item.Destination,request.Format,Now),CancellationToken.None);
    var history=Path.Combine(location.DataDirectory,"ExportHistory");Directory.CreateDirectory(history);
    await File.WriteAllTextAsync(Path.Combine(history,Now.ToString("yyyyMMddHHmmss")+"_"+job.Id.ToString("N")+".json"),JsonSerializer.Serialize(new ExportHistoryEntry(job.Id,Now,output,written,skipped,rows.Select(x=>x.Details.Folder).Distinct().Count())),CancellationToken.None);
   }
   catch(Exception e)when(e is IOException or UnauthorizedAccessException){code="EXPORT_PUBLISHED_RECEIPT_PENDING";}
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;code="EXPORT_CANCELLED";}
  catch(ExportException e){failed=true;code=e.Code;}
  catch(ArchiveException){failed=true;code="EXPORT_SOURCE_INVALID";}
  catch{failed=true;code="EXPORT_FAILED";}
  finally
  {
   try
   {
    int processed=rows.Count;
    if(!published&&plan is not null)
    {
     for(int i=0;i<rows.Count;i++)if(rows[i].Code=="EXPORTED")rows[i]=rows[i] with{Code="NOT_PUBLISHED"};
     var attempted=rows.Select(x=>x.Path).ToHashSet(PathComparer);
     foreach(var item in plan.Where(x=>!attempted.Contains(x.Destination)))rows.Add(new(item.Destination,item.Key,item.Model,item.Environment,item.Event?"Evento":"Documento",item.Hash,0,"NOT_PROCESSED"){Details=item.Details});
    }
    if(stageOwned&&!published&&stage is not null)
    {
     try{CheckPath(stage);if(File.Exists(stage))File.Delete(stage);else if(Directory.Exists(stage))Directory.Delete(stage,true);}
     catch(IOException){}catch(UnauthorizedAccessException){}catch(ExportException){}
    }
    if(jobSaved)try{await db.SaveChangesAsync(CancellationToken.None);}catch{}
    db.ChangeTracker.Clear();
    if(jobSaved)
    {
     try
     {
      var saved=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,CancellationToken.None);saved.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:rows.Any(x=>x.Code is not ("EXPORTED" or "ALREADY_EXISTS"))?JobStatus.CompletedWithWarnings:JobStatus.Completed;saved.ProcessedItems=processed;saved.CompletedAtUtc=Now;saved.LastErrorCode=failed||cancelled?code:null;
      if(!published)await db.ProcessingJobItems.Where(x=>x.JobId==job.Id).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Status,cancelled?JobStatus.Cancelled:JobStatus.Failed).SetProperty(x=>x.ErrorCode,x=>x.ErrorCode=="EXPORTED"?"NOT_PUBLISHED":x.ErrorCode),CancellationToken.None);
      Audit(published?AuditAction.ExportCompleted:cancelled?AuditAction.ExportCancelled:AuditAction.ExportFailed,job.Id,code);await db.SaveChangesAsync(CancellationToken.None);
     }
     catch{if(published)code="EXPORT_PUBLISHED_AUDIT_PENDING";}
    }
   }
   finally{exportGate.Semaphore.Release();}
  }
  return new(job.Id,published?output:null,published?written:0,published?skipped:rows.Count,cancelled,failed,code,rows.AsReadOnly());
  void AddJobItem(PlannedXml item,string result,bool valid)=>db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=job.Id,CompanyId=item.CompanyId,ItemReference=item.Destination,Model=item.Model,ProviderCode="LOCAL_ARCHIVE",Status=valid?JobStatus.Completed:JobStatus.CompletedWithWarnings,Availability=valid?Availability.Available:Availability.TemporaryError,ErrorCode=result});
 }
 private string Destination(string path)
 {
  try{if(!Path.IsPathFullyQualified(path))throw new ExportException("EXPORT_DESTINATION_INVALID");string full=Path.GetFullPath(path);if(!Directory.Exists(full)||files.IsInsideData(full))throw new ExportException("EXPORT_DESTINATION_INVALID");CheckPath(full);return full;}
  catch(ExportException){throw;}catch{throw new ExportException("EXPORT_DESTINATION_INVALID");}
 }
 private void CheckOutsideData(string path)
 {
  var data=Path.GetFullPath(location.DataDirectory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  string full=Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  // Case insensitive is conservative on Linux and matches the Windows target.
  if(full.StartsWith(data,StringComparison.OrdinalIgnoreCase)||data.StartsWith(full,StringComparison.OrdinalIgnoreCase))throw new ExportException("EXPORT_DESTINATION_INVALID");
 }
 private static void CheckPath(string path)
 {
  string full=Path.GetFullPath(path),current=Path.GetPathRoot(full)!;
  foreach(var part in full[current.Length..].Split(Path.DirectorySeparatorChar,StringSplitOptions.RemoveEmptyEntries))
  {
   current=Path.Combine(current,part);
   try{if((File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)throw new ExportException("EXPORT_DESTINATION_INVALID");}
   catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
  }
 }
 private static void ValidateRelative(string relative)
 {
  if(relative.Length>300||relative.Contains('\\')||relative.Contains(':')||Path.IsPathRooted(relative)||relative.Split('/').Any(p=>p.Length==0||p is "." or ".."))throw new ExportException("EXPORT_DESTINATION_INVALID");
 }
 private static string EntryPath(string root,string relative)
 {
  ValidateRelative(relative);
  var full=Path.GetFullPath(Path.Combine(root,relative.Replace('/',Path.DirectorySeparatorChar)));var prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
  if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new ExportException("EXPORT_DESTINATION_INVALID");CheckPath(full);return full;
 }
 private static async Task Write(string relative,byte[] bytes,ZipArchive? zip,string stage,CancellationToken ct)
 {
  if(zip is not null)
  {
   ValidateRelative(relative);
   var entry=zip.CreateEntry(relative,CompressionLevel.Optimal);entry.LastWriteTime=new DateTimeOffset(1980,1,1,0,0,0,TimeSpan.Zero);await using var output=entry.Open();await output.WriteAsync(bytes,ct);
  }
  else
  {
   var path=EntryPath(stage,relative);Directory.CreateDirectory(Path.GetDirectoryName(path)!);CheckPath(path);
   await using var output=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);await output.WriteAsync(bytes,ct);output.Flush(true);
  }
 }
 private static async Task Verify(string stage,ExportFormat format,IReadOnlyList<ExportRow> rows,IReadOnlyList<VerifiedFile> metadata,CancellationToken ct)
 {
  var written=rows.Where(x=>x.Code=="EXPORTED").Select(x=>new VerifiedFile(x.Path,x.Hash,x.Bytes)).Concat(metadata).ToArray();ZipArchive? zip=null;
  try
  {
   if(format==ExportFormat.Zip)zip=ZipFile.OpenRead(stage);
   var expected=written.Select(x=>x.Path).ToHashSet(StringComparer.Ordinal);
   if(zip is not null)
   {if(zip.Entries.Count!=expected.Count||zip.Entries.Select(x=>x.FullName).Distinct(StringComparer.Ordinal).Count()!=expected.Count||zip.Entries.Any(x=>!expected.Contains(x.FullName)))throw new ExportException("EXPORT_VERIFY_FAILED");}
   else
   {
    var allowedDirs=new HashSet<string>(StringComparer.Ordinal);foreach(var name in expected){var parts=name.Split('/');for(int i=1;i<parts.Length;i++)allowedDirs.Add(string.Join('/',parts.Take(i)));}
    var pending=new Stack<string>();pending.Push(stage);int count=0;
    while(pending.Count>0)foreach(var path in Directory.EnumerateFileSystemEntries(pending.Pop()))
    {
     ct.ThrowIfCancellationRequested();CheckPath(path);var relative=Path.GetRelativePath(stage,path).Replace(Path.DirectorySeparatorChar,'/');
     if(Directory.Exists(path)){if(!allowedDirs.Contains(relative))throw new ExportException("EXPORT_VERIFY_FAILED");pending.Push(path);}
     else{if(!expected.Contains(relative)||++count>expected.Count)throw new ExportException("EXPORT_VERIFY_FAILED");}
    }
    if(count!=expected.Count)throw new ExportException("EXPORT_VERIFY_FAILED");
   }
   foreach(var row in written)
   {
    ct.ThrowIfCancellationRequested();Stream input;
    if(zip is not null){var entry=zip.GetEntry(row.Path)??throw new ExportException("EXPORT_VERIFY_FAILED");if(entry.Length!=row.Bytes)throw new ExportException("EXPORT_VERIFY_FAILED");input=entry.Open();}
    else input=new FileStream(EntryPath(stage,row.Path),FileMode.Open,FileAccess.Read,FileShare.Read);
    await using(input)
    {
     using var sha=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);var buffer=new byte[32768];long length=0;int read;
     while((read=await input.ReadAsync(buffer,ct))!=0){length+=read;if(length>row.Bytes)throw new ExportException("EXPORT_VERIFY_FAILED");sha.AppendData(buffer,0,read);}
     if(length!=row.Bytes||Convert.ToHexString(sha.GetHashAndReset())!=row.Hash)throw new ExportException("EXPORT_VERIFY_FAILED");
    }
   }
  }
  catch(OperationCanceledException){throw;}catch{throw new ExportException("EXPORT_VERIFY_FAILED");}
  finally{zip?.Dispose();}
 }
 private void Audit(AuditAction action,Guid id,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=id,ResultCode=code});
}
