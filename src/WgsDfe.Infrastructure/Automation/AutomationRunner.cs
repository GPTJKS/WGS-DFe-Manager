using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Automation;
namespace WgsDfe.Infrastructure.Automation;
// Explicit session opt-in. No timer is started by construction or bootstrap.
public sealed class AutomationRunner:IAsyncDisposable
{
 private readonly IServiceScopeFactory scopes;private readonly AutomationSession session;private readonly ProviderOptions options;private readonly TimeProvider clock;
 public AutomationRunner(IServiceScopeFactory scopes,AutomationSession session,ProviderOptions options,TimeProvider clock)
 {this.scopes=scopes;this.session=session;this.options=options;this.clock=clock;options.RequestsDisabled+=session.Pause;}
 private readonly SemaphoreSlim lifecycle=new(1,1);private CancellationTokenSource? cancellation;private Task? loop;
 private string? lastError;public string? LastError=>Volatile.Read(ref lastError);
 public bool IsArmed=>session.IsArmed&&options.EnableExternalRequests;
 public async Task ArmAsync()
 {
  await lifecycle.WaitAsync();try
  {
   if(loop is {IsCompleted:false}&&session.IsArmed){options.EnableExternalRequests=true;return;}
   cancellation?.Cancel();if(loop is not null)await loop;
   cancellation?.Dispose();cancellation=new();session.Arm();options.EnableExternalRequests=true;lastError=null;
   loop=Task.Run(()=>Loop(cancellation.Token));
  }finally{lifecycle.Release();}
 }
 private async Task Loop(CancellationToken ct)
 {
  using var timer=new PeriodicTimer(TimeSpan.FromSeconds(30),clock);
  try
  {
   do
   {
    if(!session.IsArmed)break;
    try{await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<IAutomationService>().RunDueAsync(ct);Volatile.Write(ref lastError,null);}
    catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
    catch{Volatile.Write(ref lastError,"AUTOMATION_FAILED");session.Pause();options.EnableExternalRequests=false;break;}
   }while(await timer.WaitForNextTickAsync(ct));
  }catch(OperationCanceledException)when(ct.IsCancellationRequested){}
 }
 public async Task PauseAsync()
 {
  session.Pause();options.EnableExternalRequests=false;
  await lifecycle.WaitAsync();try{cancellation?.Cancel();if(loop is not null)await loop;loop=null;cancellation?.Dispose();cancellation=null;}finally{lifecycle.Release();}
 }
 public async ValueTask DisposeAsync(){await PauseAsync();options.RequestsDisabled-=session.Pause;lifecycle.Dispose();}
}
