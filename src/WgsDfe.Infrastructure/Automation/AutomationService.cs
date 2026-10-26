using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Automation;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Automation;
public sealed class AutomationGate:IDisposable{internal SemaphoreSlim Semaphore{get;}=new(1,1);public void Dispose()=>Semaphore.Dispose();}
public sealed class AutomationSession
{
 private int armed;public bool IsArmed=>Volatile.Read(ref armed)==1;
 public void Arm()=>Interlocked.Exchange(ref armed,1);public void Pause()=>Interlocked.Exchange(ref armed,0);
}
public sealed record AutomaticDispatchResult(string Code,Guid? JobId=null,DateTime? NextAllowedAtUtc=null);
public interface IAutomaticDispatcher{Task<AutomaticDispatchResult> DispatchAsync(ScheduleInput input,CancellationToken ct);}
public sealed class AutomationService(WgsDbContext db,AutomationGate gate,AutomationSession session,ProviderOptions options,IAutomaticDispatcher dispatcher,TimeProvider clock):IAutomationService
{
 private DateTime Now=>clock.GetUtcNow().UtcDateTime;
 internal static ScheduleInput Input(SyncSchedule s)=>new(s.CompanyId,s.ProviderCode,s.Environment,s.Hour,s.Minute,s.TimeZoneId,s.Actor,s.PayloadMode,s.Models,s.MaxPages,s.Enabled);
 private static ScheduleRow Row(SyncSchedule s,string company)=>new(s.Id,s.Revision,company,Input(s),s.NextRunAtUtc,s.LastStartedAtUtc,s.LastCode,s.Running);
 public async Task<IReadOnlyList<ScheduleRow>> ListAsync(CancellationToken ct=default)
 {var names=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.LegalName,ct);return (await db.SyncSchedules.AsNoTracking().OrderBy(x=>x.NextRunAtUtc).ToListAsync(ct)).Select(s=>Row(s,names.GetValueOrDefault(s.CompanyId,"Empresa"))).ToArray();}
 public async Task<IReadOnlyList<ScheduleRunRow>> RunsAsync(CancellationToken ct=default)=>await (from run in db.SyncScheduleRuns.AsNoTracking() join s in db.SyncSchedules.AsNoTracking() on run.ScheduleId equals s.Id join c in db.Companies.AsNoTracking() on s.CompanyId equals c.Id orderby run.StartedAtUtc descending select new ScheduleRunRow(run.Id,s.Id,c.LegalName,s.ProviderCode,run.StartedAtUtc,run.CompletedAtUtc,run.Code,run.JobId)).Take(200).ToListAsync(ct);
 private async Task<Company> Validate(ScheduleInput i,CancellationToken ct,bool requireEligible=true)
 {
  bool cte=i.ProviderCode==CteSource.Code;
  if(i.CompanyId==Guid.Empty||(!cte&&!NfeSources.IsKnown(i.ProviderCode))||!Enum.IsDefined(i.Environment)||
   i.TimeZoneId is not("America/Sao_Paulo" or "UTC")||i.MaxPages is <1 or >100||!Enum.IsDefined(i.PayloadMode)||
   (cte?(i.Models==EnabledDocuments.None||(i.Models&~(EnabledDocuments.CTe|EnabledDocuments.CTeOS))!=0||i.Actor!=ActorFilter.Either||i.PayloadMode!=PayloadMode.FullXml):
    i.Actor is not(ActorFilter.Issuer or ActorFilter.Recipient or ActorFilter.Either))||i.ProviderCode==NfeSources.National&&(i.Actor!=ActorFilter.Either||i.PayloadMode!=PayloadMode.FullXml)||NfeSources.IsSantaCatarina(i.ProviderCode)&&i.Environment!=FiscalEnvironment.Production)
   throw new AutomationException("SCHEDULE_INVALID");
  _=ScheduleClock.Next(i,Now);
  var company=await db.Companies.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.CompanyId,ct)??throw new AutomationException("SCHEDULE_INVALID");
  if(requireEligible&&NfeSources.IsSantaCatarina(i.ProviderCode)&&company.State!="SC")throw new AutomationException("AUTOMATION_SOURCE_STATE");
  var models=cte?i.Models:NfeSources.Documents(i.ProviderCode);
  if(requireEligible&&(!company.IsActive||(company.EnabledDocuments&models)!=models))throw new AutomationException("AUTOMATION_COMPANY_DISABLED");
  return company;
 }
 public async Task<ScheduleRow> SaveAsync(ScheduleInput input,Guid? id=null,long? revision=null,CancellationToken ct=default)
 {
  await gate.Semaphore.WaitAsync(ct);try
  {
   var company=await Validate(input,ct,input.Enabled);SyncSchedule s;
   if(id.HasValue){s=await db.SyncSchedules.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new AutomationException("SCHEDULE_MISSING");if(s.Revision!=revision||s.Running)throw new AutomationException("SCHEDULE_CHANGED");}
   else{if(revision.HasValue)throw new AutomationException("SCHEDULE_INVALID");if(await db.SyncSchedules.CountAsync(ct)>=1000)throw new AutomationException("SCHEDULE_LIMIT");s=new(){OrganizationId=db.OrganizationId};db.SyncSchedules.Add(s);}
   if(id.HasValue&&await db.SyncScheduleRuns.AnyAsync(x=>x.ScheduleId==s.Id,ct)&&
    (s.CompanyId!=input.CompanyId||s.ProviderCode!=input.ProviderCode||s.Environment!=input.Environment||s.Actor!=input.Actor||s.PayloadMode!=input.PayloadMode||s.Models!=input.Models))throw new AutomationException("SCHEDULE_HISTORY_SCOPE");
   if(await db.SyncSchedules.AnyAsync(x=>x.Id!=s.Id&&x.CompanyId==input.CompanyId&&x.ProviderCode==input.ProviderCode&&x.Environment==input.Environment&&x.Hour==input.Hour&&x.Minute==input.Minute&&x.TimeZoneId==input.TimeZoneId,ct))throw new AutomationException("SCHEDULE_DUPLICATE");
   s.CompanyId=input.CompanyId;s.ProviderCode=input.ProviderCode;s.Environment=input.Environment;s.Hour=input.Hour;s.Minute=input.Minute;s.TimeZoneId=input.TimeZoneId;s.Actor=input.Actor;s.PayloadMode=input.PayloadMode;s.Models=input.Models;s.MaxPages=input.MaxPages;s.Enabled=input.Enabled;
   s.NextRunAtUtc=ScheduleClock.Next(input,Now);Audit(input.Enabled?AuditAction.ScheduleSaved:AuditAction.SchedulePaused,s.Id,"SCHEDULE_SAVED");await db.SaveChangesAsync(ct);return Row(s,company.LegalName);
  }
  catch(DbUpdateConcurrencyException){throw new AutomationException("SCHEDULE_CHANGED");}
  finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
 }
 public async Task DeleteAsync(Guid id,long revision,CancellationToken ct=default)
 {
  await gate.Semaphore.WaitAsync(ct);try
  {
   var s=await db.SyncSchedules.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new AutomationException("SCHEDULE_MISSING");
   if(s.Revision!=revision||s.Running)throw new AutomationException("SCHEDULE_CHANGED");
   if(await db.SyncScheduleRuns.AnyAsync(x=>x.ScheduleId==id,ct))throw new AutomationException("SCHEDULE_HAS_HISTORY");
   db.SyncSchedules.Remove(s);Audit(AuditAction.ScheduleDeleted,id,"SCHEDULE_DELETED");await db.SaveChangesAsync(ct);
  }catch(DbUpdateConcurrencyException){throw new AutomationException("SCHEDULE_CHANGED");}finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
 }
 public async Task<int> RunDueAsync(CancellationToken ct=default)
 {
  if(!session.IsArmed||!options.EnableExternalRequests)return 0;
  await gate.Semaphore.WaitAsync(ct);int count=0;
  try
  {
   if(!session.IsArmed||!options.EnableExternalRequests)return 0;
   var ids=await db.SyncSchedules.AsNoTracking().Where(x=>x.Enabled&&!x.Running&&x.NextRunAtUtc<=Now).OrderBy(x=>x.NextRunAtUtc).ThenBy(x=>x.Id).Select(x=>x.Id).Take(25).ToListAsync(ct);
   foreach(var id in ids)
   {
    ct.ThrowIfCancellationRequested();if(!session.IsArmed||!options.EnableExternalRequests)break;
    db.ChangeTracker.Clear();var s=await db.SyncSchedules.SingleAsync(x=>x.Id==id,ct);var input=Input(s);var started=Now;
    var run=new SyncScheduleRun{OrganizationId=db.OrganizationId,ScheduleId=s.Id,StartedAtUtc=started};
    // Persist the claim and next window BEFORE network; a crash must not replay a missed slot.
    s.NextRunAtUtc=ScheduleClock.Next(input,started);s.LastStartedAtUtc=started;s.LastCode="AUTOMATION_STARTED";s.Running=true;
    db.SyncScheduleRuns.Add(run);Audit(AuditAction.ScheduleRunStarted,s.Id,"AUTOMATION_STARTED");await db.SaveChangesAsync(ct);count++;
    AutomaticDispatchResult result;
    try{await Validate(input,ct);result=await dispatcher.DispatchAsync(input,ct);}
    catch(OperationCanceledException)when(ct.IsCancellationRequested){result=new("AUTOMATION_CANCELLED");}
    catch(AutomationException e){result=new(e.Code);}
    catch(NfeException e){result=new(e.Code);}
    catch{result=new("AUTOMATION_FAILED");}
    string code=SafeCode(result.Code);s.Running=false;s.LastCode=code;run.Code=code;run.CompletedAtUtc=Now;run.JobId=result.JobId;
    // Advance to the next scheduled daily window at or after the provider's cooldown.
    var floor=Now;if(result.NextAllowedAtUtc is {} cooldown&&cooldown>floor)floor=DateTime.SpecifyKind(cooldown,DateTimeKind.Utc).AddTicks(-1);
    var next=ScheduleClock.Next(input,floor);if(next>s.NextRunAtUtc)s.NextRunAtUtc=next;
    Audit(AuditAction.ScheduleRunCompleted,s.Id,code);await db.SaveChangesAsync(CancellationToken.None);
    if(ct.IsCancellationRequested)break;
   }
   return count;
  }
  finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
 }
 public async Task RecoverAsync(CancellationToken ct=default)
 {
  await gate.Semaphore.WaitAsync(ct);try
  {
   foreach(var s in await db.SyncSchedules.Where(x=>x.Running).ToListAsync(ct))
   {s.Running=false;s.LastCode="AUTOMATION_INTERRUPTED";var next=ScheduleClock.Next(Input(s),Now);if(s.NextRunAtUtc<next)s.NextRunAtUtc=next;Audit(AuditAction.ScheduleRunInterrupted,s.Id,"AUTOMATION_INTERRUPTED");}
   foreach(var run in await db.SyncScheduleRuns.Where(x=>x.CompletedAtUtc==null).ToListAsync(ct)){run.CompletedAtUtc=Now;run.Code="AUTOMATION_INTERRUPTED";}
   await db.SaveChangesAsync(ct);
  }finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
 }
 private static string SafeCode(string code)=>!string.IsNullOrEmpty(code)&&code.Length<=100&&code.All(c=>c is >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')?code:"AUTOMATION_FAILED";
 private void Audit(AuditAction action,Guid id,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=Guid.NewGuid(),ResultCode=code});
}
