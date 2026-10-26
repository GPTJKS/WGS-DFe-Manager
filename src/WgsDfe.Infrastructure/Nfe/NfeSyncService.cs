using System.Globalization;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Parsing;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Archive;
namespace WgsDfe.Infrastructure.Nfe;
public sealed class NfeSyncGate:IDisposable
{
 internal SemaphoreSlim Semaphore{get;}=new(1,1);
 public void Dispose()=>Semaphore.Dispose();
}
public sealed class NfeSyncService(WgsDbContext db,IEnumerable<INfeDistributionProvider> providers,IDfeParser parser,
 ArchiveService archive,ArchiveGate archiveGate,NfeSyncGate syncGate,TimeProvider clock,ISyncDiagnostic? diagnostic=null):INfeSyncService
{
 private DateTime Now=>clock.GetUtcNow().UtcDateTime;
 public async Task<IReadOnlyList<NfeCompany>> CompaniesAsync(CancellationToken ct=default,string source=NfeSources.SantaCatarina)=>await db.Companies.AsNoTracking().Where(x=>x.IsActive&&(x.EnabledDocuments&NfeSources.Documents(source))!=0).OrderBy(x=>x.LegalName).Select(x=>new NfeCompany(x.Id,x.LegalName,x.Cnpj,x.State,x.TradeName)).ToListAsync(ct);
 public async Task<IReadOnlyList<NfeSyncRow>> StatesAsync(CancellationToken ct=default,string? source=null)
 {
  var states=await db.DocumentSyncStates.AsNoTracking().Where(x=>(source==null?(x.ProviderCode==NfeSources.SantaCatarina||x.ProviderCode==NfeSources.National):x.ProviderCode==source)).ToListAsync(ct);
  var names=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.LegalName,ct);
  return states.Select(x=>new NfeSyncRow(x.CompanyId,names.GetValueOrDefault(x.CompanyId,"Empresa"),$"{x.ProviderCode} · {(x.Environment==FiscalEnvironment.Production?"Produção":"Homologação")} · {ActorLabel(x.Actor)} · {(x.PayloadMode==PayloadMode.FullXml?"XML":"Chaves")}","SAVED_STATE",x.LastResponseCode,0,0,0,x.LastNsu,x.NextAllowedAtUtc)).ToArray();
 }
 public async Task<NfeSyncResult> SynchronizeAsync(NfeSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default)
 {
  if(request.Companies.Count is <1 or >1000||request.Companies.Distinct().Count()!=request.Companies.Count||!NfeSources.IsKnown(request.ProviderCode)||!Enum.IsDefined(request.Environment)||request.Actor is not (ActorFilter.Issuer or ActorFilter.Recipient or ActorFilter.Either or ActorFilter.Both)||!Enum.IsDefined(request.PayloadMode)||request.MaxPages is <0 or >100||request.From>request.To)throw new NfeException("INVALID_SYNC_REQUEST");
  string? key=request.AccessKey is null?null:DfeAccessKey.Parse(request.AccessKey).Value;
  if(key is not null&&(DfeAccessKey.Parse(key).Model!=NfeSources.Model(request.ProviderCode)||request.Companies.Count!=1))throw new NfeException("KEY_REQUIRES_ONE_COMPANY_AND_MODEL_"+(int)NfeSources.Model(request.ProviderCode));
  var provider=providers.Single(x=>x.Capabilities.Code==request.ProviderCode);
  List<Company> selected=[];
  await syncGate.Semaphore.WaitAsync(ct);
  var rows=new List<NfeSyncRow>();bool cancelled=false,failed=false;Guid correlation=Guid.NewGuid();
  var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Download,Status=JobStatus.Running,StartedAtUtc=Now,TotalItems=request.Companies.Count};
  try
  {
   selected=await db.Companies.AsNoTracking().Where(x=>request.Companies.Contains(x.Id)).OrderBy(x=>x.LegalName).ToListAsync(ct);
   if(selected.Count!=request.Companies.Count)throw new NfeException("COMPANY_NOT_FOUND");
   db.ProcessingJobs.Add(job);Audit(AuditAction.DownloadStarted,job.Id,correlation,"NFE_STARTED");await db.SaveChangesAsync(ct);
   foreach(var company in selected)
   {
    ct.ThrowIfCancellationRequested();NfeSyncRow row;NfeSyncRow? partial=null;
    var observer=new ProgressObserver(p=>{if(p.Row is not null)partial=p.Row;progress?.Report(p);});
    try{row=await SynchronizeCompany(company,request with{AccessKey=key},provider,correlation,observer,ct);}
    catch(NfeException e){db.ChangeTracker.Clear();row=partial is not null?partial with{Code=e.Code}:new(company.Id,company.LegalName,request.ProviderCode,e.Code,null,0,0,0,"—",null);}
    var current=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,ct);current.ProcessedItems++;
    db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=job.Id,CompanyId=company.Id,ItemReference=company.Id.ToString("N"),Model=NfeSources.Model(request.ProviderCode),ProviderCode=request.ProviderCode,ErrorCode=row.Code,Status=IsSuccessful(row.Code)?JobStatus.Completed:JobStatus.CompletedWithWarnings,Availability=row.Code=="LOCAL_XML_PRESENT"?Availability.Available:Availability.Pending,AttemptCount=row.Pages});
    rows.Add(row);await db.SaveChangesAsync(ct);progress?.Report(new(company.LegalName,row.Pages,row.Code,row));
   }
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;db.ChangeTracker.Clear();}
  catch{failed=true;db.ChangeTracker.Clear();throw;}
  finally
  {
   try
   {
    db.ChangeTracker.Clear();var saved=await db.ProcessingJobs.SingleOrDefaultAsync(x=>x.Id==job.Id,CancellationToken.None);
    if(saved is not null){saved.Status=failed?JobStatus.Failed:cancelled?JobStatus.Cancelled:rows.Any(x=>!IsSuccessful(x.Code))?JobStatus.CompletedWithWarnings:JobStatus.Completed;saved.CompletedAtUtc=Now;saved.LastErrorCode=failed?"NFE_FAILED":cancelled?"NFE_CANCELLED":null;Audit(AuditAction.NfeSyncCompleted,saved.Id,correlation,failed?"NFE_FAILED":cancelled?"NFE_CANCELLED":"NFE_FINISHED");await db.SaveChangesAsync(CancellationToken.None);}
   }
   finally{syncGate.Semaphore.Release();}
  }
  return new(job.Id,rows.AsReadOnly(),cancelled);
 }
 private static string ActorLabel(ActorFilter actor)=>actor switch{ActorFilter.Issuer=>"Emitente",ActorFilter.Recipient=>"Destinatário",ActorFilter.Both=>"Ambos",_=>"Qualquer"};
 private sealed class ProgressObserver(Action<NfeProgress> report):IProgress<NfeProgress>{public void Report(NfeProgress value)=>report(value);}
 private static bool IsPreflightError(string code)=>code is "EXTERNAL_REQUESTS_DISABLED" or "ORGANIZATION_MISMATCH" or "COMPANY_NOT_FOUND" or "COMPANY_NFE_DISABLED" or "COMPANY_STATE_UNSUPPORTED" or "PROVIDER_CERTIFICATE_REQUIRED" or "CERTIFICATE_OUTSIDE_VALIDITY" or "ACCOUNTANT_CERTIFICATE_REQUIRED" or "PARTICIPANT_ROOT_CNPJ_MISMATCH" or "CERTIFICATE_STORE_UNAVAILABLE" or "CERTIFICATE_KEY_OR_VALIDITY_INVALID" or "ICP_BRASIL_POLICY_REQUIRED" or "CERTIFICATE_CHAIN_OR_REVOCATION_FAILED";
 private static bool IsSuccessful(string code)=>code is "NFE_PAGE_COMMITTED" or "NFE_SYNCHRONIZED" or "LOCAL_XML_PRESENT" or "WAIT_UNTIL_ALLOWED";
 private async Task<NfeSyncRow> SynchronizeCompany(Company company,NfeSyncRequest r,INfeDistributionProvider provider,Guid correlation,IProgress<NfeProgress>? progress,CancellationToken ct)
 {
  if(!company.IsActive||(company.EnabledDocuments&NfeSources.Documents(r.ProviderCode))==0)throw new NfeException("COMPANY_NFE_DISABLED");
  if(NfeSources.IsSantaCatarina(r.ProviderCode)&&(company.State!="SC"||r.Environment!=FiscalEnvironment.Production))throw new NfeException("SC_REQUIRES_SC_COMPANY_AND_PRODUCTION");
  if(r.ProviderCode==NfeSources.National&&(r.Actor!=ActorFilter.Either||r.PayloadMode!=PayloadMode.FullXml))throw new NfeException("NATIONAL_HAS_ONE_STREAM_NO_ACTOR_FILTER");
  var periodKeys=new HashSet<string>(StringComparer.Ordinal);int pages=0,documents=0,summaries=0,newXml=0,existingXml=0,inPeriod=0,outsidePeriod=0,undated=0;string code="NFE_PAGE_COMMITTED";int? status=null;DateTime? nextAllowed=null;
  var state=await State(company.Id,r.ProviderCode,r.Environment,r.Actor,r.PayloadMode,ct);
  // Replay every pending receipt for this company/source/environment before any new network request.
  var pending=await db.NfeDistributionReceipts.Where(x=>x.CompanyId==company.Id&&x.ProviderCode==r.ProviderCode&&x.Environment==r.Environment&&!x.Applied).OrderBy(x=>x.CreatedAtUtc).ToListAsync(ct);
  foreach(var receipt in pending)
  {
   var applied=await ApplyReceipt(receipt,company,provider,correlation,ct,r.From,r.To,periodKeys);documents+=applied.Documents;summaries+=applied.Summaries;newXml+=applied.NewXml;existingXml+=applied.ExistingXml;inPeriod+=applied.InPeriod;outsidePeriod+=applied.OutsidePeriod;undated+=applied.Undated;
  }
  state=await State(company.Id,r.ProviderCode,r.Environment,r.Actor,r.PayloadMode,ct);
  if(r.AccessKey is not null)
  {
   // Cached key must belong to the selected company. Do not expose another company's cached XML.
   var local=await db.FiscalDocuments.AsNoTracking().SingleOrDefaultAsync(x=>x.AccessKey==r.AccessKey&&x.Environment==r.Environment&&(x.IssuerTaxId==company.Cnpj||x.RecipientTaxId==company.Cnpj),ct);
   if(local is not null&&(await archive.CheckAvailabilityAsync(r.AccessKey,r.Environment,ct)).Availability==Availability.Available)return Row("LOCAL_XML_PRESENT");
  }
  for(int page=1;(r.MaxPages==0||page<=r.MaxPages);page++)
  {
   ct.ThrowIfCancellationRequested();state=await State(company.Id,r.ProviderCode,r.Environment,r.Actor,r.PayloadMode,ct);
   // A block applies across filter changes and key requests; changing filters cannot bypass it.
   var allStates=await db.DocumentSyncStates.Where(x=>x.CompanyId==company.Id&&x.ProviderCode==r.ProviderCode&&x.Environment==r.Environment).ToListAsync(ct);
   var blocking=allStates.Where(x=>x.NextAllowedAtUtc>Now&&(r.ProviderCode==NfeSources.National||x==state||x.LastResponseCode==657||r.AccessKey is not null)).Select(x=>x.NextAllowedAtUtc).Max();
   var requesterKey=await RequesterBlockKey(company.Id,r.ProviderCode,r.Environment,ct);
   var requester=await db.ApplicationSettings.SingleOrDefaultAsync(x=>x.Key==requesterKey,ct);
   if(requester is not null&&DateTime.TryParseExact(requester.Value,"O",CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var until)&&until>Now&&(!blocking.HasValue||until>blocking))blocking=until;
   if(blocking>Now){nextAllowed=blocking;Trace(page,state.LastNsu,state.LastNsu,"WAIT_UNTIL_ALLOWED",0);return Row("WAIT_UNTIL_ALLOWED");}
   var documentRequest=new DocumentRequest(db.OrganizationId,company.Id,NfeSources.Model(r.ProviderCode),r.Environment,r.Actor,r.PayloadMode,state.LastNsu,r.AccessKey,provider.Capabilities.SupportsDateRange?r.From:null,provider.Capabilities.SupportsDateRange?r.To:null,correlation,company.Cnpj);
   if(!provider.CanHandle(documentRequest))throw new NfeException("UNSUPPORTED_FILTER_OR_ENVIRONMENT");
   if(r.ProviderCode==NfeSources.National&&r.AccessKey is not null)
   {
    string rateCode="NFE_KEY_"+(int)r.Environment;var since=Now.AddHours(-1);
    if(await db.AuditLogs.CountAsync(x=>x.Action==AuditAction.NfeRequestStarted&&x.EntityId==company.Id&&x.ResultCode==rateCode&&x.CreatedAtUtc>=since,ct)>=20)throw new NfeException("NATIONAL_KEY_HOURLY_LIMIT");
   }
   // Reserve an uncertainty cooldown BEFORE the HTTP request, including cancellation or process death.
   state.NextAllowedAtUtc=Now.AddHours(NfeSources.IsSantaCatarina(r.ProviderCode)?12:1);state.LastResponseCode=null;
   Audit(AuditAction.NfeRequestStarted,company.Id,correlation,r.AccessKey is null?"NFE_NSU_REQUEST":"NFE_KEY_"+(int)r.Environment);await db.SaveChangesAsync(ct);
   status=null; // This request has not received a cStat; do not attribute the previous reply to it.
   progress?.Report(new(company.LegalName,page,"NFE_REQUESTING",Row("NFE_REQUESTING")));
   var watch=System.Diagnostics.Stopwatch.StartNew();string previousNsu=state.LastNsu;byte[] response;
   try{response=await provider.ReceiveAsync(documentRequest,ct);}
   catch(NfeException e)when(IsPreflightError(e.Code))
   {state.NextAllowedAtUtc=null;await db.SaveChangesAsync(CancellationToken.None);Trace(page,previousNsu,state.LastNsu,e.Code,watch.ElapsedMilliseconds);throw;}
   catch(NfeException ex){Trace(page,previousNsu,state.LastNsu,ex.Code,watch.ElapsedMilliseconds);throw;}
   catch(OperationCanceledException){Trace(page,previousNsu,state.LastNsu,"CANCEL_REQUESTED",watch.ElapsedMilliseconds);throw;}
   pages++;
   progress?.Report(new(company.LegalName,page,"NFE_PROCESSING",Row("NFE_PROCESSING")));
   // Once received, durable inbox is written even if the user cancels at this exact moment.
   var receipt=new NfeDistributionReceipt{OrganizationId=db.OrganizationId,CompanyId=company.Id,ProviderCode=r.ProviderCode,Environment=r.Environment,Actor=r.Actor,PayloadMode=r.PayloadMode,AccessKey=r.AccessKey,RequestedNsu=state.LastNsu,ResponseBytes=response,ResponseHash=Convert.ToHexString(SHA256.HashData(response))};
   db.NfeDistributionReceipts.Add(receipt);await db.SaveChangesAsync(CancellationToken.None);
   (NfePage Page,int Documents,int Summaries,int NewXml,int ExistingXml,int InPeriod,int OutsidePeriod,int Undated) result;
   try{result=await ApplyReceipt(receipt,company,provider,correlation,ct,r.From,r.To,periodKeys);}
   catch(NfeException ex){Trace(page,previousNsu,state.LastNsu,ex.Code,watch.ElapsedMilliseconds);throw;}
   catch(OperationCanceledException){Trace(page,previousNsu,state.LastNsu,"CANCEL_DURING_COMMIT",watch.ElapsedMilliseconds);throw;}
   documents+=result.Documents;summaries+=result.Summaries;newXml+=result.NewXml;existingXml+=result.ExistingXml;inPeriod+=result.InPeriod;outsidePeriod+=result.OutsidePeriod;undated+=result.Undated;status=result.Page.Status;
   state=await State(company.Id,r.ProviderCode,r.Environment,r.Actor,r.PayloadMode,ct);nextAllowed=state.NextAllowedAtUtc;
   bool success=status is 117 or 118 or 137 or 138;code=success?result.Page.HasMore?"NFE_PAGE_COMMITTED":"NFE_SYNCHRONIZED":StatusCode(status.Value);
   bool stalled=success&&result.Page.HasMore&&r.AccessKey is null&&(result.Page.NextNsu is null||Nsu.Parse(result.Page.NextNsu).Value<=Nsu.Parse(receipt.RequestedNsu).Value);
   Trace(page,previousNsu,state.LastNsu,stalled?"NFE_CURSOR_NOT_ADVANCING":code,watch.ElapsedMilliseconds);
   progress?.Report(new(company.LegalName,page,"NFE_PAGE_COMMITTED",Row(code),NewXml:newXml,ExistingXml:existingXml,InPeriod:inPeriod,OutsidePeriod:outsidePeriod,Undated:undated));
   if(!success||!result.Page.HasMore||r.AccessKey is not null)break;
   if(result.Page.NextNsu is null||Nsu.Parse(result.Page.NextNsu).Value<=Nsu.Parse(receipt.RequestedNsu).Value){code="NFE_CURSOR_NOT_ADVANCING";break;}
  }
  return Row(code);
  NfeSyncRow Row(string result)=>new(company.Id,company.LegalName,r.ProviderCode,result,status,pages,documents,summaries,state.LastNsu,nextAllowed??state.NextAllowedAtUtc);
  void Trace(int batch,string before,string after,string decision,long elapsed)
  {
   string token=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"{db.OrganizationId:N}:{company.Id:N}")))[..16];
   diagnostic?.Write(new(correlation,r.ProviderCode,token,batch,before,after,status,elapsed,decision,documents,summaries));
  }
 }
 private async Task<(NfePage Page,int Documents,int Summaries,int NewXml,int ExistingXml,int InPeriod,int OutsidePeriod,int Undated)> ApplyReceipt(NfeDistributionReceipt receipt,Company company,INfeDistributionProvider provider,Guid correlation,CancellationToken ct,DateOnly? from=null,DateOnly? to=null,HashSet<string>? periodKeys=null)
 {
  if(!string.Equals(receipt.ResponseHash,Convert.ToHexString(SHA256.HashData(receipt.ResponseBytes)),StringComparison.Ordinal))throw new NfeException("INBOX_HASH_MISMATCH");
  var request=new DocumentRequest(db.OrganizationId,company.Id,NfeSources.Model(receipt.ProviderCode),receipt.Environment,receipt.Actor,receipt.PayloadMode,receipt.RequestedNsu,receipt.AccessKey,null,null,correlation,company.Cnpj);
  NfePage page;
  var parsed=new List<(NfeItem Item,DfeParseResult? Parsed)>();
  try
  {
   page=provider.Decode(receipt.ResponseBytes,request);
   foreach(var item in page.Items)
   {
    ct.ThrowIfCancellationRequested();DfeParseResult? p=item.Xml is null?null:parser.Parse(item.Xml,ct);
    if(receipt.ProviderCode==NfeSources.National&&p is not null)
    {
     string expected=p.Kind switch{DfePayloadKind.FullDocument=>"procNFe_v",DfePayloadKind.Event=>"procEventoNFe_v",DfePayloadKind.DocumentSummary=>"resNFe_v",_=>"resEvento_v"};
     if(entrySchema(item.Schema)!=expected+p.LayoutVersion+".xsd")throw new NfeException("DOCZIP_SCHEMA_MISMATCH");
    }
    if(receipt.ProviderCode==NfeSources.SantaCatarinaNfcom&&p?.Kind==DfePayloadKind.Event&&p.EventType is not ("110111" or "240150" or "240140" or "240151" or "240161" or "240162"))throw new NfeException("PAYLOAD_EVENT_UNSUPPORTED_INBOX_RETAINED");
    if(receipt.ProviderCode==NfeSources.SantaCatarinaNfce&&p?.Kind==DfePayloadKind.Event&&p.EventType is not ("110111" or "110112"))throw new NfeException("PAYLOAD_EVENT_UNSUPPORTED_INBOX_RETAINED");
    if(p is not null&&(p.Key.Model!=NfeSources.Model(receipt.ProviderCode)||p.Environment.HasValue&&p.Environment!=receipt.Environment||item.Key is not null&&item.Key!=p.Key.Value||receipt.AccessKey is not null&&receipt.AccessKey!=p.Key.Value))throw new NfeException("PAYLOAD_IDENTITY_MISMATCH");
    if(p is not null&&p.Kind==DfePayloadKind.FullDocument)
    {
     bool issuer=p.IssuerTaxId==company.Cnpj,recipient=p.RecipientTaxId==company.Cnpj;
     if(!issuer&&!recipient||NfeSources.IsSantaCatarina(receipt.ProviderCode)&&(receipt.Actor==ActorFilter.Issuer&&!issuer||receipt.Actor==ActorFilter.Recipient&&!recipient||receipt.Actor==ActorFilter.Both&&!(issuer&&recipient)))throw new NfeException("PAYLOAD_COMPANY_MISMATCH");
    }
    if(receipt.AccessKey is not null&&item.Key is not null&&item.Key!=receipt.AccessKey)throw new NfeException("PAYLOAD_KEY_MISMATCH");
    if(p is null&&item.Key is null)throw new NfeException("PAYLOAD_KEY_REQUIRED");
    parsed.Add((item,p));
   }
  }
  catch(DfeParseException){receipt.ResultCode="PAYLOAD_UNSUPPORTED_OR_INVALID";await db.SaveChangesAsync(CancellationToken.None);throw new NfeException("PAYLOAD_UNSUPPORTED_OR_INVALID_INBOX_RETAINED");}
  catch(NfeException){receipt.ResultCode="DISTRIBUTION_VALIDATION_FAILED";await db.SaveChangesAsync(CancellationToken.None);throw;}
  int docs=0,summaries=0,newXml=0,existingXml=0,inPeriod=0,outsidePeriod=0,undated=0;await archiveGate.Semaphore.WaitAsync(ct);
  try
  {
   await using var transaction=await db.Database.BeginTransactionAsync(ct);
   var state=await State(company.Id,receipt.ProviderCode,receipt.Environment,receipt.Actor,receipt.PayloadMode,ct);
   if(receipt.AccessKey is null&&Nsu.Parse(state.LastNsu).Value!=Nsu.Parse(receipt.RequestedNsu).Value)throw new NfeException("INBOX_CURSOR_DIVERGED");
   foreach(var entry in parsed)
   {
    var p=entry.Parsed;string key=p?.Key.Value??DfeAccessKey.Parse(entry.Item.Key).Value;
    bool full=p?.Kind is DfePayloadKind.FullDocument or DfePayloadKind.Event;
    if(full)
    {
     var outcome=await archive.StoreProviderPayloadAsync(p!,entry.Item.Xml!,receipt.ProviderCode,ct);
     if(outcome==ArchiveOutcome.Conflict)throw new NfeException("PAYLOAD_CONFLICT_INBOX_RETAINED");
     if(outcome==ArchiveOutcome.Duplicate)existingXml++;else newXml++;
     await db.SaveChangesAsync(ct);docs++;
     if(p!.Kind==DfePayloadKind.FullDocument&&p.IssueDate is DateTimeOffset issued){if(periodKeys is null||periodKeys.Add(p.Key.Value)){var day=DateOnly.FromDateTime(issued.DateTime);if((!from.HasValue||day>=from)&&(!to.HasValue||day<=to))inPeriod++;else outsidePeriod++;}}else undated++;
    }
    else summaries++;
    var known=await db.KnownDocumentKeys.SingleOrDefaultAsync(x=>x.CompanyId==company.Id&&x.Environment==receipt.Environment&&x.AccessKey==key,ct);
    if(known is null){known=new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,Environment=receipt.Environment,AccessKey=key,Source=receipt.ProviderCode};db.KnownDocumentKeys.Add(known);}
    // An event or summary does not prove that the complete note is present.
    known.Availability=(await archive.CheckAvailabilityAsync(key,receipt.Environment,ct)).Availability;
    await db.SaveChangesAsync(ct);
   }
   if(receipt.AccessKey is null&&page.NextNsu is not null)state.Advance(page.NextNsu,Now);
   state.LastResponseCode=page.Status;state.NextAllowedAtUtc=page.Delay>TimeSpan.Zero?Now+page.Delay:null;
   if(page.Status is 656 or 657)
   {
    string blockKey=await RequesterBlockKey(company.Id,receipt.ProviderCode,receipt.Environment,ct);var setting=await db.ApplicationSettings.SingleOrDefaultAsync(x=>x.Key==blockKey,ct);
    if(setting is null){setting=new(){OrganizationId=db.OrganizationId,Key=blockKey};db.ApplicationSettings.Add(setting);}setting.Value=(Now+page.Delay).ToString("O",CultureInfo.InvariantCulture);
   }
   var tracked=await db.Companies.SingleAsync(x=>x.Id==company.Id,ct);tracked.ConnectionStatusCode=StatusCode(page.Status);
   if(page.Status is 117 or 118 or 137 or 138)tracked.LastSynchronizationAtUtc=Now;
   if(NfeSources.IsSantaCatarina(receipt.ProviderCode))tracked.AccountantLinkStatusCode=page.Status is 117 or 118?"SEF_SC_LINK_ACCEPTED":page.Status==8002?"SEF_SC_LINK_DENIED":tracked.AccountantLinkStatusCode;
   receipt.Applied=true;receipt.ResponseCode=page.Status;receipt.ResultCode="NFE_PAGE_COMMITTED";
   // Keep summaries, original docZip and SOAP as durable evidence; capped by each HTTP response limit.
   Audit(AuditAction.NfePageCommitted,receipt.Id,correlation,"NFE_PAGE_COMMITTED");await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);
  }
  catch{db.ChangeTracker.Clear();throw;}
  finally{archiveGate.Semaphore.Release();}
  return(page,docs,summaries,newXml,existingXml,inPeriod,outsidePeriod,undated);
 }
 private static string entrySchema(string schema)=>schema.StartsWith("resEvento_",StringComparison.Ordinal)&&!schema.StartsWith("resEvento_v",StringComparison.Ordinal)?schema.Replace("resEvento_","resEvento_v",StringComparison.Ordinal):schema;
 private async Task<DocumentSyncState> State(Guid company,string provider,FiscalEnvironment environment,ActorFilter actor,PayloadMode mode,CancellationToken ct)
 {
  var state=await db.DocumentSyncStates.SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ProviderCode==provider&&x.Model==NfeSources.Model(provider)&&x.Environment==environment&&x.Actor==actor&&x.PayloadMode==mode,ct);
  if(state is not null)return state;
  state=new(){OrganizationId=db.OrganizationId,CompanyId=company,ProviderCode=provider,Model=NfeSources.Model(provider),Environment=environment,Actor=actor,PayloadMode=mode};db.DocumentSyncStates.Add(state);await db.SaveChangesAsync(ct);return state;
 }
 private async Task<string> RequesterBlockKey(Guid company,string provider,FiscalEnvironment environment,CancellationToken ct)
 {var link=await db.CertificateAssociations.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company&&x.ProviderCode==provider,ct);return $"NfeBlock/{provider}/{(int)environment}/{link?.CertificateId??company:N}";}
 private static string StatusCode(int status)=>status switch{117 or 137=>"NFE_NO_NEW_DOCUMENTS",118 or 138=>"NFE_DOCUMENTS_RECEIVED",656 or 657=>"NFE_CONSUMPTION_BLOCKED",8002=>"NFE_ACCOUNTANT_LINK_DENIED",8004=>"NFE_KEY_ACCESS_DENIED",632=>"NFE_OUTSIDE_SOURCE_WINDOW",116 or 217=>"NFE_NOT_FOUND_OR_OUTSIDE_WINDOW",280 or 281 or 282 or 283 or 284 or 285 or 286=>"NFE_CERTIFICATE_REJECTED",_=>"NFE_OFFICIAL_REJECTION_"+status.ToString(CultureInfo.InvariantCulture)};
 private void Audit(AuditAction action,Guid id,Guid correlation,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode=code});
}
