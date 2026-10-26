using System.Globalization;
using System.Security.Cryptography;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Cte;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Parsing;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Nfe;
using WgsDfe.Providers.Cte;
namespace WgsDfe.Infrastructure.Cte;
public sealed class CteSyncService(WgsDbContext db,ICteDistributionProvider provider,IDfeParser parser,ArchiveService archive,
 ArchiveGate archiveGate,NfeSyncGate gate,TimeProvider clock):ICteSyncService
{
 private const EnabledDocuments Supported=EnabledDocuments.CTe|EnabledDocuments.CTeOS;
 private DateTime Now=>clock.GetUtcNow().UtcDateTime;
 public async Task<IReadOnlyList<CteCompany>> CompaniesAsync(CancellationToken ct=default)=>await db.Companies.AsNoTracking().Where(x=>x.IsActive&&(x.EnabledDocuments&Supported)!=0).OrderBy(x=>x.LegalName).Select(x=>new CteCompany(x.Id,x.LegalName,x.Cnpj,x.State,x.EnabledDocuments)).ToListAsync(ct);
 public async Task<IReadOnlyList<CteSyncRow>> StatesAsync(CancellationToken ct=default)
 {var companies=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.LegalName,ct);return (await db.DocumentSyncStates.AsNoTracking().Where(x=>x.ProviderCode==CteSource.Code).ToListAsync(ct)).Select(x=>new CteSyncRow(x.CompanyId,companies.GetValueOrDefault(x.CompanyId,"Empresa"),x.Environment,"SAVED_STATE",x.LastResponseCode,0,0,0,x.LastNsu,x.NextAllowedAtUtc)).ToArray();}
 public async Task<IReadOnlyList<CteRetainedRow>> RetainedAsync(int page=1,CancellationToken ct=default)
 {
  if(page is <1 or >100000)throw new NfeException("CTE_INVALID_REQUEST");var companies=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.LegalName,ct);
  return (await db.CteDistributionItems.AsNoTracking().Where(x=>!x.Archived).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip((page-1)*50).Take(50).Select(x=>new{x.Id,x.CompanyId,x.Environment,x.Nsu,x.Schema,x.AccessKey,x.Model,x.ResultCode}).ToListAsync(ct)).Select(x=>new CteRetainedRow(x.Id,companies.GetValueOrDefault(x.CompanyId,"Empresa"),x.Environment,x.Nsu,x.Schema,x.AccessKey,x.Model,x.ResultCode)).ToArray();
 }
 public Task<CteSyncResult> SynchronizeAsync(CteSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default)=>Run(request,false,progress,ct);
 public Task<CteSyncResult> ReprocessAsync(CteSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default)=>Run(request,true,progress,ct);
 private async Task<CteSyncResult> Run(CteSyncRequest r,bool offline,IProgress<NfeProgress>? progress,CancellationToken ct)
 {
  if(r.Companies.Count is <1 or >1000||r.Companies.Distinct().Count()!=r.Companies.Count||!Enum.IsDefined(r.Environment)||(r.Models&Supported)==0||(r.Models&~Supported)!=0||r.MaxPages is <0 or >100||r.From>r.To||r.SpecificNsu is not null&&r.Companies.Count!=1)throw new NfeException("CTE_INVALID_REQUEST");
  if(r.SpecificNsu is not null)r=r with{SpecificNsu=CteWireCodec.ValidateNsu(r.SpecificNsu,true)};
  await gate.Semaphore.WaitAsync(ct);var rows=new List<CteSyncRow>();bool cancelled=false,failed=false;Guid correlation=Guid.NewGuid();
  var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Download,Status=JobStatus.Running,StartedAtUtc=Now,TotalItems=r.Companies.Count};
  try
  {
   var companies=await db.Companies.AsNoTracking().Where(x=>r.Companies.Contains(x.Id)).OrderBy(x=>x.LegalName).ToListAsync(ct);
   if(companies.Count!=r.Companies.Count)throw new NfeException("COMPANY_NOT_FOUND");
   db.ProcessingJobs.Add(job);Audit(AuditAction.DownloadStarted,job.Id,correlation,"CTE_STARTED");await db.SaveChangesAsync(ct);
   foreach(var company in companies)
   {
    ct.ThrowIfCancellationRequested();CteSyncRow row;
    try{row=await CompanySync(company,r,offline,correlation,progress,ct);}
    catch(NfeException e){db.ChangeTracker.Clear();row=new(company.Id,company.LegalName,r.Environment,e.Code,null,0,0,0,"—",null);}
    rows.Add(row);progress?.Report(new(company.LegalName,row.Pages,row.Code,CteRow:row));var tracked=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,ct);tracked.ProcessedItems++;
    db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=job.Id,CompanyId=company.Id,ItemReference=company.Id.ToString("N"),ProviderCode=CteSource.Code,Status=Successful(row)?JobStatus.Completed:JobStatus.CompletedWithWarnings,ErrorCode=row.Code,Availability=Availability.Pending,AttemptCount=row.Pages});await db.SaveChangesAsync(ct);
   }
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;db.ChangeTracker.Clear();}
  catch{failed=true;db.ChangeTracker.Clear();throw;}
  finally
  {
   try{db.ChangeTracker.Clear();var saved=await db.ProcessingJobs.SingleOrDefaultAsync(x=>x.Id==job.Id,CancellationToken.None);if(saved is not null){saved.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:rows.All(Successful)?JobStatus.Completed:JobStatus.CompletedWithWarnings;saved.CompletedAtUtc=Now;saved.LastErrorCode=failed?"CTE_FAILED":cancelled?"CTE_CANCELLED":null;Audit(AuditAction.CteSyncCompleted,saved.Id,correlation,failed?"CTE_FAILED":cancelled?"CTE_CANCELLED":"CTE_FINISHED");await db.SaveChangesAsync(CancellationToken.None);}}
   finally{gate.Semaphore.Release();}
  }
  return new(job.Id,rows.AsReadOnly(),cancelled);
 }
 public async Task<ArchiveXml> ReadRetainedXmlAsync(Guid id,CancellationToken ct=default)
 {
  var item=await db.CteDistributionItems.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id&&!x.Archived,ct)??throw new NfeException("CTE_ITEM_NOT_FOUND");
  if(item.XmlHash!=Hash(item.XmlBytes))throw new NfeException("CTE_ITEM_HASH_MISMATCH");
  string text=CteWireCodec.ReadPayload(item.XmlBytes).ToString();bool truncated=text.Length>200000;
  return new(truncated?text[..200000]:text,truncated,item.XmlHash);
 }
 private static bool Successful(CteSyncRow row)=>row.Retained==0&&row.Code is "CTE_SYNCHRONIZED" or "CTE_PAGE_COMMITTED" or "CTE_REPROCESSED" or "WAIT_UNTIL_ALLOWED" or "CTE_LOCAL_NSU_PRESENT";
 private async Task<CteSyncRow> CompanySync(Company company,CteSyncRequest r,bool offline,Guid correlation,IProgress<NfeProgress>? progress,CancellationToken ct)
 {
  if(!company.IsActive||(company.EnabledDocuments&r.Models&Supported)==0)throw new NfeException("CTE_COMPANY_DISABLED");
  var effective=r.Models&company.EnabledDocuments&Supported;var seenPeriod=new HashSet<string>(StringComparer.Ordinal);int pages=0,archived=0,inside=0,outside=0,undated=0,newXml=0,existingXml=0;int? status=null;string code=offline?"CTE_REPROCESSED":"CTE_PAGE_COMMITTED";
  var state=await State(company.Id,r.Environment,ct);
  foreach(var receipt in await db.CteDistributionReceipts.Where(x=>x.CompanyId==company.Id&&x.Environment==r.Environment&&!x.Applied).OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToListAsync(ct))
  {var applied=await Apply(receipt,company,effective,correlation,ct,r.From,r.To,seenPeriod);archived+=applied.Archived;inside+=applied.Inside;outside+=applied.Outside;undated+=applied.Undated;newXml+=applied.NewXml;existingXml+=applied.ExistingXml;status=applied.Page.Status;db.ChangeTracker.Clear();}
  // Re-evaluate preserved items without rewinding the shared stream or using the network.
  archived+=await Reprocess(company,r.Environment,effective,correlation,progress,ct);
  state=await State(company.Id,r.Environment,ct);
  if(offline)return await Row(code);
  if(r.SpecificNsu is not null&&await db.CteDistributionItems.AnyAsync(x=>x.CompanyId==company.Id&&x.Environment==r.Environment&&x.Nsu==r.SpecificNsu&&x.Archived&&((x.Model==57&&(effective&EnabledDocuments.CTe)!=0)||(x.Model==67&&(effective&EnabledDocuments.CTeOS)!=0)),ct))
  {
   var cached=await db.CteDistributionItems.Where(x=>x.CompanyId==company.Id&&x.Environment==r.Environment&&x.Nsu==r.SpecificNsu&&x.Archived&&((x.Model==57&&(effective&EnabledDocuments.CTe)!=0)||(x.Model==67&&(effective&EnabledDocuments.CTeOS)!=0))).OrderBy(x=>x.CreatedAtUtc).FirstAsync(ct);
   // Re-evaluates the stored bytes and can restore a missing physical file. No blind metadata-only cache hit.
   await archiveGate.Semaphore.WaitAsync(ct);try{await using var tx=await db.Database.BeginTransactionAsync(ct);await TryArchive(cached,company,effective,correlation,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);}finally{archiveGate.Semaphore.Release();}
   if(cached.Archived)return await Row("CTE_LOCAL_NSU_PRESENT");
  }
  for(int number=1;(r.MaxPages==0||number<=r.MaxPages);number++)
  {
   ct.ThrowIfCancellationRequested();state=await State(company.Id,r.Environment,ct);
   var block=await RequesterBlock(company.Id,r.Environment,ct);
   if(state.NextAllowedAtUtc>Now||block>Now)return await Row("WAIT_UNTIL_ALLOWED",block);
   if(r.SpecificNsu is not null)
   {var since=Now.AddHours(-1);string rate="CTE_POINT_"+(int)r.Environment;if(await db.AuditLogs.CountAsync(x=>x.Action==AuditAction.CteRequestStarted&&x.EntityId==company.Id&&x.ResultCode==rate&&x.CreatedAtUtc>=since,ct)>=20)throw new NfeException("CTE_POINT_RATE_LIMIT");}
   var request=WireRequest(company,r.Environment,state.LastNsu,r.SpecificNsu,correlation);
   state.NextAllowedAtUtc=Now.AddHours(1);state.LastResponseCode=null;Audit(AuditAction.CteRequestStarted,company.Id,correlation,r.SpecificNsu is null?"CTE_NSU_REQUEST":"CTE_POINT_"+(int)r.Environment);await db.SaveChangesAsync(ct);
   progress?.Report(new(company.LegalName,number,"NFE_REQUESTING"));byte[] bytes;
   try{bytes=await provider.ReceiveAsync(request,ct);}
   catch(NfeException e)when(Preflight(e.Code)){state.NextAllowedAtUtc=null;await db.SaveChangesAsync(CancellationToken.None);throw;}
   pages++;
   var receipt=new CteDistributionReceipt{OrganizationId=db.OrganizationId,CompanyId=company.Id,Environment=r.Environment,RequestedNsu=state.LastNsu,SpecificNsu=r.SpecificNsu,ResponseBytes=bytes,ResponseHash=Hash(bytes)};db.CteDistributionReceipts.Add(receipt);await db.SaveChangesAsync(CancellationToken.None);
   var result=await Apply(receipt,company,effective,correlation,ct,r.From,r.To,seenPeriod);archived+=result.Archived;inside+=result.Inside;outside+=result.Outside;undated+=result.Undated;newXml+=result.NewXml;existingXml+=result.ExistingXml;status=result.Page.Status;db.ChangeTracker.Clear();state=await State(company.Id,r.Environment,ct);
   code=status is 137 or 138?result.Page.HasMore?"CTE_PAGE_COMMITTED":"CTE_SYNCHRONIZED":Status(status.Value);
   progress?.Report(new(company.LegalName,number,"CTE_PAGE_COMMITTED",CteRow:await Row(code),NewXml:newXml,ExistingXml:existingXml,InPeriod:inside,OutsidePeriod:outside,Undated:undated));if(status is not (137 or 138)||!result.Page.HasMore||r.SpecificNsu is not null)break;
   if(result.Page.NextNsu is null||Nsu.Parse(result.Page.NextNsu).Value<=Nsu.Parse(receipt.RequestedNsu).Value){code="CTE_CURSOR_NOT_ADVANCING";break;}
  }
  return await Row(code);
  async Task<CteSyncRow> Row(string result,DateTime? block=null)=>new(company.Id,company.LegalName,r.Environment,result,status,pages,archived,await db.CteDistributionItems.CountAsync(x=>x.CompanyId==company.Id&&x.Environment==r.Environment&&!x.Archived,ct),state.LastNsu,block>state.NextAllowedAtUtc?block:state.NextAllowedAtUtc);
 }
 private async Task<(NfePage Page,int Archived,int Inside,int Outside,int Undated,int NewXml,int ExistingXml)> Apply(CteDistributionReceipt receipt,Company company,EnabledDocuments models,Guid correlation,CancellationToken ct,DateOnly? from=null,DateOnly? to=null,HashSet<string>? seen=null)
 {
  if(receipt.ResponseHash!=Hash(receipt.ResponseBytes))throw new NfeException("CTE_INBOX_HASH_MISMATCH");
  var page=provider.Decode(receipt.ResponseBytes,WireRequest(company,receipt.Environment,receipt.RequestedNsu,receipt.SpecificNsu,correlation));int archived=0,inside=0,outside=0,undated=0,newXml=0,existingXml=0;
  await archiveGate.Semaphore.WaitAsync(ct);
  try
  {
   await using var tx=await db.Database.BeginTransactionAsync(ct);var state=await State(company.Id,receipt.Environment,ct);
   if(receipt.SpecificNsu is null&&Nsu.Parse(state.LastNsu).Value!=Nsu.Parse(receipt.RequestedNsu).Value)throw new NfeException("CTE_INBOX_CURSOR_DIVERGED");
   foreach(var payload in page.Items)
   {
    var item=new CteDistributionItem{OrganizationId=db.OrganizationId,ReceiptId=receipt.Id,CompanyId=company.Id,Environment=receipt.Environment,Nsu=payload.Nsu,Schema=payload.Schema,XmlBytes=payload.Xml!,XmlHash=Hash(payload.Xml!)};
    bool known=await db.ArchiveFiles.AnyAsync(x=>x.Hash==item.XmlHash,ct);db.CteDistributionItems.Add(item);await TryArchive(item,company,models,correlation,ct);if(item.Archived){archived++;if(known)existingXml++;else newXml++;var parsed=parser.Parse(item.XmlBytes,ct);if(parsed.Kind==DfePayloadKind.FullDocument&&parsed.IssueDate is DateTimeOffset issued){if(seen is null||seen.Add(parsed.Key.Value)){var day=DateOnly.FromDateTime(issued.DateTime);if((!from.HasValue||day>=from)&&(!to.HasValue||day<=to))inside++;else outside++;}}else undated++;}await db.SaveChangesAsync(ct);
   }
   if(receipt.SpecificNsu is null&&page.NextNsu is not null)state.Advance(page.NextNsu,Now);
   state.LastResponseCode=page.Status;state.NextAllowedAtUtc=page.Delay>TimeSpan.Zero?Now+page.Delay:null;
   if(page.Status==656){string key=await BlockKey(company.Id,receipt.Environment,ct);var setting=await db.ApplicationSettings.SingleOrDefaultAsync(x=>x.Key==key,ct);if(setting is null){setting=new(){OrganizationId=db.OrganizationId,Key=key};db.ApplicationSettings.Add(setting);}setting.Value=Now.AddHours(1).ToString("O",CultureInfo.InvariantCulture);}
   var tracked=await db.Companies.SingleAsync(x=>x.Id==company.Id,ct);tracked.ConnectionStatusCode=Status(page.Status);if(page.Status is 137 or 138)tracked.LastSynchronizationAtUtc=Now;
   receipt.Applied=true;receipt.ResponseCode=page.Status;Audit(AuditAction.CtePageCommitted,receipt.Id,correlation,"CTE_PAGE_COMMITTED");await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);
  }
  catch{db.ChangeTracker.Clear();throw;}
  finally{archiveGate.Semaphore.Release();}
  return(page,archived,inside,outside,undated,newXml,existingXml);
 }
 private async Task<int> Reprocess(Company company,FiscalEnvironment environment,EnabledDocuments models,Guid correlation,IProgress<NfeProgress>? progress,CancellationToken ct)
 {
  int done=0,batch=0;
  // Snapshot IDs only, then load and release at most fifty payloads at a time.
  var ids=await db.CteDistributionItems.AsNoTracking().Where(x=>x.CompanyId==company.Id&&x.Environment==environment&&!x.Archived).OrderBy(x=>x.Id).Select(x=>x.Id).ToListAsync(ct);
  foreach(var selected in ids.Chunk(50))
  {
   foreach(var id in selected)
   {
    ct.ThrowIfCancellationRequested();var item=await db.CteDistributionItems.SingleAsync(x=>x.Id==id,ct);await archiveGate.Semaphore.WaitAsync(ct);
    try{await using var tx=await db.Database.BeginTransactionAsync(ct);await TryArchive(item,company,models,correlation,ct);await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);if(item.Archived)done++;db.ChangeTracker.Clear();}
    catch{db.ChangeTracker.Clear();throw;}finally{archiveGate.Semaphore.Release();}
   }
   db.ChangeTracker.Clear();progress?.Report(new(company.LegalName,++batch,"CTE_REPROCESSED"));
  }
  return done;
 }
 private async Task TryArchive(CteDistributionItem item,Company company,EnabledDocuments models,Guid correlation,CancellationToken ct)
 {
  if(item.XmlHash!=Hash(item.XmlBytes))throw new NfeException("CTE_ITEM_HASH_MISMATCH");item.Archived=false;
  DfeParseResult parsed;IReadOnlyList<CteParticipant>? participants=null;
  try
  {
   parsed=parser.Parse(item.XmlBytes,ct);item.Model=(int)parsed.Key.Model;item.AccessKey=parsed.Key.Value;
   if(parsed.Key.Model is not (DocumentModel.CTe or DocumentModel.CTeOS)||parsed.Kind is not (DfePayloadKind.FullDocument or DfePayloadKind.Event)){item.ResultCode="CTE_LAYOUT_RETAINED";return;}
   if(parsed.Environment!=item.Environment){item.ResultCode="CTE_PAYLOAD_INVALID_RETAINED";return;}
   string expected=parsed.Kind==DfePayloadKind.Event?"procEventoCTe_v":parsed.Key.Model==DocumentModel.CTe?"procCTe_v":"procCTeOS_v";
   if(item.Schema!=expected+parsed.LayoutVersion+".xsd"){item.ResultCode="CTE_LAYOUT_RETAINED";return;}
   if((models&(parsed.Key.Model==DocumentModel.CTe?EnabledDocuments.CTe:EnabledDocuments.CTeOS))==0){item.ResultCode="CTE_MODEL_NOT_SELECTED";return;}
   if(parsed.Kind==DfePayloadKind.FullDocument)
   {participants=CteParticipants.Read(item.XmlBytes);if(!participants.Any(x=>x.TaxId==company.Cnpj)){item.ResultCode="CTE_COMPANY_MISMATCH_RETAINED";return;}}
  }
  catch(DfeParseException e){item.ResultCode=e.Code is DfeParseError.UnsupportedRoot or DfeParseError.UnsupportedVersion?"CTE_LAYOUT_RETAINED":"CTE_PAYLOAD_INVALID_RETAINED";return;}
  catch(NfeException){item.ResultCode="CTE_PAYLOAD_INVALID_RETAINED";return;}
  var outcome=await archive.StoreProviderPayloadAsync(parsed,item.XmlBytes,CteSource.Code,ct);
  if(outcome==ArchiveOutcome.Conflict){item.ResultCode="CTE_CONFLICT_RETAINED";return;}
  await db.SaveChangesAsync(ct);
  if(participants is not null)
  {
   var document=await db.FiscalDocuments.SingleAsync(x=>x.AccessKey==parsed.Key.Value&&x.Environment==item.Environment,ct);await LinkParticipants(document,participants,ct);
  }
  var known=await db.KnownDocumentKeys.SingleOrDefaultAsync(x=>x.CompanyId==company.Id&&x.Environment==item.Environment&&x.AccessKey==parsed.Key.Value,ct);
  if(known is null){known=new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,Environment=item.Environment,AccessKey=parsed.Key.Value,Source=CteSource.Code};db.KnownDocumentKeys.Add(known);}
  known.Availability=(await archive.CheckAvailabilityAsync(parsed.Key.Value,item.Environment,ct)).Availability;
  item.Archived=true;item.ResultCode="CTE_ARCHIVED";Audit(AuditAction.XmlImported,item.Id,correlation,"CTE_ARCHIVED");
 }
 private async Task LinkParticipants(FiscalDocument document,IReadOnlyList<CteParticipant> participants,CancellationToken ct)
 {
  var taxIds=participants.Select(x=>x.TaxId).Distinct().ToArray();var companies=await db.Companies.Where(x=>taxIds.Contains(x.Cnpj)).ToListAsync(ct);
  foreach(var company in companies)
  {
   var roles=participants.Where(x=>x.TaxId==company.Cnpj).Select(x=>x.Role).Distinct().Order().ToArray();
   var link=await db.FiscalDocumentCompanies.SingleOrDefaultAsync(x=>x.FiscalDocumentId==document.Id&&x.CompanyId==company.Id,ct);
   if(link is null){link=new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,FiscalDocumentId=document.Id,Role=ActorFilter.OtherParticipant};db.FiscalDocumentCompanies.Add(link);}
   link.Participation=string.Join("; ",roles);
  }
 }
 private async Task<DocumentSyncState> State(Guid company,FiscalEnvironment environment,CancellationToken ct)
 {
  // Model 57 is the representative of ONE shared CTe/CTeOS/other national stream; never create a 67 cursor.
  var state=await db.DocumentSyncStates.SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ProviderCode==CteSource.Code&&x.Environment==environment&&x.Model==DocumentModel.CTe&&x.Actor==ActorFilter.Either&&x.PayloadMode==PayloadMode.FullXml,ct);
  if(state is not null)return state;state=new(){OrganizationId=db.OrganizationId,CompanyId=company,ProviderCode=CteSource.Code,Environment=environment,Model=DocumentModel.CTe,Actor=ActorFilter.Either,PayloadMode=PayloadMode.FullXml};db.DocumentSyncStates.Add(state);await db.SaveChangesAsync(ct);return state;
 }
 private DocumentRequest WireRequest(Company company,FiscalEnvironment environment,string nsu,string? specific,Guid correlation)=>new(db.OrganizationId,company.Id,DocumentModel.CTe,environment,ActorFilter.Either,PayloadMode.FullXml,nsu,null,null,null,correlation,company.Cnpj,specific);
 private async Task<string> BlockKey(Guid company,FiscalEnvironment environment,CancellationToken ct){var link=await db.CertificateAssociations.SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ProviderCode==CteSource.Code,ct);return $"CteBlock/{(int)environment}/{link?.CertificateId??company:N}";}
 private async Task<DateTime?> RequesterBlock(Guid company,FiscalEnvironment environment,CancellationToken ct){string key=await BlockKey(company,environment,ct);var value=await db.ApplicationSettings.SingleOrDefaultAsync(x=>x.Key==key,ct);return value is not null&&DateTime.TryParseExact(value.Value,"O",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var time)?time:null;}
 private static bool Preflight(string code)=>code is "EXTERNAL_REQUESTS_DISABLED" or "COMPANY_NOT_FOUND" or "ORGANIZATION_MISMATCH" or "COMPANY_NFE_DISABLED" or "COMPANY_STATE_UNSUPPORTED" or "PROVIDER_CERTIFICATE_REQUIRED" or "CERTIFICATE_OUTSIDE_VALIDITY" or "PARTICIPANT_ROOT_CNPJ_MISMATCH" or "CERTIFICATE_STORE_UNAVAILABLE" or "CERTIFICATE_KEY_OR_VALIDITY_INVALID" or "ICP_BRASIL_POLICY_REQUIRED" or "CERTIFICATE_CHAIN_OR_REVOCATION_FAILED";
 private static string Status(int status)=>status switch{137=>"CTE_OFFICIAL_NO_DOCUMENTS",138=>"CTE_DOCUMENTS_RECEIVED",656=>"CTE_CONSUMPTION_BLOCKED",280 or 281 or 283 or 284 or 285 or 286 or 473=>"CTE_CERTIFICATE_REJECTED",_=>"CTE_OFFICIAL_REJECTION_"+status.ToString(CultureInfo.InvariantCulture)};
 private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 private void Audit(AuditAction action,Guid id,Guid correlation,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode=code});
}
