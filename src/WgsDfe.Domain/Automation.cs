namespace WgsDfe.Domain;
public sealed class SyncSchedule:OrganizationEntity
{
 public Guid CompanyId{get;set;} public string ProviderCode{get;set;}="";
 public FiscalEnvironment Environment{get;set;} public int Hour{get;set;} public int Minute{get;set;}
 public string TimeZoneId{get;set;}="America/Sao_Paulo";public ActorFilter Actor{get;set;}=ActorFilter.Either;
 public PayloadMode PayloadMode{get;set;}=PayloadMode.FullXml;public EnabledDocuments Models{get;set;}
 public int MaxPages{get;set;}=10;public bool Enabled{get;set;} public DateTime NextRunAtUtc{get;set;}
 public DateTime? LastStartedAtUtc{get;set;} public string? LastCode{get;set;} public bool Running{get;set;}
}
public sealed class SyncScheduleRun:OrganizationEntity
{
 public Guid ScheduleId{get;set;}public DateTime StartedAtUtc{get;set;}public DateTime? CompletedAtUtc{get;set;}
 public string Code{get;set;}="AUTOMATION_STARTED";public Guid? JobId{get;set;}
}
