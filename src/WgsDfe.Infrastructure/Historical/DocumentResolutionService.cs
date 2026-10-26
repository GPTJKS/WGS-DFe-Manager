using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Historical;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Parsing;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Cte;
namespace WgsDfe.Infrastructure.Historical;
public sealed partial class HistoricalRecoveryService
{
 public async Task RunAsync(Guid batchId,HistoricalOptions options,IProgress<HistoricalRecoveryProgress>? progress=null,CancellationToken ct=default)
 {
  if(options.MaximumPaidCalls is <0 or >100000||options.Provider?.Length>64)throw new HistoricalException("INVALID_OPTIONS");
  var registry=Registry();await gate.Semaphore.WaitAsync(ct);bool cancelled=false,failed=false;
  Guid? jobId=null;
  try
  {
   var batch=await db.HistoricalRecoveryBatches.SingleOrDefaultAsync(x=>x.Id==batchId,ct)??throw new HistoricalException("NOT_FOUND_BATCH");jobId=batch.JobId;
   var job=await db.ProcessingJobs.SingleAsync(x=>x.Id==batch.JobId,ct);job.Status=JobStatus.Running;job.StartedAtUtc??=Now;job.CompletedAtUtc=null;await db.SaveChangesAsync(ct);
   var ids=await db.HistoricalRecoveryItems.AsNoTracking().Where(x=>x.BatchId==batchId&&x.State!=HistoricalItemState.Invalid&&x.State!=HistoricalItemState.Duplicate).OrderBy(x=>x.SourceLine).Select(x=>x.Id).ToListAsync(ct);
   int n=0;
   foreach(var id in ids)
   {
    ct.ThrowIfCancellationRequested();db.ChangeTracker.Clear();
    var item=await db.HistoricalRecoveryItems.SingleAsync(x=>x.Id==id,ct);var environment=batch.Environment;
    try{await Resolve(item,environment,options,registry,ct);}
    catch(HistoricalException e){db.ChangeTracker.Clear();item=await db.HistoricalRecoveryItems.SingleAsync(x=>x.Id==id,CancellationToken.None);item.ResultCode=HistoricalMessages.SafeCode(e.Code);if(e.Code is "LOCAL_INTEGRITY_ERROR" or "PAYLOAD_INVALID" or "PAYLOAD_HASH_MISMATCH" or "PAYLOAD_CONFLICT" or "COMPANY_XML_MISMATCH"){item.Availability=Availability.TemporaryError;if(item.State==HistoricalItemState.Available)item.State=item.ResponseBytes is null?HistoricalItemState.Pending:HistoricalItemState.PayloadReceived;}await db.SaveChangesAsync(CancellationToken.None);}
    catch(ArchiveException){db.ChangeTracker.Clear();item=await db.HistoricalRecoveryItems.SingleAsync(x=>x.Id==id,CancellationToken.None);item.ResultCode="LOCAL_INTEGRITY_ERROR";item.Availability=Availability.TemporaryError;await db.SaveChangesAsync(CancellationToken.None);}
    var checkpoint=await db.HistoricalRecoveryItems.AsNoTracking().Where(x=>x.Id==id).Select(x=>new{x.SourceLine,x.ResultCode}).SingleAsync(ct);
    progress?.Report(new(++n,ids.Count,checkpoint.SourceLine,checkpoint.ResultCode));
   }
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;}
  catch{failed=true;throw;}
  finally
  {
   try
   {
    db.ChangeTracker.Clear();
    if(jobId.HasValue)
    {
     var job=await db.ProcessingJobs.SingleAsync(x=>x.Id==jobId,CancellationToken.None);
     int unresolved=await db.HistoricalRecoveryItems.CountAsync(x=>x.BatchId==batchId&&x.State!=HistoricalItemState.Available&&x.State!=HistoricalItemState.Duplicate,CancellationToken.None);
     job.ProcessedItems=await db.HistoricalRecoveryItems.CountAsync(x=>x.BatchId==batchId&&(x.State==HistoricalItemState.Available||x.State==HistoricalItemState.Invalid||x.State==HistoricalItemState.Duplicate||x.State==HistoricalItemState.NotFound),CancellationToken.None);
     job.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:unresolved>0?JobStatus.CompletedWithWarnings:JobStatus.Completed;job.CompletedAtUtc=Now;job.LastErrorCode=failed?"HISTORICAL_FAILED":cancelled?"HISTORICAL_CANCELLED":null;
     Audit(AuditAction.HistoricalRecoveryCompleted,batchId,failed?"HISTORICAL_FAILED":cancelled?"HISTORICAL_CANCELLED":"HISTORICAL_FINISHED");await db.SaveChangesAsync(CancellationToken.None);
    }
   }
   finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
  }
 }
 private async Task Resolve(HistoricalRecoveryItem item,FiscalEnvironment environment,HistoricalOptions options,Dictionary<string,HistoricalProviderRegistration> registry,CancellationToken ct)
 {
  var cached=await archive.CheckAvailabilityAsync(item.AccessKey!,environment,ct);
  if(cached.Availability==Availability.Available)
  {
   bool related=!item.CompanyId.HasValue||await db.FiscalDocuments.AnyAsync(x=>x.AccessKey==item.AccessKey&&x.Environment==environment&&
    (db.Companies.Any(c=>c.Id==item.CompanyId&&(c.Cnpj==x.IssuerTaxId||c.Cnpj==x.RecipientTaxId))||db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==x.Id&&l.CompanyId==item.CompanyId)),ct);
   if(related){item.State=HistoricalItemState.Available;item.Availability=Availability.Available;item.ResultCode="LOCAL_AVAILABLE";item.NextAllowedAtUtc=null;await db.SaveChangesAsync(ct);return;}
   throw new HistoricalException("COMPANY_XML_MISMATCH");
  }
  if(item.State==HistoricalItemState.Available){item.State=HistoricalItemState.Pending;item.Availability=Availability.Pending;await db.SaveChangesAsync(ct);}
  if(cached.Availability==Availability.TemporaryError)throw new HistoricalException("LOCAL_INTEGRITY_ERROR");
  if(item.ResponseBytes is not null){await ApplyResponse(item,environment,ct);return;}
  if(item.State==HistoricalItemState.NotFound||item.State==HistoricalItemState.NeedsReview)return;
  if(item.CompanyId is null)throw new HistoricalException("COMPANY_REQUIRED");
  var company=await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==item.CompanyId,ct);
  var flag=item.Model switch{DocumentModel.NFe=>EnabledDocuments.NFe,DocumentModel.NFCe=>EnabledDocuments.NFCe,DocumentModel.CTe=>EnabledDocuments.CTe,DocumentModel.CTeOS=>EnabledDocuments.CTeOS,_=>EnabledDocuments.NFCom};
  if(!company.IsActive||(company.EnabledDocuments&flag)==0)throw new HistoricalException("COMPANY_DISABLED");
  if(item.NextAllowedAtUtc>Now){item.ResultCode="WAIT_UNTIL_ALLOWED";await db.SaveChangesAsync(ct);return;}
  // Existing external jobs are always resumed with the same provider and stable item id.
  if(item.ExternalJobId is null&&item.State!=HistoricalItemState.Submitting&&options.UseOfficial)
  {
   string? code=item.Model switch
   {DocumentModel.NFe=>company.State=="SC"&&environment==FiscalEnvironment.Production?NfeSources.SantaCatarina:NfeSources.National,
    DocumentModel.NFCe when company.State=="SC"&&environment==FiscalEnvironment.Production=>NfeSources.SantaCatarinaNfce,
    DocumentModel.NFCom when company.State=="SC"&&environment==FiscalEnvironment.Production=>NfeSources.SantaCatarinaNfcom,_=>null};
   if(code is not null)
   {
    if(!network.EnableExternalRequests)throw new HistoricalException("NETWORK_DISABLED");
    var result=await official.SynchronizeAsync(new([company.Id],code,environment,AccessKey:item.AccessKey,MaxPages:1),ct:ct);ct.ThrowIfCancellationRequested();
    // The official coordinator can clear the shared context during recovery; reload our checkpoint.
    item=await db.HistoricalRecoveryItems.SingleAsync(x=>x.Id==item.Id,ct);
    var available=await archive.CheckAvailabilityAsync(item.AccessKey!,environment,ct);
    if(available.Availability==Availability.Available){item.State=HistoricalItemState.Available;item.Availability=Availability.Available;item.ProviderCode=code;item.ResultCode="RECOVERED";await db.SaveChangesAsync(ct);return;}
    var row=result.Rows.SingleOrDefault();
    if(row is null||row.Code!="NFE_SYNCHRONIZED"&&row.Code is not ("NFE_OUTSIDE_SOURCE_WINDOW" or "NFE_NOT_FOUND_OR_OUTSIDE_WINDOW"))
    {item.ResultCode="OFFICIAL_DEFERRED";item.NextAllowedAtUtc=row?.NextAllowedAtUtc;await db.SaveChangesAsync(ct);return;}
    item.ResultCode="OFFICIAL_NOT_AVAILABLE";item.ProviderCode=code;await db.SaveChangesAsync(ct);
   }
   else{item.ResultCode="NO_OFFICIAL_KEY_PROVIDER";await db.SaveChangesAsync(ct);}
  }
  if(!options.UseHistorical)throw new HistoricalException(options.UseOfficial?item.ResultCode:"HISTORICAL_DISABLED");
  if(!network.EnableExternalRequests)throw new HistoricalException("NETWORK_DISABLED");
  if(options.Provider is null||!registry.TryGetValue(options.Provider,out var registration)||!registration.Provider.Capabilities.Models.Contains(item.Model!.Value))throw new HistoricalException("NO_HISTORICAL_PROVIDER");
  if(item.ExternalJobId is not null||item.State==HistoricalItemState.Submitting)
   if(item.ProviderCode!=options.Provider)throw new HistoricalException("PROVIDER_CHANGE_REQUIRES_REVIEW");
  if(item.State==HistoricalItemState.Submitting&&!registration.IdempotentSubmission)
  {item.State=HistoricalItemState.NeedsReview;item.ResultCode="SUBMISSION_REVIEW_REQUIRED";await db.SaveChangesAsync(ct);return;}
  var provider=registration.Provider;
  try
  {
   if(item.ExternalJobId is null)
   {
    item.ProviderCode=options.Provider;item.State=HistoricalItemState.Submitting;item.ResultCode="EXTERNAL_SUBMITTING";
    await Reserve(item,registration,options,ct);
    var submitted=await provider.SubmitAsync(new(db.OrganizationId,company.Id,item.Model!.Value,environment,[item.AccessKey!],item.Id),ct);
    if(string.IsNullOrWhiteSpace(submitted.ExternalJobId)||submitted.ExternalJobId.Length>200||submitted.ExternalJobId.Any(char.IsControl))throw new HistoricalException("SUBMISSION_REVIEW_REQUIRED");
    item.ExternalJobId=submitted.ExternalJobId;item.State=HistoricalItemState.Waiting;item.ResultCode="EXTERNAL_WAITING";await db.SaveChangesAsync(CancellationToken.None);ct.ThrowIfCancellationRequested();return;
   }
   if(item.State!=HistoricalItemState.Downloading)
   {
    await Reserve(item,registration,options,ct);
    var status=await provider.GetStatusAsync(item.ExternalJobId,ct);
    if(status.ExternalJobId!=item.ExternalJobId)throw new HistoricalException("PAYLOAD_INVALID");
    if(status.Status==JobStatus.Completed){item.State=HistoricalItemState.Downloading;await db.SaveChangesAsync(CancellationToken.None);}
    else if(status.Status is JobStatus.Failed or JobStatus.Cancelled or JobStatus.CompletedWithWarnings)
    {item.State=HistoricalItemState.NotFound;item.Availability=Availability.NotRecovered;item.ResultCode="EXTERNAL_JOB_FAILED";await db.SaveChangesAsync(CancellationToken.None);return;}
    else{item.State=HistoricalItemState.Waiting;item.ResultCode="EXTERNAL_WAITING";await db.SaveChangesAsync(CancellationToken.None);return;}
   }
   ct.ThrowIfCancellationRequested();await Reserve(item,registration,options,ct);
   var response=await provider.DownloadAsync(item.ExternalJobId,ct);
   if(response.Documents.Count==0)
   {
    if(response.Availability is Availability.NotFoundInSource or Availability.OutsideWindow or Availability.NotRecovered)
    {item.State=HistoricalItemState.NotFound;item.Availability=response.Availability;item.ResultCode="NOT_FOUND";await db.SaveChangesAsync(CancellationToken.None);return;}
    throw new HistoricalException("EXTERNAL_ERROR");
   }
   if(response.Documents.Count>50||response.Documents.Any(x=>x.Xml is null||x.Xml.Length is <1 or >10*1024*1024||x.AccessKey?.Length>100||x.SchemaName.Length>200||x.Nsu?.Length>100)||response.Documents.Sum(x=>(long)x.Xml.Length)>32*1024*1024)throw new HistoricalException("PAYLOAD_INVALID");
   item.ResponseBytes=JsonSerializer.SerializeToUtf8Bytes(response.Documents);item.ResponseHash=Convert.ToHexString(SHA256.HashData(item.ResponseBytes));item.State=HistoricalItemState.PayloadReceived;item.ResultCode="PAYLOAD_RECEIVED";await db.SaveChangesAsync(CancellationToken.None);ct.ThrowIfCancellationRequested();
   await ApplyResponse(item,environment,ct);
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
  catch(HistoricalException){throw;}
  catch(DbUpdateException){throw;}
  catch(ArchiveException){throw;}
  catch{item.ResultCode="EXTERNAL_ERROR";await db.SaveChangesAsync(CancellationToken.None);}
 }
 private async Task Reserve(HistoricalRecoveryItem item,HistoricalProviderRegistration registration,HistoricalOptions options,CancellationToken ct)
 {
  var batch=await db.HistoricalRecoveryBatches.SingleAsync(x=>x.Id==item.BatchId,ct);
  if(registration.IsPaid)
  {
   if(!options.AllowPaid||options.MaximumPaidCalls==0)throw new HistoricalException("PAID_NOT_ALLOWED");
   if(batch.PaidCallsReserved>=options.MaximumPaidCalls)throw new HistoricalException("PAID_LIMIT");
   batch.PaidCallsReserved++;
  }
  item.Attempts++;item.NextAllowedAtUtc=Now.AddSeconds(registration.PollSeconds);
  Audit(AuditAction.HistoricalCallReserved,item.Id,registration.IsPaid?"HISTORICAL_PAID_RESERVED":"HISTORICAL_FREE_RESERVED");await db.SaveChangesAsync(ct);
 }
 private async Task ApplyResponse(HistoricalRecoveryItem item,FiscalEnvironment environment,CancellationToken ct)
 {
  if(item.ResponseBytes is null||item.ResponseBytes.Length>48*1024*1024||item.ResponseHash!=Convert.ToHexString(SHA256.HashData(item.ResponseBytes)))throw new HistoricalException("PAYLOAD_HASH_MISMATCH");
  var parsed=new List<(DfeParseResult Parsed,byte[] Bytes)>();
  try
  {
   var payloads=JsonSerializer.Deserialize<RetrievedPayload[]>(item.ResponseBytes)??throw new HistoricalException("PAYLOAD_INVALID");
   if(payloads.Length is <1 or >50)throw new HistoricalException("PAYLOAD_INVALID");
   var company=item.CompanyId.HasValue?await db.Companies.AsNoTracking().SingleAsync(x=>x.Id==item.CompanyId,ct):null;
   foreach(var payload in payloads)
   {
    var p=parser.Parse(payload.Xml,ct);
    if(p.Key.Value!=item.AccessKey||p.Environment!=environment||p.Key.Model!=item.Model||payload.AccessKey is not null&&payload.AccessKey!=p.Key.Value||p.Kind is not (DfePayloadKind.FullDocument or DfePayloadKind.Event))throw new HistoricalException("PAYLOAD_INVALID");
    if(p.Kind==DfePayloadKind.FullDocument&&company is not null&&p.IssuerTaxId!=company.Cnpj&&p.RecipientTaxId!=company.Cnpj&&!(p.Key.Model is DocumentModel.CTe or DocumentModel.CTeOS&&CteParticipants.Read(payload.Xml).Any(x=>x.TaxId==company.Cnpj)))throw new HistoricalException("PAYLOAD_INVALID");
    parsed.Add((p,payload.Xml));
   }
   if(!parsed.Any(x=>x.Parsed.Kind==DfePayloadKind.FullDocument))throw new HistoricalException("PAYLOAD_INVALID");
  }
  catch(OperationCanceledException){throw;}catch(HistoricalException){throw;}catch{throw new HistoricalException("PAYLOAD_INVALID");}
  await archiveGate.Semaphore.WaitAsync(ct);
  try
  {
   await using var transaction=await db.Database.BeginTransactionAsync(ct);
   foreach(var (p,bytes) in parsed)
   {
    var outcome=await archive.StoreProviderPayloadAsync(p,bytes,item.ProviderCode??"HISTORICAL",ct);
    if(outcome==ArchiveOutcome.Conflict)throw new HistoricalException("PAYLOAD_CONFLICT");await db.SaveChangesAsync(ct);
    if(p.Kind==DfePayloadKind.FullDocument&&p.Key.Model is DocumentModel.CTe or DocumentModel.CTeOS)
    {
     var participants=CteParticipants.Read(bytes);var taxes=participants.Select(x=>x.TaxId).ToArray();var companies=await db.Companies.Where(x=>taxes.Contains(x.Cnpj)).ToListAsync(ct);
     var document=await db.FiscalDocuments.SingleAsync(x=>x.AccessKey==p.Key.Value&&x.Environment==environment,ct);
     foreach(var company in companies)if(!await db.FiscalDocumentCompanies.AnyAsync(x=>x.CompanyId==company.Id&&x.FiscalDocumentId==document.Id,ct))db.FiscalDocumentCompanies.Add(new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,FiscalDocumentId=document.Id,Role=ActorFilter.OtherParticipant,Participation=string.Join(", ",participants.Where(x=>x.TaxId==company.Cnpj).Select(x=>x.Role).Distinct())});
    }
   }
   item.State=HistoricalItemState.Available;item.Availability=Availability.Available;item.ResultCode="RECOVERED";item.NextAllowedAtUtc=null;
   Audit(AuditAction.XmlRecovered,item.Id,"HISTORICAL_XML_RECOVERED");await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
  }
  catch{db.ChangeTracker.Clear();throw;}
  finally{archiveGate.Semaphore.Release();}
 }
 private void Audit(AuditAction action,Guid id,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,EntityId=id,CorrelationId=id,Action=action,ResultCode=code});
}
