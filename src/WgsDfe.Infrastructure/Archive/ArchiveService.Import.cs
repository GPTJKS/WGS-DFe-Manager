using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Parsing;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Archive;
public sealed partial class ArchiveService(WgsDbContext db,IDfeParser parser,IArchiveFileStore files,ArchiveGate gate):IArchiveService
{
 private const string SourceCode="LOCAL_ARCHIVE";
 public async Task<ArchiveImportResult> ImportAsync(ArchiveImportRequest request,IProgress<ArchiveProgress>? progress=null,CancellationToken ct=default)
 {
  if(request.Sources.Count is <1 or >500)throw new ArchiveException(ArchiveError.InvalidQuery);
  await gate.Semaphore.WaitAsync(ct);
  Guid correlation=Guid.NewGuid();var rows=new List<ArchiveImportRow>();Guid jobId=Guid.Empty;bool cancelled=false,failed=false;long readBytes=0;
  try
  {
   var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Import,Status=JobStatus.Running,StartedAtUtc=DateTime.UtcNow};jobId=job.Id;
   db.ProcessingJobs.Add(job);Audit(AuditAction.ArchiveImportStarted,job.Id,correlation,"ARCHIVE_STARTED");await db.SaveChangesAsync(ct);
   foreach(var input in ArchiveSourceReader.Enumerate(request.Sources,files,ct))
   {
    ct.ThrowIfCancellationRequested();int number=rows.Count+1;ArchiveImportRow row;
    if(input.Read is null)row=new(number,input.Label,input.Outcome!.Value,null,input.Code);
    else
    {
     byte[]? bytes=null;DfeParseResult? parsed=null;ArchiveOutcome? invalid=null;string code="";
     try
     {
      bytes=await input.Read(ct);readBytes=checked(readBytes+bytes.Length);
      if(readBytes>1024L*1024*1024){invalid=ArchiveOutcome.LimitExceeded;code="BATCH_BYTES_LIMIT";}
      else parsed=parser.Parse(bytes,ct);
     }
     catch(DfeParseException e){invalid=ArchiveOutcome.Invalid;code="XML_"+e.Code;}
     catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException||ArchiveSourceReader.IsSizeError(e))
     {invalid=ArchiveSourceReader.IsSizeError(e)?ArchiveOutcome.LimitExceeded:ArchiveOutcome.Invalid;code=ArchiveSourceReader.IsSizeError(e)?"XML_SIZE_LIMIT":"SOURCE_READ_FAILED";}
     if(invalid.HasValue)row=new(number,input.Label,invalid.Value,null,code);
     else if(parsed!.Kind is DfePayloadKind.DocumentSummary or DfePayloadKind.EventSummary)row=new(number,input.Label,ArchiveOutcome.SummarySkipped,parsed.Key.Value,"SUMMARY_NOT_FULL_XML");
     else
     {
      try
      {
       // Publicação imutável antes da transação. Crash pode deixar órfão, nunca apagar XML confirmado.
       var indexed=await db.ArchiveFiles.AsNoTracking().SingleOrDefaultAsync(x=>x.Hash==parsed.Sha256,ct);
       string relative=indexed?.RelativePath??files.GetRelativePath(parsed);bool newPhysical=await files.PutAsync(relative,parsed.Sha256,bytes!,ct);
       await using var transaction=await db.Database.BeginTransactionAsync(ct);
       ArchiveOutcome outcome;
       if(indexed is not null)
       {
        if(indexed.FiscalDocumentId.HasValue){var document=await db.FiscalDocuments.SingleAsync(x=>x.Id==indexed.FiscalDocumentId,ct);await LinkCompanies(document,ct);}
        outcome=indexed.IsConflict?ArchiveOutcome.Conflict:newPhysical?ArchiveOutcome.Restored:ArchiveOutcome.Duplicate;
       }
       else outcome=await StoreMetadata(parsed,relative,bytes!.Length,ct);
       row=new(number,input.Label,outcome,parsed.Key.Value,outcome.ToString().ToUpperInvariant());
       await Checkpoint(jobId,row,correlation,ct);await transaction.CommitAsync(ct);
       rows.Add(row);progress?.Report(new(rows.Count,rows.Count(x=>x.Outcome==ArchiveOutcome.Imported),input.Label));continue;
      }
      catch(ArchiveException e){db.ChangeTracker.Clear();row=new(number,input.Label,ArchiveOutcome.StorageFailed,parsed.Key.Value,"STORAGE_"+e.Code);failed=true;}
      catch(DbUpdateException){db.ChangeTracker.Clear();row=new(number,input.Label,ArchiveOutcome.StorageFailed,parsed.Key.Value,"DATABASE_WRITE_FAILED");failed=true;}
     }
    }
    await Checkpoint(jobId,row,correlation,ct);rows.Add(row);progress?.Report(new(rows.Count,rows.Count(x=>x.Outcome==ArchiveOutcome.Imported),input.Label));
    if(failed||row.Code=="BATCH_BYTES_LIMIT"||row.Code=="SOURCE_COUNT_LIMIT")break;
   }
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;db.ChangeTracker.Clear();}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or DbUpdateException or System.Data.Common.DbException)
  {failed=true;db.ChangeTracker.Clear();}
  catch{failed=true;db.ChangeTracker.Clear();throw;}
  finally
  {
   try
   {
    if(jobId!=Guid.Empty)
    {
     db.ChangeTracker.Clear();var job=await db.ProcessingJobs.SingleOrDefaultAsync(x=>x.Id==jobId,CancellationToken.None);
     if(job is not null)
     {
      job.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:rows.Any(x=>x.Outcome is ArchiveOutcome.Conflict or ArchiveOutcome.Invalid or ArchiveOutcome.UnsafeArchive or ArchiveOutcome.LimitExceeded or ArchiveOutcome.SourceUnavailable)?JobStatus.CompletedWithWarnings:JobStatus.Completed;
      job.CompletedAtUtc=DateTime.UtcNow;job.LastErrorCode=failed?"ARCHIVE_FAILED":cancelled?"ARCHIVE_CANCELLED":null;
      Audit(failed?AuditAction.ArchiveImportFailed:cancelled?AuditAction.ArchiveImportCancelled:AuditAction.ArchiveImportCompleted,jobId,correlation,failed?"ARCHIVE_FAILED":cancelled?"ARCHIVE_CANCELLED":"ARCHIVE_COMPLETED");await db.SaveChangesAsync(CancellationToken.None);
     }
    }
   }
   finally{gate.Semaphore.Release();}
  }
  return new(jobId,rows.AsReadOnly(),cancelled,failed);
 }
 // Caller holds ArchiveGate and owns the SQL transaction; physical orphans are safe on rollback.
 internal async Task<ArchiveOutcome> StoreProviderPayloadAsync(DfeParseResult parsed,byte[] bytes,string source,CancellationToken ct)
 {
  var indexed=await db.ArchiveFiles.SingleOrDefaultAsync(x=>x.Hash==parsed.Sha256,ct);
  string relative=indexed?.RelativePath??files.GetRelativePath(parsed);
  await files.PutAsync(relative,parsed.Sha256,bytes,ct);
  if(indexed is not null)
  {if(indexed.FiscalDocumentId.HasValue)await LinkCompanies(await db.FiscalDocuments.SingleAsync(x=>x.Id==indexed.FiscalDocumentId,ct),ct);return indexed.IsConflict?ArchiveOutcome.Conflict:ArchiveOutcome.Duplicate;}
  return await StoreMetadata(parsed,relative,bytes.Length,ct,source);
 }
 private async Task Checkpoint(Guid jobId,ArchiveImportRow row,Guid correlation,CancellationToken ct)
 {
  var job=await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId,ct);job.ProcessedItems++;job.TotalItems=job.ProcessedItems;
  bool available=row.Outcome is ArchiveOutcome.Imported or ArchiveOutcome.Upgraded or ArchiveOutcome.Duplicate or ArchiveOutcome.VariantStored or ArchiveOutcome.Restored;
  db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=jobId,ItemReference=row.Number.ToString(CultureInfo.InvariantCulture),ProviderCode=SourceCode,ErrorCode=row.Code,
   Status=available?JobStatus.Completed:JobStatus.CompletedWithWarnings,Availability=available?Availability.Available:Availability.Pending});
  if(available||row.Outcome==ArchiveOutcome.Conflict)Audit(row.Outcome==ArchiveOutcome.Imported?AuditAction.XmlImported:AuditAction.ArchiveVariantStored,jobId,correlation,row.Code);
  await db.SaveChangesAsync(ct);
 }
 private async Task<ArchiveOutcome> StoreMetadata(DfeParseResult parsed,string relative,int length,CancellationToken ct,string source=SourceCode)
 {
  if(parsed.Environment is null)throw new ArchiveException(ArchiveError.InvalidQuery);
  var key=parsed.Key;bool conflict=false,upgraded=false,created=false,restored=false;Guid? documentId=null,eventId=null;
  if(parsed.Kind==DfePayloadKind.FullDocument)
  {
   var document=await db.FiscalDocuments.SingleOrDefaultAsync(x=>x.AccessKey==key.Value&&x.Environment==parsed.Environment,ct);
   if(document is null)
   {
    document=new(){OrganizationId=db.OrganizationId,AccessKey=key.Value,Environment=parsed.Environment.Value,Model=key.Model};db.FiscalDocuments.Add(document);FillDocument(document,parsed,relative,source);created=true;
   }
   else
   {
    if(document.XmlHash==parsed.Sha256){document.FileRelativePath=relative;restored=true;}
    else
    {
    bool core=document.IssuerTaxId==parsed.IssuerTaxId&&document.RecipientTaxId==parsed.RecipientTaxId&&document.IssueDateUtc==parsed.IssueDate?.UtcDateTime&&document.TotalValue==parsed.Total;
    conflict=!core||(document.ProtocolNumber is not null&&parsed.ProtocolNumber is not null&&(document.ProtocolNumber!=parsed.ProtocolNumber||document.DeclaredStatus!=parsed.Status));
    if(!conflict&&document.ProtocolNumber is null&&parsed.ProtocolNumber is not null&&parsed.Status==DocumentStatus.Authorized){FillDocument(document,parsed,relative,source);upgraded=true;}
    }
   }
   documentId=document.Id;
   if(!conflict)
   {
    await LinkCompanies(document,ct);
    var pending=await db.FiscalDocumentEvents.Where(e=>e.AccessKey==key.Value&&e.Environment==parsed.Environment).ToListAsync(ct);
    foreach(var item in pending)item.FiscalDocumentId=document.Id;
    ApplyCancellation(document,pending);
   }
  }
  else
  {
   var ev=await db.FiscalDocumentEvents.SingleOrDefaultAsync(x=>x.AccessKey==key.Value&&x.Environment==parsed.Environment&&x.EventTypeCode==parsed.EventType&&x.SequenceNumber==parsed.EventSequence,ct);
   if(ev is null)
   {ev=new(){OrganizationId=db.OrganizationId,AccessKey=key.Value,Environment=parsed.Environment.Value,Model=key.Model,EventTypeCode=parsed.EventType!,SequenceNumber=parsed.EventSequence!.Value};db.FiscalDocumentEvents.Add(ev);FillEvent(ev,parsed,relative,source);created=true;}
   else
   {
    if(ev.XmlHash==parsed.Sha256){ev.FileRelativePath=relative;restored=true;}
    else
    {
    conflict=ev.EventDateUtc!=parsed.EventDate?.UtcDateTime||(ev.ProtocolNumber is not null&&parsed.ProtocolNumber is not null&&(ev.ProtocolNumber!=parsed.ProtocolNumber||ev.ResponseCode!=parsed.ResponseCode));
    if(!conflict&&ev.ResponseCode is null&&parsed.ResponseCode.HasValue){FillEvent(ev,parsed,relative,source);upgraded=true;}
    }
   }
   eventId=ev.Id;
   if(!conflict)
   {
    var document=await db.FiscalDocuments.SingleOrDefaultAsync(x=>x.AccessKey==key.Value&&x.Environment==parsed.Environment,ct);
    if(document is not null)
    {
     ev.FiscalDocumentId=document.Id;
     var events=await db.FiscalDocumentEvents.Where(x=>x.AccessKey==key.Value&&x.Environment==parsed.Environment).ToListAsync(ct);
     if(!events.Any(x=>x.Id==ev.Id))events.Add(ev);ApplyCancellation(document,events);
    }
   }
  }
  db.ArchiveFiles.Add(new(){OrganizationId=db.OrganizationId,FiscalDocumentId=documentId,FiscalDocumentEventId=eventId,Hash=parsed.Sha256,RelativePath=relative,ByteLength=length,IsConflict=conflict,ResponseCode=parsed.ResponseCode});
  return conflict?ArchiveOutcome.Conflict:restored?ArchiveOutcome.Restored:created?ArchiveOutcome.Imported:upgraded?ArchiveOutcome.Upgraded:ArchiveOutcome.VariantStored;
 }
 private static void FillDocument(FiscalDocument document,DfeParseResult p,string relative,string source)
 {
  document.IssuerTaxId=p.IssuerTaxId;document.RecipientTaxId=p.RecipientTaxId;document.IssuerName=p.IssuerName;document.RecipientName=p.RecipientName;
  document.IssueDateUtc=p.IssueDate?.UtcDateTime;document.IssueDateOriginal=p.IssueDate?.ToString("O",CultureInfo.InvariantCulture);document.IssueLocalDate=p.IssueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);
  document.ReferenceYear=p.Key.Year;document.ReferenceMonth=p.Key.Month;document.Series=p.Key.Series;document.DocumentNumber=p.Key.Number;document.TotalValue=p.Total;document.TotalCents=p.Total.HasValue?checked((long)(p.Total.Value*100m)):null;
  document.DeclaredStatus=document.Status=p.Status;document.ProtocolNumber=p.ProtocolNumber;document.ResponseCode=p.ResponseCode;document.AuthorizationDateUtc=p.Status==DocumentStatus.Authorized?p.ReceiptDate?.UtcDateTime:null;
  document.FileRelativePath=relative;document.XmlHash=p.Sha256;document.SourceProviderCode=source;document.RetrievedAtUtc=DateTime.UtcNow;
  document.SearchText=SearchNormalizer.Normalize($"{document.AccessKey} {p.IssuerTaxId} {p.RecipientTaxId} {p.IssuerName} {p.RecipientName}");
 }
 private static void FillEvent(FiscalDocumentEvent ev,DfeParseResult p,string relative,string source)
 {
  ev.EventDateUtc=p.EventDate?.UtcDateTime;ev.EventDateOriginal=p.EventDate?.ToString("O",CultureInfo.InvariantCulture);ev.ProtocolNumber=p.ProtocolNumber;ev.ResponseCode=p.ResponseCode;ev.ReceiptDateUtc=p.ReceiptDate?.UtcDateTime;ev.FileRelativePath=relative;ev.XmlHash=p.Sha256;ev.SourceProviderCode=source;
 }
 private async Task<int> LinkCompanies(FiscalDocument document,CancellationToken ct)
 {
  var companies=await db.Companies.Where(c=>c.Cnpj==document.IssuerTaxId||c.Cnpj==document.RecipientTaxId).ToListAsync(ct);
  var links=await db.FiscalDocumentCompanies.Where(x=>x.FiscalDocumentId==document.Id).ToListAsync(ct);int added=0;
  foreach(var company in companies)
  {
   var role=company.Cnpj==document.IssuerTaxId?(company.Cnpj==document.RecipientTaxId?ActorFilter.Both:ActorFilter.Issuer):ActorFilter.Recipient;
   var link=links.FirstOrDefault(x=>x.CompanyId==company.Id);
   if(link is null){db.FiscalDocumentCompanies.Add(new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,FiscalDocumentId=document.Id,Role=role});added++;}else link.Role=role;
  }
  return added;
 }
 private static void ApplyCancellation(FiscalDocument document,IEnumerable<FiscalDocumentEvent> events)
 {
  var cancellation=events.Where(e=>(e.EventTypeCode=="110111"||(e.EventTypeCode=="110112"&&document.Model==DocumentModel.NFCe))&&e.ResponseCode is 135 or 155&&e.ProtocolNumber is not null&&e.EventDateUtc.HasValue&&e.ReceiptDateUtc>=e.EventDateUtc&&e.EventDateUtc>=document.IssueDateUtc).OrderBy(e=>e.EventDateUtc).FirstOrDefault();
  document.Status=cancellation is null?document.DeclaredStatus:DocumentStatus.Cancelled;
  if(cancellation is not null)document.CancellationDateUtc=cancellation.EventDateUtc;else if(document.DeclaredStatus!=DocumentStatus.Cancelled)document.CancellationDateUtc=null;
 }
 private void Audit(AuditAction action,Guid id,Guid correlation,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode=code});
}
