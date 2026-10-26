using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Automation;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
namespace WgsDfe.Infrastructure.Automation;
public sealed class AutomaticDispatcher(IServiceScopeFactory scopes,AutomationSession session,ProviderOptions options):IAutomaticDispatcher
{
 public async Task<AutomaticDispatchResult> DispatchAsync(ScheduleInput input,CancellationToken ct)
 {
  if(input.Environment!=WgsDfe.Domain.FiscalEnvironment.Production)return new("AUTOMATION_PRODUCTION_REQUIRED");
  if(!session.IsArmed||!options.EnableExternalRequests)return new("AUTOMATION_PAUSED");
  await using var scope=scopes.CreateAsyncScope();
  if(input.ProviderCode==CteSource.Code)
  {
   var result=await scope.ServiceProvider.GetRequiredService<ICteSyncService>().SynchronizeAsync(new([input.CompanyId],input.Environment,input.Models,MaxPages:input.MaxPages),ct:ct);
   var row=result.Rows.SingleOrDefault();return new(result.Cancelled?"AUTOMATION_CANCELLED":row?.Code??"AUTOMATION_FAILED",result.JobId,row?.NextAllowedAtUtc);
  }
  else
  {
   var result=await scope.ServiceProvider.GetRequiredService<INfeSyncService>().SynchronizeAsync(new([input.CompanyId],input.ProviderCode,input.Environment,input.Actor,input.PayloadMode,MaxPages:input.MaxPages),ct:ct);
   var row=result.Rows.SingleOrDefault();return new(result.Cancelled?"AUTOMATION_CANCELLED":row?.Code??"AUTOMATION_FAILED",result.JobId,row?.NextAllowedAtUtc);
  }
 }
}
