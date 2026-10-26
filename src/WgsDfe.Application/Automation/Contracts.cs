using WgsDfe.Domain;
namespace WgsDfe.Application.Automation;
public sealed record ScheduleInput(Guid CompanyId,string ProviderCode,FiscalEnvironment Environment,int Hour,int Minute,
 string TimeZoneId="America/Sao_Paulo",ActorFilter Actor=ActorFilter.Either,PayloadMode PayloadMode=PayloadMode.FullXml,
 EnabledDocuments Models=EnabledDocuments.CTe|EnabledDocuments.CTeOS,int MaxPages=10,bool Enabled=false);
public sealed record ScheduleRow(Guid Id,long Revision,string Company,ScheduleInput Settings,DateTime NextRunAtUtc,DateTime? LastStartedAtUtc,string? LastCode,bool Running)
{
 public string Source=>Settings.ProviderCode;public string Time=>$"{Settings.Hour:D2}:{Settings.Minute:D2}";public string Zone=>Settings.TimeZoneId;
 public string Environment=>Settings.Environment==FiscalEnvironment.Production?"Produção":"Homologação";
 public string State=>Running?"Em execução":Settings.Enabled?"Ativo":"Pausado";
 public string Next=>TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(NextRunAtUtc,DateTimeKind.Utc),TimeZoneInfo.FindSystemTimeZoneById(Zone)).ToString("dd/MM/yyyy HH:mm");
 public string Result=>AutomationMessages.Describe(LastCode);
}
public sealed record ScheduleRunRow(Guid Id,Guid ScheduleId,string Company,string Source,DateTime StartedAtUtc,DateTime? CompletedAtUtc,string Code,Guid? JobId)
{public string Result=>AutomationMessages.Describe(Code);}
public interface IAutomationService
{
 Task<IReadOnlyList<ScheduleRow>> ListAsync(CancellationToken ct=default);
 Task<IReadOnlyList<ScheduleRunRow>> RunsAsync(CancellationToken ct=default);
 Task<ScheduleRow> SaveAsync(ScheduleInput input,Guid? id=null,long? revision=null,CancellationToken ct=default);
 Task DeleteAsync(Guid id,long revision,CancellationToken ct=default);
 Task<int> RunDueAsync(CancellationToken ct=default);
 Task RecoverAsync(CancellationToken ct=default);
}
public sealed class AutomationException(string code):Exception(AutomationMessages.Describe(code)){public string Code{get;}=code;}
public static class AutomationMessages
{
 public static string Describe(string? code)=>code switch
 {
  null=>"Ainda não executado","SCHEDULE_INVALID"=>"Confira empresa, fonte, ambiente, horário e opções",
  "SCHEDULE_MISSING"=>"Agendamento não encontrado; atualize a lista","SCHEDULE_CHANGED"=>"Agendamento alterado ou em execução; atualize a lista",
  "SCHEDULE_DUPLICATE"=>"Já existe agendamento com essa empresa, fonte, ambiente e horário",
  "SCHEDULE_HISTORY_SCOPE"=>"Crie outro agendamento para mudar empresa, fonte, ambiente ou filtros de um horário com histórico",
  "SCHEDULE_HAS_HISTORY"=>"Agendamento com histórico deve ser pausado, preservando as execuções",
  "SCHEDULE_LIMIT"=>"Limite de 1.000 agendamentos atingido","AUTOMATION_PAUSED"=>"Automação pausada nesta sessão",
  "AUTOMATION_STARTED"=>"Sincronização automática iniciada","AUTOMATION_FAILED"=>"Falha na execução; confira a consulta manual",
  "AUTOMATION_CANCELLED"=>"Execução cancelada; recebimentos confirmados preservados",
  "AUTOMATION_INTERRUPTED"=>"Execução interrompida; conferir recebimentos antes da próxima janela",
  "AUTOMATION_COMPANY_DISABLED"=>"Empresa inativa ou sem o modelo habilitado",
  "AUTOMATION_PRODUCTION_REQUIRED"=>"Agendamento de homologação preservado e não executado. Crie um horário em Produção.",
  "AUTOMATION_SOURCE_STATE"=>"Fonte SC exige empresa de SC",
  _=>WgsDfe.Application.Cte.CteMessages.Describe(code)
 };
}
