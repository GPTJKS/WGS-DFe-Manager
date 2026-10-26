using Microsoft.EntityFrameworkCore;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Export;
namespace WgsDfe.Infrastructure.Archive;
public sealed record RetentionResult(int Examined,int Removed,long Bytes,int Protected);
public sealed class ArchiveRetention(WgsDbContext db,ArchiveLocation location,ArchiveGate gate,ExportGate exportGate)
{
 public async Task<RetentionResult> CleanAsync(int days,CancellationToken ct=default)
 {
  if(days is not (30 or 60 or 90))throw new ArgumentOutOfRangeException(nameof(days));
  await exportGate.Semaphore.WaitAsync(ct);
  try
  {
   await gate.Semaphore.WaitAsync(ct);
   try
   {
    int examined=0,removed=0,protectedCount=0;long bytes=0;var cutoff=DateTime.UtcNow.AddDays(-days);var receipts=new ExportReceiptStore(location);
    // Preserve cancellation/event evidence, unknown status and all divergent or alternative versions.
    var query=db.FiscalDocuments.AsNoTracking().Where(d=>d.Status==DocumentStatus.Authorized&&!db.FiscalDocumentEvents.Any(e=>e.FiscalDocumentId==d.Id)&&!db.ArchiveFiles.Any(f=>f.FiscalDocumentId==d.Id&&f.Hash!=d.XmlHash));
    await foreach(var doc in query.AsAsyncEnumerable().WithCancellation(ct))
    {
     examined++;var receipt=await receipts.FindAsync(doc.FileRelativePath,doc.XmlHash,ct);
     if(receipt is null||receipt.PublishedAtUtc>cutoff){protectedCount++;continue;}
     var path=Path.GetFullPath(Path.Combine(location.DataDirectory,doc.FileRelativePath));
     var root=Path.GetFullPath(Path.Combine(location.DataDirectory,"XML",location.OrganizationId.ToString("N")))+Path.DirectorySeparatorChar;
     if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path)){protectedCount++;continue;}
     try
     {
      ExportReceiptStore.CheckLinks(path);
      // On Windows, keep the verified external package open against replacement/deletion.
      var externalPath=receipt.Format==WgsDfe.Application.Export.ExportFormat.Zip?receipt.Output:Path.Combine(receipt.Output,receipt.Entry.Replace('/',Path.DirectorySeparatorChar));
      using var guard=new FileStream(externalPath,FileMode.Open,FileAccess.Read,FileShare.Read);
      var external=await receipts.ReadAsync(receipt,ct);
      using var local=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
      if(Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(local,ct))!=doc.XmlHash){protectedCount++;continue;}
      ct.ThrowIfCancellationRequested();File.Delete(path);removed++;bytes+=external.LongLength;db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=AuditAction.ArchiveLocalCopyRemoved,EntityId=doc.Id,CorrelationId=Guid.NewGuid(),ResultCode="LOCAL_COPY_REMOVED_EXTERNAL_VERIFIED"});
     }
     catch(WgsDfe.Application.Archive.ArchiveException){protectedCount++;}
     catch(Exception e)when(e is IOException or UnauthorizedAccessException){protectedCount++;}
    }
    await db.SaveChangesAsync(CancellationToken.None);return new(examined,removed,bytes,protectedCount);
   }
   finally{try{if(db.ChangeTracker.HasChanges())await db.SaveChangesAsync(CancellationToken.None);}finally{gate.Semaphore.Release();}}
  }
  finally{exportGate.Semaphore.Release();}
 }
}
