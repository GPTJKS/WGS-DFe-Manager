using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Providers;
using Xunit;
namespace WgsDfe.Tests;
public sealed class FoundationTests
{
 private static readonly Guid OfficeA=Guid.NewGuid();
 private static readonly Guid OfficeB=Guid.NewGuid();
 private static Company Company(Guid office,string cnpj="12345678000195") => new(){OrganizationId=office,Cnpj=cnpj,LegalName="Empresa teste"};
 private static async Task Seed(WgsDbContext db,Guid office)
 {
  db.Organizations.Add(new Organization{Id=office,Name="Teste"});
  await db.SaveChangesAsync();
 }
 [Fact] public async Task Migrations_AreIdempotent_AndKeepData()
 {
  using var temp=new TempDirectory();
  await using(var db=DatabaseFactory.Create(temp.Db,OfficeA))
  {
   await db.Database.MigrateAsync(); await Seed(db,OfficeA);
   db.Companies.Add(Company(OfficeA)); await db.SaveChangesAsync();
  }
  await using(var db=DatabaseFactory.Create(temp.Db,OfficeA))
  {
   await db.Database.MigrateAsync();
   Assert.Single(await db.Companies.ToListAsync());
   Assert.Equal(9,(await db.Database.GetAppliedMigrationsAsync()).Count());
   Assert.False(db.Database.HasPendingModelChanges());
  }
 }
 [Fact] public async Task DuplicateCnpj_IsRejectedByDatabase()
 {
  using var temp=new TempDirectory(); await using var db=DatabaseFactory.Create(temp.Db,OfficeA);
  await db.Database.MigrateAsync(); await Seed(db,OfficeA);
  db.Companies.AddRange(Company(OfficeA),Company(OfficeA));
  await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 [Fact] public async Task QueryFilters_AndWriteGuard_IsolateOrganizations()
 {
  using var temp=new TempDirectory();
  await using(var a=DatabaseFactory.Create(temp.Db,OfficeA))
  {await a.Database.MigrateAsync();await Seed(a,OfficeA);a.Companies.Add(Company(OfficeA));await a.SaveChangesAsync();}
  await using var b=DatabaseFactory.Create(temp.Db,OfficeB);await Seed(b,OfficeB);
  Assert.Empty(await b.Companies.ToListAsync());
  b.Companies.Add(Company(OfficeB));await b.SaveChangesAsync(); // Mesmo CNPJ, outra organização.
  b.Companies.Add(Company(OfficeA,"12345678000276"));
  await Assert.ThrowsAsync<InvalidOperationException>(()=>b.SaveChangesAsync());
 }
 [Fact] public async Task CompositeForeignKey_PreventsCrossOfficeAssociation()
 {
  using var temp=new TempDirectory(); Guid companyId;
  await using(var a=DatabaseFactory.Create(temp.Db,OfficeA))
  {await a.Database.MigrateAsync();await Seed(a,OfficeA);var c=Company(OfficeA);companyId=c.Id;a.Companies.Add(c);await a.SaveChangesAsync();}
  await using var b=DatabaseFactory.Create(temp.Db,OfficeB);await Seed(b,OfficeB);
  var cert=new Certificate{OrganizationId=OfficeB,Label="Teste",SecureStoreReference="windows-store:example"};
  b.Certificates.Add(cert);await b.SaveChangesAsync();
  b.CertificateAssociations.Add(new(){OrganizationId=OfficeB,CompanyId=companyId,CertificateId=cert.Id,ProviderCode="TEST"});
  await Assert.ThrowsAsync<DbUpdateException>(()=>b.SaveChangesAsync());
 }
 [Fact] public async Task DocumentIsSharedBetweenCompanies_ButUniqueInArchive()
 {
  using var temp=new TempDirectory();await using var db=DatabaseFactory.Create(temp.Db,OfficeA);
  await db.Database.MigrateAsync();await Seed(db,OfficeA);
  var a=Company(OfficeA);var b=Company(OfficeA,"12345678000276");
  var doc=new FiscalDocument{OrganizationId=OfficeA,AccessKey=new string('1',44),Model=DocumentModel.NFe,Environment=FiscalEnvironment.Production,TotalValue=123456789.01m};
  db.Companies.AddRange(a,b);db.FiscalDocuments.Add(doc);
  db.FiscalDocumentCompanies.AddRange(new FiscalDocumentCompany{OrganizationId=OfficeA,CompanyId=a.Id,FiscalDocumentId=doc.Id,Role=ActorFilter.Issuer},new FiscalDocumentCompany{OrganizationId=OfficeA,CompanyId=b.Id,FiscalDocumentId=doc.Id,Role=ActorFilter.Recipient});
  await db.SaveChangesAsync();db.ChangeTracker.Clear();
  Assert.Equal(123456789.01m,(await db.FiscalDocuments.SingleAsync()).TotalValue);
  Assert.Equal(2,await db.FiscalDocumentCompanies.CountAsync());
  db.FiscalDocuments.Add(new FiscalDocument{OrganizationId=OfficeA,AccessKey=doc.AccessKey,Environment=doc.Environment});
  await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 [Fact] public async Task EventCanArriveBeforeDocument_AndCannotDuplicate()
 {
  using var temp=new TempDirectory();await using var db=DatabaseFactory.Create(temp.Db,OfficeA);
  await db.Database.MigrateAsync();await Seed(db,OfficeA);
  FiscalDocumentEvent Event()=>new(){OrganizationId=OfficeA,AccessKey=new string('1',44),Environment=FiscalEnvironment.Production,EventTypeCode="110111",SequenceNumber=1};
  db.FiscalDocumentEvents.Add(Event());await db.SaveChangesAsync();
  db.FiscalDocumentEvents.Add(Event());await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 [Fact] public async Task ConcurrentUpdates_DoNotLoseData()
 {
  using var temp=new TempDirectory();
  await using var a=DatabaseFactory.Create(temp.Db,OfficeA);await a.Database.MigrateAsync();await Seed(a,OfficeA);
  a.Companies.Add(Company(OfficeA));await a.SaveChangesAsync();a.ChangeTracker.Clear();
  await using var b=DatabaseFactory.Create(temp.Db,OfficeA);
  var ca=await a.Companies.SingleAsync();var cb=await b.Companies.SingleAsync();
  ca.LegalName="Primeira edição";await a.SaveChangesAsync();
  cb.LegalName="Edição concorrente";await Assert.ThrowsAsync<DbUpdateConcurrencyException>(()=>b.SaveChangesAsync());
 }
 [Fact] public async Task RolledBackBatch_DoesNotAdvanceNsu()
 {
  using var temp=new TempDirectory();await using var db=DatabaseFactory.Create(temp.Db,OfficeA);
  await db.Database.MigrateAsync();await Seed(db,OfficeA);var c=Company(OfficeA);db.Companies.Add(c);
  var state=new DocumentSyncState{OrganizationId=OfficeA,CompanyId=c.Id,ProviderCode="TEST",Model=DocumentModel.NFe,LastNsu="10"};
  db.DocumentSyncStates.Add(state);await db.SaveChangesAsync();
  await using(var tx=await db.Database.BeginTransactionAsync())
  {
   state.Advance("20",DateTime.UtcNow);
   db.FiscalDocuments.Add(new(){OrganizationId=OfficeA,AccessKey="invalid"});
   await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
   await tx.RollbackAsync();
  }
  db.ChangeTracker.Clear();Assert.Equal("10",(await db.DocumentSyncStates.SingleAsync()).LastNsu);
 }
 [Fact] public async Task SyncStreams_DistinguishFiltersAndEnvironment()
 {
  using var temp=new TempDirectory();await using var db=DatabaseFactory.Create(temp.Db,OfficeA);
  await db.Database.MigrateAsync();await Seed(db,OfficeA);var c=Company(OfficeA);db.Companies.Add(c);
  DocumentSyncState State(ActorFilter actor,PayloadMode mode,FiscalEnvironment env)=>new(){OrganizationId=OfficeA,CompanyId=c.Id,ProviderCode="TEST",Model=DocumentModel.NFe,Actor=actor,PayloadMode=mode,Environment=env};
  db.DocumentSyncStates.AddRange(State(ActorFilter.Either,PayloadMode.FullXml,FiscalEnvironment.Production),State(ActorFilter.Issuer,PayloadMode.FullXml,FiscalEnvironment.Production),State(ActorFilter.Either,PayloadMode.KeysOnly,FiscalEnvironment.Production),State(ActorFilter.Either,PayloadMode.FullXml,FiscalEnvironment.Homologation));
  await db.SaveChangesAsync();Assert.Equal(4,await db.DocumentSyncStates.CountAsync());
  db.DocumentSyncStates.Add(State(ActorFilter.Either,PayloadMode.FullXml,FiscalEnvironment.Production));
  await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());
 }
 [Fact] public async Task RuntimeBootstrap_IsIdempotent_AndNoExternalProviders()
 {
  using var temp=new TempDirectory();var opts=new FoundationOptions{Application=new(){OrganizationId=OfficeA,OrganizationName="Teste",DataDirectory=temp.Path}};
  await using(var runtime=new FoundationRuntime(opts))
  {
   var status=await runtime.InitializeAsync();Assert.Equal(9,status.Migrations);
   await using var providersScope=runtime.Services.CreateAsyncScope();
   Assert.Equal(5,providersScope.ServiceProvider.GetRequiredService<IProviderRegistry>().Providers.Count);
  }
  await using(var runtime=new FoundationRuntime(opts))
  {await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();Assert.Single(await scope.ServiceProvider.GetRequiredService<WgsDbContext>().AuditLogs.ToListAsync());}
 }
 [Fact] public async Task Runtime_RejectsSecondInstance()
 {
  using var temp=new TempDirectory();var opts=new FoundationOptions{Application=new(){OrganizationId=OfficeA,DataDirectory=temp.Path}};
  await using var a=new FoundationRuntime(opts);await a.InitializeAsync();
  await using var b=new FoundationRuntime(opts);await Assert.ThrowsAsync<IOException>(()=>b.InitializeAsync());
 }
 [Theory]
 [InlineData("")][InlineData("-1")][InlineData("1e5")][InlineData("9999999999999999999")][InlineData("١٢")]
 public void NsuRejectsInvalidFormats(string text)=>Assert.Throws<ArgumentException>(()=>Nsu.Parse(text));
 [Fact] public void NsuAccepts18DigitsWithoutPrecisionLoss()
 {Assert.Equal("999999999999999999",Nsu.Parse("999999999999999999").ToString());Assert.Equal("42",Nsu.Parse("00042").ToString());}
 [Fact] public void NsuCannotRegress()
 {var s=new DocumentSyncState{LastNsu="20"};Assert.Throws<InvalidOperationException>(()=>s.Advance("19",DateTime.UtcNow));}
 [Fact] public void ConfigurationAllowsImplementedExternalRequestsButRejectsRelativeDirectory()
 {
  var options=new FoundationOptions{Application=new(){OrganizationId=OfficeA},Providers=new(){EnableExternalRequests=true}};
  options.Validate();options.Providers.EnableExternalRequests=false;
  options.Application.DataDirectory="relative";Assert.Throws<InvalidOperationException>(()=>options.Validate());
 }
 [Fact] public void TechnicalLog_ContainsOnlyApprovedFields()
 {
  using var temp=new TempDirectory();new SafeFileLog(temp.Path).Write(TechnicalLevel.Error,TechnicalEvent.StartupFailed,Guid.NewGuid());
  var text=File.ReadAllText(Directory.GetFiles(temp.Path,"*.jsonl").Single());
  using var json=System.Text.Json.JsonDocument.Parse(text);
  Assert.Equal(new[]{"atUtc","level","eventCode","correlationId"},json.RootElement.EnumerateObject().Select(p=>p.Name));
 }
 [Fact] public void ProviderRegistryRejectsDuplicateCodes()
 {Assert.Throws<ArgumentException>(()=>new ProviderRegistry(new[]{new FakeProvider(),new FakeProvider()}));}
 private sealed class FakeProvider : IDocumentProvider
 {
  public ProviderCapabilities Capabilities=>new("FAKE",new HashSet<DocumentModel>{DocumentModel.NFe},CertificateRequirement.None,false,false,false,false,false);
  public bool CanHandle(DocumentRequest request)=>false;
  public Task<ProviderResult> GetDocumentsAsync(DocumentRequest request,CancellationToken cancellationToken)=>throw new NotSupportedException();
 }
 private sealed class TempDirectory : IDisposable
 {
  public string Path {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-test-"+Guid.NewGuid().ToString("N"));
  public string Db=>System.IO.Path.Combine(Path,"test.db");
  public TempDirectory()=>Directory.CreateDirectory(Path);
  public void Dispose(){Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(Path,true);}
 }
}
