using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Providers;
using WgsDfe.Application.Companies;
using WgsDfe.Infrastructure.Companies;
using WgsDfe.Application.Certificates;
using WgsDfe.Infrastructure.Certificates;
namespace WgsDfe.Infrastructure;
public sealed record OrganizationContext(Guid OrganizationId) : IOrganizationContext;
public sealed record FoundationStatus(string OrganizationName,int Companies,int Documents,int Migrations,string DataDirectory);
public sealed class FoundationRuntime : IAsyncDisposable
{
 public ServiceProvider Services { get; }
 public string DataDirectory { get; }
 public Guid CorrelationId { get; }=Guid.NewGuid();
 private readonly FoundationOptions options;
 private FileStream? instanceLock;
 public FoundationRuntime(FoundationOptions options,Action<IServiceCollection>? configureHistorical=null)
 {
  options.Validate(); this.options=options; DataDirectory=options.ResolveDataDirectory();
  Directory.CreateDirectory(DataDirectory);
  var services=new ServiceCollection();
  services.AddSingleton<IOrganizationContext>(new OrganizationContext(options.Application.OrganizationId));
  services.AddSingleton<ITechnicalLog>(new SafeFileLog(Path.Combine(DataDirectory,"Logs")));
  services.AddSingleton<WgsDfe.Application.Nfe.ISyncDiagnostic>(new WgsDfe.Infrastructure.Nfe.SyncDiagnosticLog(Path.Combine(DataDirectory,"Logs")));
  services.AddScoped(_=>DatabaseFactory.Create(Path.Combine(DataDirectory,"wgs-dfe.db"),options.Application.OrganizationId));
  services.AddSingleton<ISecretStore>(new WindowsDpapiSecretStore(Path.Combine(DataDirectory,"Certificates"),options.Application.OrganizationId));
  services.AddScoped<CertificateService>();
  services.AddScoped<CertificateSelection>();
  services.AddScoped<ICertificateService>(s=>s.GetRequiredService<CertificateService>());
  services.AddScoped<ICertificateKeySource>(s=>s.GetRequiredService<CertificateService>());
  services.AddSingleton(new WgsDfe.Infrastructure.Archive.ArchiveLocation(DataDirectory,options.Application.OrganizationId));
  services.AddSingleton<WgsDfe.Infrastructure.Archive.ArchiveGate>();
  services.AddSingleton<WgsDfe.Infrastructure.Archive.IArchiveFileStore,WgsDfe.Infrastructure.Archive.ArchiveFileStore>();
  services.AddScoped<WgsDfe.Infrastructure.Archive.ArchiveService>();
  services.AddScoped<WgsDfe.Application.Archive.IArchiveService>(s=>s.GetRequiredService<WgsDfe.Infrastructure.Archive.ArchiveService>());
  services.AddSingleton<WgsDfe.Application.Parsing.IDfeParser,WgsDfe.Infrastructure.Parsing.DfeParser>();
  services.AddScoped<ICompanyService,CompanyService>();
  services.AddScoped<CompanyRegistrationService>();
  services.AddSingleton(new CompanyLookup(new HttpClient{Timeout=TimeSpan.FromSeconds(22),MaxResponseContentBufferSize=1024*1024}));
  services.AddScoped<ICompanyImportService,CompanyImportService>();
  services.AddSingleton(options.Providers);
  services.AddSingleton(TimeProvider.System);
  services.AddSingleton<WgsDfe.Infrastructure.Nfe.NfeNetworkGate>();
  services.AddSingleton<WgsDfe.Infrastructure.Nfe.NfeSyncGate>();
  services.AddSingleton<WgsDfe.Application.Nfe.INfeTransport,WgsDfe.Infrastructure.Nfe.NfeTransport>();
  services.AddScoped<WgsDfe.Application.Nfe.INfeCredentialSource,WgsDfe.Infrastructure.Nfe.NfeCredentialSource>();
  foreach(string code in new[]{WgsDfe.Application.Nfe.NfeSources.SantaCatarina,WgsDfe.Application.Nfe.NfeSources.National,WgsDfe.Application.Nfe.NfeSources.SantaCatarinaNfce,WgsDfe.Application.Nfe.NfeSources.SantaCatarinaNfcom})
   services.AddScoped<WgsDfe.Application.Nfe.INfeDistributionProvider>(s=>new WgsDfe.Providers.Nfe.NfeSoapProvider(code,s.GetRequiredService<WgsDfe.Application.Nfe.INfeCredentialSource>(),s.GetRequiredService<WgsDfe.Application.Nfe.INfeTransport>()));
  services.AddScoped<WgsDfe.Application.Cte.ICteDistributionProvider,WgsDfe.Providers.Cte.CteSoapProvider>();
  services.AddScoped<WgsDfe.Application.Cte.ICteSyncService,WgsDfe.Infrastructure.Cte.CteSyncService>();
  services.AddScoped<IProviderRegistry>(s=>new ProviderRegistry(s.GetServices<WgsDfe.Application.Nfe.INfeDistributionProvider>().Cast<IDocumentProvider>().Concat(new[]{s.GetRequiredService<WgsDfe.Application.Cte.ICteDistributionProvider>()})));
  services.AddScoped<WgsDfe.Application.Nfe.INfeSyncService,WgsDfe.Infrastructure.Nfe.NfeSyncService>();
  services.AddSingleton<WgsDfe.Infrastructure.Historical.HistoricalGate>();
  services.AddScoped<WgsDfe.Application.Historical.IHistoricalRecoveryService,WgsDfe.Infrastructure.Historical.HistoricalRecoveryService>();
  services.AddSingleton<WgsDfe.Infrastructure.Export.ExportGate>();
  services.AddScoped<WgsDfe.Infrastructure.Archive.ArchiveRetention>();
  services.AddSingleton(new WgsDfe.Application.Export.ExportLimits());
  services.AddScoped<WgsDfe.Application.Export.IExportService,WgsDfe.Infrastructure.Export.ExportService>();
  services.AddSingleton<WgsDfe.Infrastructure.Automation.AutomationGate>();
  services.AddSingleton<WgsDfe.Infrastructure.Automation.AutomationSession>();
  services.AddSingleton<WgsDfe.Infrastructure.Automation.IAutomaticDispatcher,WgsDfe.Infrastructure.Automation.AutomaticDispatcher>();
  services.AddScoped<WgsDfe.Application.Automation.IAutomationService,WgsDfe.Infrastructure.Automation.AutomationService>();
  services.AddSingleton<WgsDfe.Infrastructure.Automation.AutomationRunner>();
  configureHistorical?.Invoke(services);
  Services=services.BuildServiceProvider(new ServiceProviderOptions{ValidateScopes=true,ValidateOnBuild=true});
 }
 public async Task<FoundationStatus> InitializeAsync(CancellationToken cancellationToken=default)
 {
  if(instanceLock is not null) throw new InvalidOperationException("Fundação já inicializada.");
  // Uma instância por pasta, inclusive durante migrations; sistema operacional libera em crash.
  instanceLock=new FileStream(Path.Combine(DataDirectory,options.LockFileName),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var log=Services.GetRequiredService<ITechnicalLog>(); log.Write(TechnicalLevel.Info,TechnicalEvent.Startup,CorrelationId);
  Directory.CreateDirectory(Path.Combine(DataDirectory,"XML"));
  await using var scope=Services.CreateAsyncScope();
  var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();
  if(File.Exists(Path.Combine(DataDirectory,"wgs-dfe.db"))&&(await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Any()&&(await db.Database.GetPendingMigrationsAsync(cancellationToken)).Contains("20261020230000_CompanySoftDeletion"))
  {
   var snapshot=Path.Combine(DataDirectory,"wgs-dfe.before-v0.16.0-"+DateTime.UtcNow.ToString("yyyyMMddHHmmss")+"-"+Guid.NewGuid().ToString("N")+".sqlite");
   await using var source=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=Path.Combine(DataDirectory,"wgs-dfe.db"),Mode=Microsoft.Data.Sqlite.SqliteOpenMode.ReadOnly,Pooling=false}.ToString());
   await using var target=new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder{DataSource=snapshot,Pooling=false}.ToString());
   await source.OpenAsync(cancellationToken);await target.OpenAsync(cancellationToken);source.BackupDatabase(target);
  }
  await db.Database.MigrateAsync(cancellationToken);
  await using(var transaction=await db.Database.BeginTransactionAsync(cancellationToken))
  {
   if(!await db.Organizations.AnyAsync(cancellationToken))
   {
    db.Organizations.Add(new Organization{Id=options.Application.OrganizationId,Name=options.Application.OrganizationName});
    db.AuditLogs.Add(new AuditLog{OrganizationId=options.Application.OrganizationId,Action=AuditAction.FoundationInitialized,CorrelationId=CorrelationId,ResultCode="FOUNDATION_READY"});
    await db.SaveChangesAsync(cancellationToken);
   }
   await transaction.CommitAsync(cancellationToken);
  }
  await WgsDfe.Infrastructure.Archive.ArchiveMaintenance.InitializeAsync(db,cancellationToken);
  var interruptedIds=await db.AuditLogs.Where(x=>x.Action==AuditAction.DownloadStarted&&x.ResultCode=="NFE_STARTED").Select(x=>x.EntityId).ToListAsync(cancellationToken);
  foreach(var job in await db.ProcessingJobs.Where(x=>x.Status==JobStatus.Running&&interruptedIds.Contains(x.Id)).ToListAsync(cancellationToken))
  {job.Status=JobStatus.Failed;job.LastErrorCode="NFE_INTERRUPTED_INBOX_PRESERVED";job.CompletedAtUtc=DateTime.UtcNow;}
  var cteInterruptedIds=await db.AuditLogs.Where(x=>x.Action==AuditAction.DownloadStarted&&x.ResultCode=="CTE_STARTED").Select(x=>x.EntityId).ToListAsync(cancellationToken);
  foreach(var job in await db.ProcessingJobs.Where(x=>x.Status==JobStatus.Running&&cteInterruptedIds.Contains(x.Id)).ToListAsync(cancellationToken))
  {job.Status=JobStatus.Failed;job.LastErrorCode="CTE_INTERRUPTED_INBOX_PRESERVED";job.CompletedAtUtc=DateTime.UtcNow;}
  foreach(var job in await db.ProcessingJobs.Where(x=>x.Kind==JobKind.HistoricalRecovery&&x.Status==JobStatus.Running).ToListAsync(cancellationToken))
  {job.Status=JobStatus.Failed;job.LastErrorCode="HISTORICAL_INTERRUPTED_STATE_PRESERVED";job.CompletedAtUtc=DateTime.UtcNow;}
  foreach(var job in await db.ProcessingJobs.Where(x=>x.Kind==JobKind.Export&&x.Status==JobStatus.Running).ToListAsync(cancellationToken))
  {job.Status=JobStatus.Failed;job.LastErrorCode="EXPORT_INTERRUPTED_REVIEW_PACKAGE";job.CompletedAtUtc=DateTime.UtcNow;}
  await db.SaveChangesAsync(cancellationToken);
  await scope.ServiceProvider.GetRequiredService<WgsDfe.Application.Automation.IAutomationService>().RecoverAsync(cancellationToken);
  if(OperatingSystem.IsWindows())await scope.ServiceProvider.GetRequiredService<ICertificateService>().CleanupAsync(cancellationToken);
  log.Write(TechnicalLevel.Info,TechnicalEvent.DatabaseReady,CorrelationId);
  return new FoundationStatus((await db.Organizations.SingleAsync(cancellationToken)).Name,
   await db.Companies.CountAsync(cancellationToken),await db.FiscalDocuments.CountAsync(cancellationToken),
   (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).Count(),DataDirectory);
 }
 public async ValueTask DisposeAsync()
 {
  await Services.GetRequiredService<WgsDfe.Infrastructure.Automation.AutomationRunner>().PauseAsync();
  try { Services.GetRequiredService<ITechnicalLog>().Write(TechnicalLevel.Info,TechnicalEvent.Shutdown,CorrelationId); }
  catch(IOException) { /* Disco indisponível: liberar recursos ainda é obrigatório. */ }
  catch(UnauthorizedAccessException) { /* Encerramento não deve depender do log. */ }
  finally {instanceLock?.Dispose(); await Services.DisposeAsync();}
 }
}
