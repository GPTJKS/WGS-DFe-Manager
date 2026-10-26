using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Automation;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
using WgsDfe.Application.Companies;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Automation;
using Xunit;
namespace WgsDfe.Tests;
public sealed class AutomationTests
{
 [Theory][InlineData(8,59,9)][InlineData(9,0,33)][InlineData(10,0,33)]
 public void SaoPauloDailySlotsAreStrictlyFuture(int hour,int minute,int expectedHours)
 {var after=new DateTime(2026,10,18,hour,minute,0,DateTimeKind.Utc);var next=ScheduleClock.Next(new(Guid.NewGuid(),NfeSources.SantaCatarina,FiscalEnvironment.Production,6,0),after);Assert.Equal(new DateTime(2026,10,18,0,0,0,DateTimeKind.Utc).AddHours(expectedHours),next);}
 [Fact]public void SpringGapMovesToFirstValidMinute()
 {var next=ScheduleClock.Next(new(Guid.NewGuid(),NfeSources.National,FiscalEnvironment.Production,2,30,"America/New_York"),new(2026,3,8,0,0,0,DateTimeKind.Utc));Assert.Equal(new DateTime(2026,3,8,7,0,0,DateTimeKind.Utc),next);}
 [Fact]public void FallFoldRunsOnlyOnceAtLaterOccurrence()
 {var i=new ScheduleInput(Guid.NewGuid(),NfeSources.National,FiscalEnvironment.Production,1,30,"America/New_York");var next=ScheduleClock.Next(i,new(2026,11,1,5,40,0,DateTimeKind.Utc));Assert.Equal(new DateTime(2026,11,1,6,30,0,DateTimeKind.Utc),next);Assert.Equal(new DateTime(2026,11,2,6,30,0,DateTimeKind.Utc),ScheduleClock.Next(i,next));}
 [Fact]public void InvalidClockKindIsRejected()=>Assert.Throws<AutomationException>(()=>ScheduleClock.Next(new(Guid.NewGuid(),"CTE",FiscalEnvironment.Production,6,0),DateTime.SpecifyKind(DateTime.Now,DateTimeKind.Unspecified)));
 [Fact]public async Task DefaultsAreDisabledAndCreationNeverDispatches()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input() with{Enabled=false});Assert.False(row.Settings.Enabled);Assert.False(h.Session.IsArmed);Assert.False(h.Options.EnableExternalRequests);Assert.Equal(0,await h.Due());Assert.Empty(h.Dispatch.Calls);Assert.Equal(new DateTime(2026,10,18,9,0,0,DateTimeKind.Utc),row.NextRunAtUtc);}
 [Theory][InlineData(false,true)][InlineData(true,false)][InlineData(false,false)]public async Task BothSessionAuthorizationAndNetworkAreRequired(bool armed,bool network)
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Advance();if(armed)h.Session.Arm();h.Options.EnableExternalRequests=network;Assert.Equal(0,await h.Due());Assert.Empty(h.Dispatch.Calls);Assert.Empty(await h.Runs());}
 [Fact]public async Task EnabledSlotRunsOnceAndPersistsHistoryBeforeDispatch()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input());h.Arm();h.Advance();h.Dispatch.Callback=async(_,_)=>{await using var db=h.Db();var s=await db.SyncSchedules.SingleAsync();Assert.True(s.Running);Assert.True(s.NextRunAtUtc>h.Clock.GetUtcNow().UtcDateTime);Assert.Single(await db.SyncScheduleRuns.ToListAsync());};Assert.Equal(1,await h.Due());Assert.Equal(0,await h.Due());Assert.Single(h.Dispatch.Calls);var run=Assert.Single(await h.Runs());Assert.Equal("NFE_SYNCHRONIZED",run.Code);Assert.NotNull(run.CompletedAtUtc);Assert.False(Assert.Single(await h.List()).Running);}
 [Fact]public async Task PausedScheduleDoesNotRunEvenInArmedSession()
 {await using var h=await Harness.Create();await h.Save(h.Input() with{Enabled=false});h.Arm();h.Advance();Assert.Equal(0,await h.Due());}
 [Fact]public async Task MultipleMissedDaysAreCoalescedNotReplayed()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Clock.Advance(TimeSpan.FromDays(20));h.Arm();Assert.Equal(1,await h.Due());Assert.Equal(0,await h.Due());Assert.Single(h.Dispatch.Calls);}
 [Theory][InlineData(NfeSources.SantaCatarina)][InlineData(NfeSources.National)][InlineData(NfeSources.SantaCatarinaNfce)][InlineData(NfeSources.SantaCatarinaNfcom)][InlineData(CteSource.Code)]
 public async Task AllFiveSourcesPreserveSelection(string source)
 {await using var h=await Harness.Create();var input=h.Input() with{ProviderCode=source,Models=EnabledDocuments.CTeOS,MaxPages=3};await h.Save(input);h.Arm();h.Advance();await h.Due();Assert.Equal(input,Assert.Single(h.Dispatch.Calls));}
 [Fact]public async Task CooldownAdvancesToNextDailyWindowWithoutTightRetry()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Arm();h.Advance();h.Dispatch.Result=new("WAIT_UNTIL_ALLOWED",NextAllowedAtUtc:new(2026,10,20,12,0,0,DateTimeKind.Utc));await h.Due();Assert.Equal(new DateTime(2026,10,21,9,0,0,DateTimeKind.Utc),Assert.Single(await h.List()).NextRunAtUtc);Assert.Equal(0,await h.Due());}
 [Fact]public async Task CooldownAtExactScheduledTimeDoesNotSkipExtraDay()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Arm();h.Advance();h.Dispatch.Result=new("WAIT_UNTIL_ALLOWED",NextAllowedAtUtc:new(2026,10,20,9,0,0,DateTimeKind.Utc));await h.Due();Assert.Equal(new DateTime(2026,10,20,9,0,0,DateTimeKind.Utc),Assert.Single(await h.List()).NextRunAtUtc);}
 [Fact]public async Task UnexpectedExceptionIsSanitizedAndNoImmediateRetry()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Dispatch.Callback=(_,_)=>throw new Exception("SECRET_PASSWORD");h.Arm();h.Advance();Assert.Equal(1,await h.Due());Assert.Equal("AUTOMATION_FAILED",Assert.Single(await h.Runs()).Code);Assert.Equal(0,await h.Due());}
 [Fact]public async Task ArbitraryReturnedCodeIsNotPersisted()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Dispatch.Result=new("password=<secret>");h.Arm();h.Advance();await h.Due();Assert.Equal("AUTOMATION_FAILED",Assert.Single(await h.Runs()).Code);}
 [Fact]public async Task CancelledDispatchHasCompletedHistoryAndNoSameSlotReplay()
 {await using var h=await Harness.Create();await h.Save(h.Input());using var cts=new CancellationTokenSource();h.Dispatch.Callback=(_,ct)=>{cts.Cancel();ct.ThrowIfCancellationRequested();return Task.CompletedTask;};h.Arm();h.Advance();Assert.Equal(1,await h.Due(cts.Token));Assert.Equal("AUTOMATION_CANCELLED",Assert.Single(await h.Runs()).Code);Assert.Equal(0,await h.Due());}
 [Fact]public async Task CancelBeforeClaimCreatesNoAttempt()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Arm();h.Advance();using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>h.Due(cts.Token));Assert.Empty(await h.Runs());}
 [Fact]public async Task InactivatedCompanyIsBlockedBeforeDispatcher()
 {await using var h=await Harness.Create();await h.Save(h.Input());await using(var db=h.Db()){var c=await db.Companies.SingleAsync();c.IsActive=false;await db.SaveChangesAsync();}h.Arm();h.Advance();await h.Due();Assert.Empty(h.Dispatch.Calls);Assert.Equal("AUTOMATION_COMPANY_DISABLED",Assert.Single(await h.Runs()).Code);var row=Assert.Single(await h.List());await h.Save(row.Settings with{Enabled=false},row);Assert.False(Assert.Single(await h.List()).Settings.Enabled);}
 [Fact]public async Task DisabledModelIsBlockedBeforeDispatcher()
 {await using var h=await Harness.Create();await h.Save(h.Input());await using(var db=h.Db()){var c=await db.Companies.SingleAsync();c.EnabledDocuments=EnabledDocuments.NFCe;await db.SaveChangesAsync();}h.Arm();h.Advance();await h.Due();Assert.Empty(h.Dispatch.Calls);}
 [Fact]public async Task ConcurrentTicksCannotDoubleDispatch()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Arm();h.Advance();var result=await Task.WhenAll(h.Due(),h.Due());Assert.Equal(1,result.Sum());Assert.Single(h.Dispatch.Calls);}
 [Fact]public async Task SaveAndPauseUseRevisionAndRetainAudit()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input());var updated=await h.Save(row.Settings with{Enabled=false},row);Assert.True(updated.Revision>row.Revision);var e=await Assert.ThrowsAsync<AutomationException>(()=>h.Save(row.Settings,row));Assert.Equal("SCHEDULE_CHANGED",e.Code);await using var db=h.Db();Assert.Contains(await db.AuditLogs.ToListAsync(),a=>a.Action==AuditAction.SchedulePaused);}
 [Fact]public async Task DuplicateSlotRejectedButSecondHourAllowed()
 {await using var h=await Harness.Create();await h.Save(h.Input());Assert.Equal("SCHEDULE_DUPLICATE",(await Assert.ThrowsAsync<AutomationException>(()=>h.Save(h.Input()))).Code);await h.Save(h.Input() with{Hour=18});Assert.Equal(2,(await h.List()).Count);}
 [Fact]public async Task DeleteWithoutHistoryWorksButHistoryRequiresPause()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input());await h.Use(async s=>{await s.DeleteAsync(row.Id,row.Revision);return true;});Assert.Empty(await h.List());row=await h.Save(h.Input());h.Arm();h.Advance();await h.Due();row=Assert.Single(await h.List());Assert.Equal("SCHEDULE_HAS_HISTORY",(await Assert.ThrowsAsync<AutomationException>(()=>h.Use(async s=>{await s.DeleteAsync(row.Id,row.Revision);return true;}))).Code);}
 [Theory][InlineData("source")][InlineData("environment")][InlineData("scHomologation")][InlineData("hour")][InlineData("minute")][InlineData("zone")][InlineData("pages")][InlineData("actor")][InlineData("payload")][InlineData("models")][InlineData("company")]
 public async Task InvalidInputsAreRejected(string field)
 {await using var h=await Harness.Create();var input=field switch{"source"=>h.Input() with{ProviderCode="BOGUS"},"environment"=>h.Input() with{Environment=(FiscalEnvironment)99},"scHomologation"=>h.Input() with{Environment=FiscalEnvironment.Homologation},"hour"=>h.Input() with{Hour=24},"minute"=>h.Input() with{Minute=-1},"zone"=>h.Input() with{TimeZoneId="../../etc"},"pages"=>h.Input() with{MaxPages=101},"actor"=>h.Input() with{Actor=ActorFilter.OtherParticipant},"payload"=>h.Input() with{PayloadMode=(PayloadMode)5},"models"=>h.Input() with{ProviderCode=CteSource.Code,Models=EnabledDocuments.NFe},_=>h.Input() with{CompanyId=Guid.NewGuid()}};await Assert.ThrowsAsync<AutomationException>(()=>h.Save(input));Assert.Empty(await h.List());}
 [Fact]public async Task ScCompanyRequiredOnlyForScSources()
 {await using var h=await Harness.Create();await using(var db=h.Db()){var c=await db.Companies.SingleAsync();c.State="PR";await db.SaveChangesAsync();}Assert.Equal("AUTOMATION_SOURCE_STATE",(await Assert.ThrowsAsync<AutomationException>(()=>h.Save(h.Input()))).Code);await h.Save(h.Input() with{ProviderCode=NfeSources.National,Environment=FiscalEnvironment.Homologation});}
 [Fact]public async Task NewSaveDoesNotImmediatelyCatchUpPastHour()
 {await using var h=await Harness.Create();h.Clock.Advance(TimeSpan.FromHours(8));var row=await h.Save(h.Input());Assert.Equal(new DateTime(2026,10,19,9,0,0,DateTimeKind.Utc),row.NextRunAtUtc);}
 [Fact]public async Task RecoveryMarksIncompleteAttemptAndPreservesFutureWindow()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input());await using(var db=h.Db()){var s=await db.SyncSchedules.SingleAsync();s.Running=true;s.NextRunAtUtc=new(2026,10,25,9,0,0,DateTimeKind.Utc);db.SyncScheduleRuns.Add(new(){OrganizationId=h.Org,ScheduleId=row.Id,StartedAtUtc=h.Clock.GetUtcNow().UtcDateTime});await db.SaveChangesAsync();}await h.Use(async s=>{await s.RecoverAsync();return true;});var recovered=Assert.Single(await h.List());Assert.False(recovered.Running);Assert.Equal("AUTOMATION_INTERRUPTED",recovered.LastCode);Assert.Equal(new DateTime(2026,10,25,9,0,0,DateTimeKind.Utc),recovered.NextRunAtUtc);Assert.Equal("AUTOMATION_INTERRUPTED",Assert.Single(await h.Runs()).Code);Assert.Empty(h.Dispatch.Calls);}
 [Fact]public async Task TenantFiltersAndForeignKeysProtectSchedulesAndRuns()
 {await using var h=await Harness.Create();var row=await h.Save(h.Input());var other=Guid.NewGuid();await using var db=DatabaseFactory.Create(h.PathToDb,other);db.Organizations.Add(new(){Id=other,Name="Other"});await db.SaveChangesAsync();Assert.Empty(await db.SyncSchedules.ToListAsync());db.SyncSchedules.Add(new(){OrganizationId=other,CompanyId=h.Company,ProviderCode="CTE",MaxPages=10,NextRunAtUtc=DateTime.UtcNow});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());db.ChangeTracker.Clear();db.SyncScheduleRuns.Add(new(){OrganizationId=other,ScheduleId=row.Id,StartedAtUtc=DateTime.UtcNow});await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());}
 [Fact]public async Task CompanyDeleteDisablesScheduleAndKeepsHistoryWhileCnpjChangeIsBlocked()
 {await using var h=await Harness.Create();await h.Save(h.Input());await using var scope=h.Runtime.Services.CreateAsyncScope();var service=scope.ServiceProvider.GetRequiredService<ICompanyService>();var c=(await service.FindAsync(h.Company))!;Assert.Equal(CompanyError.HasDependencies,(await Assert.ThrowsAsync<CompanyException>(()=>service.UpdateAsync(c.Id,c.Revision,c.Data with{Cnpj="12345678000276"},Guid.NewGuid()))).Code);await service.DeleteAsync(c.Id,c.Revision,Guid.NewGuid());Assert.Null(await service.FindAsync(c.Id));await using var db=h.Db();Assert.False((await db.SyncSchedules.SingleAsync()).Enabled);}
 [Fact]public async Task RunnerDoesNotStartOnBootstrapAndPauseRevokesNetwork()
 {await using var h=await Harness.Create();var runner=h.Runtime.Services.GetRequiredService<AutomationRunner>();Assert.False(runner.IsArmed);Assert.Empty(h.Dispatch.Calls);await runner.ArmAsync();Assert.True(runner.IsArmed);await runner.PauseAsync();Assert.False(runner.IsArmed);Assert.False(h.Options.EnableExternalRequests);}
 [Fact]public async Task ManualNetworkDisableRevokesAutomaticConsent()
 {await using var h=await Harness.Create();var runner=h.Runtime.Services.GetRequiredService<AutomationRunner>();await runner.ArmAsync();h.Options.EnableExternalRequests=false;Assert.False(h.Session.IsArmed);h.Options.EnableExternalRequests=true;Assert.False(runner.IsArmed);await runner.PauseAsync();}
 [Theory][InlineData(true)][InlineData(false)]public async Task NationalUnsupportedFiltersRejectedBeforeSave(bool actor)
 {await using var h=await Harness.Create();var input=h.Input() with{ProviderCode=NfeSources.National};input=actor?input with{Actor=ActorFilter.Issuer}:input with{PayloadMode=PayloadMode.KeysOnly};await Assert.ThrowsAsync<AutomationException>(()=>h.Save(input));}
 [Fact]public async Task HistoricalScopeCannotBeRelabeled()
 {await using var h=await Harness.Create();await h.Save(h.Input());h.Arm();h.Advance();await h.Due();var row=Assert.Single(await h.List());Assert.Equal("SCHEDULE_HISTORY_SCOPE",(await Assert.ThrowsAsync<AutomationException>(()=>h.Save(row.Settings with{ProviderCode=NfeSources.National},row))).Code);Assert.Equal(NfeSources.SantaCatarina,Assert.Single(await h.Runs()).Source);}
 [Fact]public async Task SnapshotHasNoPendingChanges()
 {await using var h=await Harness.Create();await using var db=h.Db();Assert.False(db.Database.HasPendingModelChanges());Assert.Equal(9,(await db.Database.GetAppliedMigrationsAsync()).Count());}
 private sealed class Clock:TimeProvider
 {private long ticks=new DateTime(2026,10,18,8,0,0,DateTimeKind.Utc).Ticks;public override DateTimeOffset GetUtcNow()=>new(Interlocked.Read(ref ticks),TimeSpan.Zero);public void Advance(TimeSpan amount)=>Interlocked.Add(ref ticks,amount.Ticks);}
 private sealed class Dispatch:IAutomaticDispatcher
 {public List<ScheduleInput> Calls{get;}=[];public AutomaticDispatchResult Result=new("NFE_SYNCHRONIZED");public Func<ScheduleInput,CancellationToken,Task>? Callback;public async Task<AutomaticDispatchResult> DispatchAsync(ScheduleInput i,CancellationToken ct){Calls.Add(i);if(Callback is not null)await Callback(i,ct);return Result;}}
 private sealed class Harness:IAsyncDisposable
 {
  public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public string Root{get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-auto-test-"+Guid.NewGuid().ToString("N"));
  public string PathToDb=>System.IO.Path.Combine(Root,"wgs-dfe.db");public Clock Clock{get;}=new();public Dispatch Dispatch{get;}=new();public FoundationRuntime Runtime{get;private set;}=null!;
  public AutomationSession Session=>Runtime.Services.GetRequiredService<AutomationSession>();public ProviderOptions Options=>Runtime.Services.GetRequiredService<ProviderOptions>();
  public static async Task<Harness> Create(){var h=new Harness();h.Runtime=new(new(){Application=new(){OrganizationId=h.Org,OrganizationName="Synthetic",DataDirectory=h.Root}},s=>{s.AddSingleton<TimeProvider>(h.Clock);s.AddSingleton<IAutomaticDispatcher>(h.Dispatch);});await h.Runtime.InitializeAsync();await using var db=h.Db();var c=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Synthetic",State="SC"};h.Company=c.Id;db.Companies.Add(c);await db.SaveChangesAsync();return h;}
  public WgsDbContext Db()=>DatabaseFactory.Create(PathToDb,Org);
  public ScheduleInput Input()=>new(Company,NfeSources.SantaCatarina,FiscalEnvironment.Production,6,0,Enabled:true);
  public void Arm(){Session.Arm();Options.EnableExternalRequests=true;}public void Advance()=>Clock.Advance(TimeSpan.FromHours(2));
  public Task<T> Use<T>(Func<IAutomationService,Task<T>> action)=>UseScope(action);
  private async Task<T> UseScope<T>(Func<IAutomationService,Task<T>> action){await using var scope=Runtime.Services.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<IAutomationService>());}
  public Task<ScheduleRow> Save(ScheduleInput i,ScheduleRow? row=null)=>Use(s=>s.SaveAsync(i,row?.Id,row?.Revision));public Task<int> Due(CancellationToken ct=default)=>Use(s=>s.RunDueAsync(ct));public Task<IReadOnlyList<ScheduleRow>> List()=>Use(s=>s.ListAsync());public Task<IReadOnlyList<ScheduleRunRow>> Runs()=>Use(s=>s.RunsAsync());
  public async ValueTask DisposeAsync(){await Runtime.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(Root,true);}
 }
}
