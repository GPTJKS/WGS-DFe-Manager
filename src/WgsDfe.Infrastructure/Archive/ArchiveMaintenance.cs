using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Archive;
public static class ArchiveMaintenance
{
 public static async Task InitializeAsync(WgsDbContext db,CancellationToken ct=default)
 {
  // Retomada não apaga arquivos e não repete importação automaticamente.
  var interrupted=await db.ProcessingJobs.Where(j=>j.Status==JobStatus.Running&&db.AuditLogs.Any(a=>a.EntityId==j.Id&&a.Action==AuditAction.ArchiveImportStarted)).ToListAsync(ct);
  foreach(var job in interrupted)
  {
   job.Status=JobStatus.Failed;job.LastErrorCode="ARCHIVE_INTERRUPTED";job.CompletedAtUtc=DateTime.UtcNow;
   db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=AuditAction.ArchiveImportFailed,EntityId=job.Id,CorrelationId=Guid.NewGuid(),ResultCode="ARCHIVE_INTERRUPTED"});
  }
  await db.SaveChangesAsync(ct);
  while(true)
  {
   var batch=await db.FiscalDocuments.Where(d=>d.SearchText=="").Take(500).ToListAsync(ct);if(batch.Count==0)break;
   foreach(var d in batch)
   {
    d.SearchText=SearchNormalizer.Normalize($"{d.AccessKey} {d.IssuerTaxId} {d.RecipientTaxId} {d.IssuerName} {d.RecipientName}");
    // Índice legado em C#: decimal exato, sem CAST SQLite para ponto flutuante.
    if(d.TotalValue is { } total&&total>=0)
    {try{decimal cents=total*100m;if(decimal.Truncate(cents)==cents)d.TotalCents=checked((long)cents);}catch(OverflowException){d.TotalCents=null;}}
    d.IssueLocalDate??=d.IssueDateUtc?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
   }
   await db.SaveChangesAsync(ct);db.ChangeTracker.Clear();
  }
 }
}
