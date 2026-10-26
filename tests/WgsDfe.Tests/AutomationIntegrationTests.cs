using System.IO.Compression;
using System.Text;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Automation;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
using WgsDfe.Database;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Automation;
using WgsDfe.Providers.Nfe;
using WgsDfe.Providers.Cte;
using Xunit;
namespace WgsDfe.Tests;
public sealed class AutomationIntegrationTests
{
 [Theory][InlineData(NfeSources.SantaCatarina)][InlineData(NfeSources.National)][InlineData(NfeSources.SantaCatarinaNfce)][InlineData(NfeSources.SantaCatarinaNfcom)][InlineData(CteSource.Code)]
 public async Task ExistingHomologationSchedulesNeverTransmit(string source)
 {
  await using var h=await Harness.Create(source);h.Arm();
  await using var scope=h.Runtime.Services.CreateAsyncScope();
  var result=await scope.ServiceProvider.GetRequiredService<IAutomaticDispatcher>().DispatchAsync(new(h.Company,source,FiscalEnvironment.Homologation,6,0),default);
  Assert.Equal("AUTOMATION_PRODUCTION_REQUIRED",result.Code);Assert.Equal(0,h.Transport.Calls);
 }
 [Theory][InlineData(NfeSources.SantaCatarina)][InlineData(NfeSources.National)][InlineData(NfeSources.SantaCatarinaNfce)][InlineData(NfeSources.SantaCatarinaNfcom)][InlineData(CteSource.Code)]
 public async Task AutomaticDispatchUsesRealCoordinatorArchivesXmlAndHonorsManualCooldown(string source)
 {
  await using var h=await Harness.Create(source);await h.Save();h.Arm();h.Clock.Advance(TimeSpan.FromHours(2));
  Assert.Equal(1,await h.Use(s=>s.RunDueAsync()));Assert.Equal(1,h.Transport.Calls);
  await using(var scope=h.Runtime.Services.CreateAsyncScope())
  {
   var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();var doc=await db.FiscalDocuments.SingleAsync();Assert.Equal(source==CteSource.Code?DocumentModel.CTe:NfeSources.Model(source),doc.Model);var state=await db.DocumentSyncStates.SingleAsync();Assert.Equal("1",state.LastNsu);Assert.NotNull(state.NextAllowedAtUtc);var run=await db.SyncScheduleRuns.SingleAsync();Assert.NotNull(run.JobId);Assert.Equal(JobStatus.Completed,(await db.ProcessingJobs.SingleAsync(x=>x.Id==run.JobId)).Status);
   if(source==CteSource.Code){var result=await scope.ServiceProvider.GetRequiredService<ICteSyncService>().SynchronizeAsync(new([h.Company],FiscalEnvironment.Production));Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(result.Rows).Code);}
   else{var result=await scope.ServiceProvider.GetRequiredService<INfeSyncService>().SynchronizeAsync(new([h.Company],source,FiscalEnvironment.Production));Assert.Equal("WAIT_UNTIL_ALLOWED",Assert.Single(result.Rows).Code);}
  }
  Assert.Equal(1,h.Transport.Calls);Assert.Equal(0,await h.Use(s=>s.RunDueAsync()));
 }
 [Fact]public async Task RealMissingCertificatePreflightIsRecordedWithoutNetwork()
 {await using var h=await Harness.Create(NfeSources.SantaCatarina,fakeCredentials:false);await h.Save();h.Arm();h.Clock.Advance(TimeSpan.FromHours(2));await h.Use(s=>s.RunDueAsync());Assert.Equal(0,h.Transport.Calls);Assert.Equal("ACCOUNTANT_CERTIFICATE_REQUIRED",Assert.Single(await h.Use(s=>s.RunsAsync())).Code);}
 [Fact]public async Task ClaimFailurePreventsAnyNetworkCall()
 {await using var h=await Harness.Create(NfeSources.SantaCatarina);await h.Save();await using(var db=h.Db())await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER deny_schedule_claim BEFORE INSERT ON SyncScheduleRuns BEGIN SELECT RAISE(ABORT,'SYNTHETIC'); END;");h.Arm();h.Clock.Advance(TimeSpan.FromHours(2));await Assert.ThrowsAsync<DbUpdateException>(()=>h.Use(s=>s.RunDueAsync()));Assert.Equal(0,h.Transport.Calls);Assert.Empty(await h.Use(s=>s.RunsAsync()));await using var check=h.Db();Assert.False((await check.SyncSchedules.SingleAsync()).Running);}
 [Fact]public async Task RunnerStartsDueWorkAndPauseCancelsAtSafeBoundary()
 {await using var h=await Harness.Create(NfeSources.SantaCatarina);await h.Save();h.Clock.Advance(TimeSpan.FromHours(2));var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);h.Transport.Before=async ct=>{started.SetResult();await Task.Delay(Timeout.InfiniteTimeSpan,ct);};var runner=h.Runtime.Services.GetRequiredService<AutomationRunner>();await runner.ArmAsync();await started.Task.WaitAsync(TimeSpan.FromSeconds(10));await runner.PauseAsync();Assert.False(runner.IsArmed);Assert.Equal("AUTOMATION_CANCELLED",Assert.Single(await h.Use(s=>s.RunsAsync())).Code);Assert.False(Assert.Single(await h.Use(s=>s.ListAsync())).Running);}
 [Fact]public async Task NewRuntimePreservesScheduleButNeverRestoresSessionConsent()
 {await using var h=await Harness.Create(NfeSources.SantaCatarina);await h.Save();await h.Runtime.DisposeAsync();h.Disposed=true;await using var reopened=new FoundationRuntime(h.Options);await reopened.InitializeAsync();Assert.False(reopened.Services.GetRequiredService<AutomationSession>().IsArmed);await using var scope=reopened.Services.CreateAsyncScope();Assert.Single(await scope.ServiceProvider.GetRequiredService<IAutomationService>().ListAsync());Assert.Equal(0,await scope.ServiceProvider.GetRequiredService<IAutomationService>().RunDueAsync());}
 [Fact]public async Task MigrationFromPhase11PreservesCompanyXmlAndCursor()
 {string root=Path.Combine(Path.GetTempPath(),"wgs-auto-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);var org=Guid.NewGuid();try{await using var db=DatabaseFactory.Create(Path.Combine(root,"db"),org);await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261018165457_HistoricalRecovery");db.Organizations.Add(new(){Id=org,Name="Synthetic"});var c=new Company{OrganizationId=org,Cnpj="12345678000195",LegalName="Preserved"};await db.SaveChangesAsync();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Companies (Id, OrganizationId, CreatedAtUtc, UpdatedAtUtc, Revision, LegalName, Cnpj, State, SearchText, IsActive, EnabledDocuments) VALUES ({c.Id}, {org}, {DateTime.UtcNow}, {DateTime.UtcNow}, 1, {c.LegalName}, {c.Cnpj}, {c.State}, {"PRESERVED"}, 1, {(int)EnabledDocuments.All})");db.DocumentSyncStates.Add(new(){OrganizationId=org,CompanyId=c.Id,ProviderCode=NfeSources.SantaCatarina,Model=DocumentModel.NFe,Environment=FiscalEnvironment.Production,LastNsu="42"});db.FiscalDocuments.Add(new(){OrganizationId=org,AccessKey=DfeParserTests.Key(),Model=DocumentModel.NFe,Environment=FiscalEnvironment.Production,XmlHash="SYNTHETIC",FileRelativePath="XML/untouched.xml"});await db.SaveChangesAsync();await db.Database.MigrateAsync();Assert.Equal("Preserved",(await db.Companies.SingleAsync()).LegalName);Assert.Equal("42",(await db.DocumentSyncStates.SingleAsync()).LastNsu);Assert.Equal("XML/untouched.xml",(await db.FiscalDocuments.SingleAsync()).FileRelativePath);Assert.Empty(await db.SyncSchedules.ToListAsync());Assert.False(db.Database.HasPendingModelChanges());}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(root,true);}}
 private sealed class Clock:TimeProvider{private DateTimeOffset now=new(2026,10,18,8,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan delta)=>now+=delta;}
 private sealed class Credentials:INfeCredentialSource
 {public Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct){using var key=RSA.Create(2048);var cert=new CertificateRequest("CN=SYNTHETIC",key,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));return Task.FromResult(new NfeCredential(request.CompanyTaxId!,"42",cert));}}
 private sealed class Transport(string source):INfeTransport
 {public int Calls;public Func<CancellationToken,Task>? Before;public async Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct){Calls++;if(Before is not null)await Before(ct);return Response(source);}}
 private static string Compress(string value){using var m=new MemoryStream();using(var gz=new GZipStream(m,CompressionMode.Compress,true))gz.Write(Encoding.UTF8.GetBytes(value));return Convert.ToBase64String(m.ToArray());}
 private static byte[] Response(string source)
 {
  bool cte=source==CteSource.Code,national=source==NfeSources.National;int model=cte?57:(int)NfeSources.Model(source);string doc=DfeParserTests.Document(model).Replace("<tpAmb>2","<tpAmb>1");string ns,op,ret;string soap=cte?"http://www.w3.org/2003/05/soap-envelope":"http://schemas.xmlsoap.org/soap/envelope/";
  if(cte||national)
  {
   ns=cte?CteWireCodec.WsNamespace:NfeWireCodec.NationalNamespace;op=cte?"cteDistDFeInteresse":"nfeDistDFeInteresse";
   ret=$"<retDistDFeInt xmlns='{(cte?CteWireCodec.Namespace:NfeWireCodec.NfeNamespace)}' versao='{(cte?"1.00":"1.01")}'><tpAmb>1</tpAmb><verAplic>Teste</verAplic><cStat>138</cStat><xMotivo>Sintético</xMotivo><dhResp>2026-10-18T07:00:00-03:00</dhResp><ultNSU>1</ultNSU><maxNSU>1</maxNSU><loteDistDFeInt><docZip NSU='1' schema='{(cte?"procCTe_v4.00.xsd":"procNFe_v4.00.xsd")}'>{Compress(doc)}</docZip></loteDistDFeInt></retDistDFeInt>";
  }
  else
  {
   string root=source==NfeSources.SantaCatarina?"NFe":source==NfeSources.SantaCatarinaNfce?"NFCe":"NFCom";ns=source==NfeSources.SantaCatarina?NfeWireCodec.ScNamespace:source==NfeSources.SantaCatarinaNfce?NfeWireCodec.NfceNamespace:NfeWireCodec.NfcomNamespace;op=source==NfeSources.SantaCatarina?"NfeDownloadContab":source==NfeSources.SantaCatarinaNfce?"nfceDownloadContab":"nfcomDownloadContab";string version=source==NfeSources.SantaCatarina?"2.02":"1.00";
   string lot=$"<loteDist{root}SC versao='{(root=="NFe"?"2.00":"1.00")}'><dist{root}SC NSU='1' chAcesso='{DfeParserTests.Key(model)}'>{doc}</dist{root}SC></loteDist{root}SC>";
   ret=$"<retDist{root}SC xmlns='{ns}' versao='{version}'><tpAmb>1</tpAmb><cStat>118</cStat><xMotivo>Sintético</xMotivo><dhResp>2026-10-18T07:00:00-03:00</dhResp><CNPJ>12345678000195</CNPJ><ultNuNSURet>1</ultNuNSURet><qtDfeRet>1</qtDfeRet><loteDistComp>{Compress(lot)}</loteDistComp></retDist{root}SC>";
  }
  return Encoding.UTF8.GetBytes($"<s:Envelope xmlns:s='{soap}'><s:Body><{op}Response xmlns='{ns}'><{op}Result>{ret}</{op}Result></{op}Response></s:Body></s:Envelope>");
 }
 private sealed class Harness:IAsyncDisposable
 {
  public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public FoundationOptions Options{get;private set;}=null!;public FoundationRuntime Runtime{get;private set;}=null!;public Clock Clock{get;}=new();public Transport Transport{get;private set;}=null!;public bool Disposed;private string source="";
  public string Root{get;}=Path.Combine(Path.GetTempPath(),"wgs-auto-integration-"+Guid.NewGuid().ToString("N"));public WgsDbContext Db()=>DatabaseFactory.Create(Path.Combine(Root,"wgs-dfe.db"),Org);
  public static async Task<Harness> Create(string source,bool fakeCredentials=true){var h=new Harness{source=source,Transport=new(source)};h.Options=new(){Application=new(){OrganizationId=h.Org,OrganizationName="Synthetic",DataDirectory=h.Root}};h.Runtime=new(h.Options,s=>{s.AddSingleton<TimeProvider>(h.Clock);s.AddSingleton<INfeTransport>(h.Transport);if(fakeCredentials)s.AddSingleton<INfeCredentialSource,Credentials>();});await h.Runtime.InitializeAsync();await using var db=h.Db();var company=new Company{OrganizationId=h.Org,LegalName="Synthetic",Cnpj="12345678000195",State="SC"};h.Company=company.Id;db.Companies.Add(company);await db.SaveChangesAsync();return h;}
  public async Task<T> Use<T>(Func<IAutomationService,Task<T>> action){await using var scope=Runtime.Services.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<IAutomationService>());}
  public Task<ScheduleRow> Save()=>Use(s=>s.SaveAsync(new(Company,source,FiscalEnvironment.Production,6,0,Enabled:true)));
  public void Arm(){Runtime.Services.GetRequiredService<AutomationSession>().Arm();Runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=true;}
  public async ValueTask DisposeAsync(){if(!Disposed)await Runtime.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(Root,true);}
 }
}
