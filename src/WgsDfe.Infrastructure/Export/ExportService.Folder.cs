using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Export;
public sealed partial class ExportService
{
 private async Task<ExportResult> ExportFolderAsync(ExportRequest request,IProgress<ExportProgress>? progress,CancellationToken ct)
 {
  await exportGate.Semaphore.WaitAsync(ct);
  var rows=new List<ExportRow>();List<PlannedXml>? plan=null;string? root=null;bool cancelled=false,failed=false,saved=false;int written=0,skipped=0;long totalBytes=0;string code="EXPORT_COMPLETED";
  var createdDirectories=new HashSet<string>(PathComparer);
  var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Export,Status=JobStatus.Running,StartedAtUtc=Now};
  try
  {
   plan=await Plan(request,ct);root=Destination(request.Destination);CheckOutsideData(root);
   foreach(var item in plan)if(EntryPath(root,item.Destination).Length>259)throw new ExportException("EXPORT_PATH_TOO_LONG");
   job.TotalItems=plan.Count;db.ProcessingJobs.Add(job);Audit(AuditAction.ExportStarted,job.Id,"EXPORT_STARTED");await db.SaveChangesAsync(ct);saved=true;
   foreach(var item in plan)
   {
    ct.ThrowIfCancellationRequested();string result="EXPORTED";byte[]? payload=null;
    try
    {
     payload=await files.ReadAsync(item.SourcePath,item.Hash,ct);var parsed=parser.Parse(payload,ct);
     if(parsed.Key.Value!=item.Key||parsed.Environment!=item.Environment||parsed.Key.Model!=item.Model||(item.Event?(parsed.Kind!=DfePayloadKind.Event||parsed.EventType!=item.EventType||parsed.EventSequence!=item.Sequence):parsed.Kind!=DfePayloadKind.FullDocument))throw new ExportException("EXPORT_SOURCE_INVALID");
    }
    catch(ArchiveException e){result=e.Code.ToString();}
    catch(DfeParseException){result="XML_INVALID";}
    catch(ExportException){result="XML_INVALID";}
    if(result=="EXPORTED")
    {
     totalBytes=checked(totalBytes+payload!.LongLength);if(totalBytes>limits.MaxBytes)throw new ExportException("EXPORT_LIMIT");
     string path=EntryPath(root,item.Destination);
     if(Directory.Exists(path))result="EXPORT_DESTINATION_CONFLICT";
     else if(File.Exists(path))result=await Matches(path,item.Hash,payload!.Length,ct)?"ALREADY_EXISTS":"EXPORT_DESTINATION_CONFLICT";
     else
     {
      string parent=Path.GetDirectoryName(path)!;
      EnsureDirectories(parent);
      string temp=Path.Combine(parent,".exportacao-"+Guid.NewGuid().ToString("N")+".tmp");bool owned=false;
      try
      {
       CheckPath(path);CheckPath(temp);
       await using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){owned=true;await stream.WriteAsync(payload!,ct);stream.Flush(true);}
       progress?.Report(new(rows.Count,plan.Count,written,skipped,"EXPORT_VERIFYING"));
       if(!await Matches(temp,item.Hash,payload!.Length,ct))throw new ExportException("EXPORT_VERIFY_FAILED");
       ct.ThrowIfCancellationRequested();CheckPath(parent);CheckPath(path);
       try{File.Move(temp,path,false);owned=false;written++;}
       catch(IOException)when(File.Exists(path)){CheckPath(path);result=await Matches(path,item.Hash,payload.Length,ct)?"ALREADY_EXISTS":"EXPORT_DESTINATION_CONFLICT";}
      }
      finally{if(owned){CheckPath(temp);File.Delete(temp);}}
     }
    }
    if(result!="EXPORTED")skipped++;
    rows.Add(new(item.Destination,item.Key,item.Model,item.Environment,item.Event?"Evento":"Documento",item.Hash,payload?.Length??0,result){Details=item.Details});
    bool valid=result is "EXPORTED" or "ALREADY_EXISTS";
    db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=job.Id,CompanyId=item.CompanyId,ItemReference=item.Destination,Model=item.Model,ProviderCode="LOCAL_ARCHIVE",Status=valid?JobStatus.Completed:JobStatus.CompletedWithWarnings,Availability=valid?Availability.Available:Availability.TemporaryError,ErrorCode=result});
    job.ProcessedItems=rows.Count;await db.SaveChangesAsync(CancellationToken.None);
    if(rows.Count%200==0){db.ChangeTracker.Clear();job=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,CancellationToken.None);}
    if(valid)
    {
     // Receipts stay in application storage, never in the user's XML destination.
     await new ExportReceiptStore(location).SaveAsync(new(item.SourcePath,item.Hash,root,item.Destination,ExportFormat.Folder,Now),CancellationToken.None);
    }
    progress?.Report(new(rows.Count,plan.Count,written,skipped,"EXPORT_COPYING"));
    if(!valid&&result!="EXPORT_DESTINATION_CONFLICT"&&!request.SkipInvalid)throw new ExportException("EXPORT_SOURCE_INVALID");
   }
   if(!rows.Any(x=>x.Code is "EXPORTED" or "ALREADY_EXISTS"))
   {if(rows.Any(x=>x.Code=="EXPORT_DESTINATION_CONFLICT"))code="EXPORT_WARNINGS";else throw new ExportException("EXPORT_NO_VALID_FILES");}
   else code=rows.Any(x=>x.Code is not ("EXPORTED" or "ALREADY_EXISTS"))?"EXPORT_WARNINGS":"EXPORT_COMPLETED";
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;code="EXPORT_CANCELLED";}
  catch(ExportException e){failed=true;code=e.Code;}
  catch{failed=true;code="EXPORT_FAILED";}
  finally
  {
   try
   {
    if(plan is not null)
    {var attempted=rows.Select(x=>x.Path).ToHashSet(PathComparer);foreach(var item in plan.Where(x=>!attempted.Contains(x.Destination)))rows.Add(new(item.Destination,item.Key,item.Model,item.Environment,item.Event?"Evento":"Documento",item.Hash,0,"NOT_PROCESSED"){Details=item.Details});}
    // Remove only directories created by this operation, only when still empty.
    foreach(string directory in createdDirectories.OrderByDescending(x=>x.Length))
    {try{CheckPath(directory);if(Directory.Exists(directory)&&!Directory.EnumerateFileSystemEntries(directory).Any())Directory.Delete(directory,false);}catch(IOException){}catch(UnauthorizedAccessException){}catch(ExportException){}}
    if(saved)
    {
     job.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:code=="EXPORT_WARNINGS"?JobStatus.CompletedWithWarnings:JobStatus.Completed;job.CompletedAtUtc=Now;job.LastErrorCode=code;
     Audit(cancelled?AuditAction.ExportCancelled:failed?AuditAction.ExportFailed:AuditAction.ExportCompleted,job.Id,code);
     try
     {
      await db.SaveChangesAsync(CancellationToken.None);
      if(rows.Any(x=>x.Code is "EXPORTED" or "ALREADY_EXISTS")&&root is not null)
      {string history=Path.Combine(location.DataDirectory,"ExportHistory");Directory.CreateDirectory(history);await File.WriteAllTextAsync(Path.Combine(history,Now.ToString("yyyyMMddHHmmss")+"_"+job.Id.ToString("N")+".json"),JsonSerializer.Serialize(new ExportHistoryEntry(job.Id,Now,root,written,skipped,rows.Select(x=>x.Details.Folder).Distinct().Count())),CancellationToken.None);}
     }
     catch{if(written>0)code="EXPORT_PUBLISHED_AUDIT_PENDING";}
    }
   }
   finally{exportGate.Semaphore.Release();}
  }
  return new(job.Id,rows.Any(x=>x.Code is "EXPORTED" or "ALREADY_EXISTS" or "EXPORT_DESTINATION_CONFLICT")?root:null,written,skipped,cancelled,failed,code,rows.AsReadOnly());
  void EnsureDirectories(string parent)
  {
   var missing=new Stack<string>();string? current=parent;
   while(current is not null&&!Directory.Exists(current)){missing.Push(current);current=Path.GetDirectoryName(current);}
   while(missing.TryPop(out var directory)){CheckPath(directory);Directory.CreateDirectory(directory);createdDirectories.Add(directory);CheckPath(directory);}
  }
 }
 private static async Task<bool> Matches(string path,string hash,long bytes,CancellationToken ct)
 {
  CheckPath(path);await using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  if(stream.Length!=bytes)return false;return Convert.ToHexString(await SHA256.HashDataAsync(stream,ct))==hash;
 }
}
