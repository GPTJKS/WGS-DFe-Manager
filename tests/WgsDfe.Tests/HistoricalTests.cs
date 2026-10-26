using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Historical;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Historical;
using WgsDfe.Infrastructure.Parsing;
using Xunit;
namespace WgsDfe.Tests;
public sealed class HistoricalTests
{
 private static string Key(int model=55)=>DfeParserTests.Key(model);
 private static string Doc(int model=55)=>DfeParserTests.Document(model).Replace("<tpAmb>2","<tpAmb>1");
 private static readonly HistoricalOptions Local=new(false,false);
 private static HistoricalOptions External(bool paid=false,int max=0)=>new(false,true,"TEST_HISTORY",paid,max);
 [Fact]public async Task ClassifiesAllFiveModelsAndDeduplicatesCanonicalKeys()
 {await using var h=await Harness.Create();string text="CHAVE\n"+string.Join('\n',new[]{55,57,62,65,67}.Select(x=>Key(x)))+"\n"+Key()+"\nBAD\n";var id=await h.Import(text);var b=Assert.Single(await h.Service.BatchesAsync());Assert.Equal(id,b.Id);Assert.Equal(7,b.Total);Assert.Equal(5,b.Valid);Assert.Equal(1,b.Invalid);Assert.Equal(1,b.Duplicates);var g=await h.Service.GroupsAsync(id);Assert.Equal(5,g.Count);Assert.All(g,x=>{Assert.Equal(2026,x.Year);Assert.Equal(9,x.Month);Assert.Equal("12345678000195",x.Issuer);Assert.Equal("Synthetic",x.Company);});}
 [Theory][InlineData(".csv",true)][InlineData(".csv",false)][InlineData(".txt",true)][InlineData(".txt",false)]
 public async Task ImportsHeaderOrSingleColumnWithoutHeader(string ext,bool header)
 {await using var h=await Harness.Create();var id=await h.Import((header?"CHAVE\n":"")+Key(),ext);Assert.Equal(Key(),Assert.Single((await h.Service.RowsAsync(id)).Rows).Key);}
 [Fact]public async Task AlphaKeysAndNormalizedPasteRemainText()
 {await using var h=await Harness.Create();var key=DfeParserTests.Key(62,tax:"12ABC34501DE35");var id=await h.Import("chave de acesso\n"+key.ToLowerInvariant()+"\n "+key+" \n");var rows=(await h.Service.RowsAsync(id)).Rows;Assert.Equal(key,rows[0].Key);Assert.Equal("DUPLICATE",rows[1].Code);}
 [Theory][InlineData("1.23456789E+43")][InlineData("=123")][InlineData("123")][InlineData("BAD")][InlineData("42260912345678000195550010000001231123456780")]
 public async Task InvalidKeyIsAResultNotWholeBatchFailure(string value)
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+value);Assert.Equal("INVALID_KEY",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);}
 [Fact]public async Task DuplicateHeaderIsRejected()
 {await using var h=await Harness.Create();await Assert.ThrowsAsync<HistoricalException>(()=>h.Import("CHAVE;CHAVE DE ACESSO\n"+Key()+";"+Key()));Assert.Empty(await h.Service.BatchesAsync());}
 [Theory][InlineData("n")][InlineData("formula")][InlineData("e")]
 public async Task XlsxNumericFormulaAndErrorCellsAreNeverRepairedAsKeys(string type)
 {await using var h=await Harness.Create();var path=h.Xlsx(type,Key());var id=await h.Service.ImportAsync(new(path,FiscalEnvironment.Production));Assert.Equal("INVALID_CELL",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);}
 [Fact]public async Task XlsxTextKeyImportsWithoutExcelRounding()
 {await using var h=await Harness.Create();var id=await h.Service.ImportAsync(new(h.Xlsx("inlineStr",Key(65)),FiscalEnvironment.Production));Assert.Equal(Key(65),Assert.Single((await h.Service.RowsAsync(id)).Rows).Key);}
 [Fact]public async Task MoreThanCompanyTenThousandLimitIsSupported()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+string.Join('\n',Enumerable.Repeat(Key(),10001)));var b=Assert.Single(await h.Service.BatchesAsync());Assert.Equal(10001,b.Total);Assert.Equal(10000,b.Duplicates);Assert.Equal(50,(await h.Service.RowsAsync(id)).Rows.Count);Assert.Single((await h.Service.RowsAsync(id,201)).Rows);}
 [Fact]public async Task OverOneHundredThousandRowsIsRejectedBeforeCreatingBatch()
 {await using var h=await Harness.Create();await Assert.ThrowsAsync<HistoricalException>(()=>h.Import("CHAVE\n"+string.Join('\n',Enumerable.Repeat(Key(),100001))));Assert.Empty(await h.Service.BatchesAsync());}
 [Fact]public async Task CancelledImportLeavesNoPartialBatch()
 {await using var h=await Harness.Create();var f=h.File("CHAVE\n"+Key());using var cts=new CancellationTokenSource();cts.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>h.Service.ImportAsync(new(f,FiscalEnvironment.Production),cts.Token));Assert.Empty(await h.Service.BatchesAsync());}
 [Fact]public async Task ExistingArchiveBypassesOfficialAndPaidProvider()
 {await using var h=await Harness.Create(paid:true);await h.Archive(Doc());var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,new(true,true,"TEST_HISTORY",true,10));Assert.Equal("LOCAL_AVAILABLE",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);Assert.Equal(0,h.Official.Calls);Assert.Equal(0,(await h.Db.HistoricalRecoveryBatches.SingleAsync()).PaidCallsReserved);}
 [Theory][InlineData(55)][InlineData(57)][InlineData(62)][InlineData(65)][InlineData(67)]
 public async Task LocalOnlyResolvesFiveModels(int model)
 {await using var h=await Harness.Create();await h.Archive(Doc(model));var id=await h.Import("CHAVE\n"+Key(model));h.Network.EnableExternalRequests=false;await h.Service.RunAsync(id,Local);Assert.Equal(Availability.Available,Assert.Single((await h.Service.RowsAsync(id)).Rows).Availability);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task EnvironmentMustBeExplicitAndDoesNotCrossCache()
 {await using var h=await Harness.Create();await h.Archive(Doc());var id=await h.Service.ImportAsync(new(h.File("CHAVE\n"+Key()),FiscalEnvironment.Homologation));await h.Service.RunAsync(id,Local);Assert.NotEqual(Availability.Available,Assert.Single((await h.Service.RowsAsync(id)).Rows).Availability);}
 [Fact]public async Task CorruptLocalArchivePreventsPaidDownload()
 {await using var h=await Harness.Create(paid:true);await h.Archive(Doc());var file=await h.Db.FiscalDocuments.SingleAsync();await System.IO.File.WriteAllTextAsync(Path.Combine(h.DataDirectory,file.FileRelativePath!),"CORRUPT");var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(true,10));Assert.Equal("LOCAL_INTEGRITY_ERROR",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task UnknownIssuerNeedsCompanyButCanBeAssignedLater()
 {await using var h=await Harness.Create();var key=DfeParserTests.Key(tax:"12345678000276");var id=await h.Import("CHAVE\n"+key);await h.Service.RunAsync(id,External());Assert.Equal("COMPANY_REQUIRED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);await h.Service.AssignUnassignedAsync(id,h.Company);Assert.Equal(h.Company,(await h.Db.HistoricalRecoveryItems.SingleAsync()).CompanyId);}
 [Fact]public async Task InactiveCompanyPreventsNetwork()
 {await using var h=await Harness.Create();var c=await h.Db.Companies.SingleAsync();c.IsActive=false;await h.Db.SaveChangesAsync();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());Assert.Equal("COMPANY_DISABLED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task NoConfiguredProviderIsAVisiblePendingResult()
 {await using var h=await Harness.Create(configured:false);var id=await h.Import("CHAVE\n"+Key(65));await h.Service.RunAsync(id,External());Assert.Equal("NO_HISTORICAL_PROVIDER",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Empty(h.Service.Providers());}
 [Fact]public async Task NetworkDisabledNeverSubmitsExternally()
 {await using var h=await Harness.Create();h.Network.EnableExternalRequests=false;var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());Assert.Equal("NETWORK_DISABLED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task OfficialSuccessNeverFallsThroughToPaidProvider()
 {await using var h=await Harness.Create(paid:true);h.Official.Document=Doc();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,new(true,true,"TEST_HISTORY",true,10));Assert.Equal("RECOVERED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(1,h.Official.Calls);Assert.Equal(0,h.External.Calls);Assert.Equal(NfeSources.SantaCatarina,h.Official.Last!.ProviderCode);}
 [Theory][InlineData(55,"SEF_SC_NFE")][InlineData(65,"SEF_SC_NFCE")][InlineData(62,"NFCOM")]
 public async Task OfficialRouteUsesModelSpecificSource(int model,string expected)
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key(model));await h.Service.RunAsync(id,new(true,false));Assert.Equal(expected,h.Official.Last!.ProviderCode);}
 [Theory][InlineData(57)][InlineData(67)]public async Task CteDoesNotInventOfficialKeyLookup(int model)
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key(model));await h.Service.RunAsync(id,new(true,false));Assert.Equal(0,h.Official.Calls);Assert.Equal("NO_OFFICIAL_KEY_PROVIDER",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);}
 [Fact]public async Task OfficialWaitNeverTriggersPaidFallback()
 {await using var h=await Harness.Create(paid:true);h.Official.Code="WAIT_UNTIL_ALLOWED";var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,new(true,true,"TEST_HISTORY",true,10));Assert.Equal("OFFICIAL_DEFERRED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task OfficialNotFoundCanFallThroughToConfiguredHistory()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,new(true,true,"TEST_HISTORY"));Assert.Equal(1,h.External.Submissions);Assert.Equal(1,h.Official.Calls);}
 [Theory][InlineData(false,0)][InlineData(false,10)][InlineData(true,0)]
 public async Task PaidCallsRequireExplicitConsentAndPositiveBudget(bool allow,int max)
 {await using var h=await Harness.Create(paid:true);var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(allow,max));Assert.Equal("PAID_NOT_ALLOWED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);Assert.Equal(0,(await h.Db.HistoricalRecoveryBatches.SingleAsync()).PaidCallsReserved);}
 [Fact]public async Task PaidBudgetIsReservedBeforeEachRemoteMethodAndPersistsAcrossResumes()
 {await using var h=await Harness.Create(paid:true);var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(true,1));Assert.Equal(1,h.External.Calls);h.Clock.Advance();await h.Service.RunAsync(id,External(true,1));Assert.Equal(1,h.External.Calls);Assert.Equal("PAID_LIMIT",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);await h.Service.RunAsync(id,External(true,3));Assert.Equal(3,h.External.Calls);Assert.Equal(3,(await h.Db.HistoricalRecoveryBatches.SingleAsync()).PaidCallsReserved);Assert.Equal(Availability.Available,Assert.Single((await h.Service.RowsAsync(id)).Rows).Availability);}
 [Fact]public async Task ExternalJobIsPolledWithoutRepeatedSubmission()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());await h.Service.RunAsync(id,External());Assert.Equal(1,h.External.Calls);h.Clock.Advance();await h.Service.RunAsync(id,External());Assert.Equal(1,h.External.Submissions);Assert.Equal(1,h.External.Polls);Assert.Equal(1,h.External.Downloads);Assert.Equal("RECOVERED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);}
 [Fact]public async Task InProgressJobWaitsForNextResume()
 {await using var h=await Harness.Create();h.External.Status=JobStatus.Running;var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());h.Clock.Advance();await h.Service.RunAsync(id,External());Assert.Equal(0,h.External.Downloads);Assert.Equal("EXTERNAL_WAITING",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);}
 [Fact]public async Task UncertainNonIdempotentSubmissionRequiresReviewNotAnotherCharge()
 {await using var h=await Harness.Create(paid:true);h.External.FailSubmit=true;var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(true,10));h.Clock.Advance();h.External.FailSubmit=false;await h.Service.RunAsync(id,External(true,10));Assert.Equal(1,h.External.Submissions);Assert.Equal("SUBMISSION_REVIEW_REQUIRED",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(1,(await h.Db.HistoricalRecoveryBatches.SingleAsync()).PaidCallsReserved);}
 [Fact]public async Task IdempotentRetryUsesTheSameStableItemIdentifier()
 {await using var h=await Harness.Create(idempotent:true);h.External.FailSubmit=true;var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());h.Clock.Advance();h.External.FailSubmit=false;await h.Service.RunAsync(id,External());Assert.Equal(2,h.External.Submissions);Assert.Equal(h.External.Requests[0].CorrelationId,h.External.Requests[1].CorrelationId);}
 [Fact]public async Task CancellationAfterSubmitKeepsExternalJobAndNeverResubmits()
 {await using var h=await Harness.Create();using var cts=new CancellationTokenSource();h.External.AfterSubmit=()=>cts.Cancel();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(),ct:cts.Token);Assert.NotNull((await h.Db.HistoricalRecoveryItems.SingleAsync()).ExternalJobId);h.External.AfterSubmit=null;h.Clock.Advance();await h.Service.RunAsync(id,External());Assert.Equal(1,h.External.Submissions);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());}
 [Fact]public async Task CancellationAfterDownloadKeepsDurablePayloadForOfflineReplay()
 {await using var h=await Harness.Create();using var cts=new CancellationTokenSource();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());h.Clock.Advance();h.External.AfterDownload=()=>cts.Cancel();await h.Service.RunAsync(id,External(),ct:cts.Token);h.Db.ChangeTracker.Clear();Assert.NotNull((await h.Db.HistoricalRecoveryItems.SingleAsync()).ResponseBytes);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());h.Network.EnableExternalRequests=false;await h.Service.RunAsync(id,Local);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(3,h.External.Calls);}
 [Theory][InlineData("key")][InlineData("env")][InlineData("xml")][InlineData("summary")][InlineData("event-only")][InlineData("company")]
 public async Task InvalidExternalPayloadIsDurableButCannotBecomeAvailable(string problem)
 {await using var h=await Harness.Create();h.External.Payload=problem switch{"key"=>Doc(65),"env"=>DfeParserTests.Document(),"xml"=>"<bad/>","event-only"=>DfeParserTests.Event().Replace("<tpAmb>2","<tpAmb>1"),"company"=>DfeParserTests.Document(tax:"12ABC34501DE35").Replace("<tpAmb>2","<tpAmb>1"),_=>$"<resNFe xmlns='{DfeParser.NfeNamespace}' versao='1.01'/>"};var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());h.Clock.Advance();await h.Service.RunAsync(id,External());Assert.Equal("PAYLOAD_INVALID",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.NotNull((await h.Db.HistoricalRecoveryItems.SingleAsync()).ResponseBytes);Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());int calls=h.External.Calls;await h.Service.RunAsync(id,External());Assert.Equal(calls,h.External.Calls);}
 [Fact]public async Task ReceiptHashTamperingPreventsReplay()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key());var item=await h.Db.HistoricalRecoveryItems.SingleAsync();item.ResponseBytes=[1,2];item.ResponseHash="BAD";await h.Db.SaveChangesAsync();await h.Service.RunAsync(id,Local);Assert.Equal("PAYLOAD_HASH_MISMATCH",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task DatabaseFailureRollsBackArchiveAndReplaysReceivedPayloadWithoutNetwork()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());await h.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER reject_history BEFORE INSERT ON FiscalDocuments BEGIN SELECT RAISE(ABORT,'synthetic'); END;");h.Clock.Advance();await Assert.ThrowsAsync<DbUpdateException>(()=>h.Service.RunAsync(id,External()));Assert.Empty(await h.Db.FiscalDocuments.ToListAsync());Assert.NotNull((await h.Db.HistoricalRecoveryItems.SingleAsync()).ResponseBytes);await h.Db.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_history;");await h.Service.RunAsync(id,Local);Assert.Single(await h.Db.FiscalDocuments.ToListAsync());Assert.Equal(3,h.External.Calls);}
 [Fact]public async Task MissingFileCanBeRestoredFromDurableResponseWithoutAnotherPaidCall()
 {await using var h=await Harness.Create(paid:true);var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External(true,3));h.Clock.Advance();await h.Service.RunAsync(id,External(true,3));var doc=await h.Db.FiscalDocuments.SingleAsync();System.IO.File.Delete(Path.Combine(h.DataDirectory,doc.FileRelativePath!));await h.Service.RunAsync(id,Local);Assert.True(System.IO.File.Exists(Path.Combine(h.DataDirectory,doc.FileRelativePath!)));Assert.Equal(3,h.External.Calls);}
 [Fact]public async Task NoXmlIsNotAProgramWideFailureIncludingNfce()
 {await using var h=await Harness.Create();h.External.Empty=true;var id=await h.Import("CHAVE\n"+Key(65)+"\n"+Key(57));await h.Service.RunAsync(id,External());h.Clock.Advance();await h.Service.RunAsync(id,External());Assert.All((await h.Service.RowsAsync(id)).Rows,x=>Assert.Equal("NOT_FOUND",x.Code));Assert.Equal(2,h.External.Submissions);}
 [Fact]public async Task CSVReportIncludesAllRowsAndNeutralizesFormulaPrefix()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n=1+1\n"+Key()+"\n"+Key());var path=Path.Combine(h.Directory,"report.csv");await h.Service.WriteReportAsync(id,path);string text=await System.IO.File.ReadAllTextAsync(path);Assert.Contains("'=1+1",text);Assert.Contains("DUPLICATE",text);Assert.Contains("INVALID_KEY",text);Assert.Contains("Linha;Ambiente;Entrada",text);}
 [Fact]public async Task ForeignTenantBatchCannotBeProcessedOrReported()
 {await using var h=await Harness.Create();await Assert.ThrowsAsync<HistoricalException>(()=>h.Service.RunAsync(Guid.NewGuid(),Local));Assert.Empty((await h.Service.RowsAsync(Guid.NewGuid())).Rows);await Assert.ThrowsAsync<HistoricalException>(()=>h.Service.WriteReportAsync(Guid.NewGuid(),Path.Combine(h.Directory,"bad.csv")));}
 [Fact]public async Task ConcurrentHistoryRunsSerializeAndSecondDoesNotResubmit()
 {await using var h=await Harness.Create();var id=await h.Import("CHAVE\n"+Key());await Task.WhenAll(h.Service.RunAsync(id,External()),h.Service.RunAsync(id,External()));Assert.Equal(1,h.External.Submissions);}

 [Theory][InlineData(false)][InlineData(true)]public async Task PreviouslyAvailableItemIsInvalidatedWhenFileIsMissingOrCorrupt(bool corrupt)
 {await using var h=await Harness.Create();await h.Archive(Doc());var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,Local);var doc=await h.Db.FiscalDocuments.SingleAsync();var path=Path.Combine(h.DataDirectory,doc.FileRelativePath!);if(corrupt)await System.IO.File.WriteAllTextAsync(path,"CORRUPT");else System.IO.File.Delete(path);await h.Service.RunAsync(id,Local);Assert.NotEqual(Availability.Available,Assert.Single((await h.Service.RowsAsync(id)).Rows).Availability);Assert.Equal(0,Assert.Single(await h.Service.BatchesAsync()).Available);Assert.Equal(0,h.External.Calls);}
 [Fact]public async Task HistoricalCompanyDeletionPreservesRecoveryItems()
 {await using var h=await Harness.Create();await h.Import("CHAVE\n"+Key());var company=await h.Db.Companies.SingleAsync();await new WgsDfe.Infrastructure.Companies.CompanyService(h.Db).DeleteAsync(company.Id,company.Revision,Guid.NewGuid());Assert.Empty(await h.Db.Companies.ToListAsync());Assert.NotEmpty(await h.Db.HistoricalRecoveryItems.ToListAsync());}

 [Fact]public async Task MigrationPreservesEarlierDataAndAddsTenantGuardedHistoryTables()
 {
  string path=Path.Combine(Path.GetTempPath(),"wgs-history-upgrade-"+Guid.NewGuid().ToString("N")+".db");Guid org=Guid.NewGuid();
  try
  {await using var db=DatabaseFactory.Create(path,org);await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261016232805_CteDistributionInbox");db.Organizations.Add(new(){Id=org,Name="Synthetic"});var company=new Company{OrganizationId=org,Cnpj="12345678000195",LegalName="Preserved",State="SC"};await db.SaveChangesAsync();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Companies (Id, OrganizationId, CreatedAtUtc, UpdatedAtUtc, Revision, LegalName, Cnpj, State, SearchText, IsActive, EnabledDocuments) VALUES ({company.Id}, {org}, {DateTime.UtcNow}, {DateTime.UtcNow}, 1, {company.LegalName}, {company.Cnpj}, {company.State}, {"PRESERVED"}, 1, {(int)EnabledDocuments.All})");await db.Database.MigrateAsync();Assert.Equal("Preserved",(await db.Companies.SingleAsync()).LegalName);Assert.Equal(9,(await db.Database.GetAppliedMigrationsAsync()).Count());Assert.Empty(await db.HistoricalRecoveryItems.ToListAsync());Assert.False(db.Database.HasPendingModelChanges());}
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
 }
 [Fact]public async Task RestartMarksHistoryJobInterruptedAndPreservesExternalReservation()
 {
  string path=Path.Combine(Path.GetTempPath(),"wgs-history-restart-"+Guid.NewGuid().ToString("N"));Guid org=Guid.NewGuid(),job=Guid.NewGuid(),batch=Guid.NewGuid();var options=new FoundationOptions{Application=new(){OrganizationId=org,OrganizationName="Synthetic",DataDirectory=path}};
  try
  {
   await using(var runtime=new FoundationRuntime(options)){await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();db.ProcessingJobs.Add(new(){Id=job,OrganizationId=org,Kind=JobKind.HistoricalRecovery,Status=JobStatus.Running});db.HistoricalRecoveryBatches.Add(new(){Id=batch,OrganizationId=org,JobId=job,Environment=FiscalEnvironment.Production,FileName="keys.csv",PaidCallsReserved=1});db.HistoricalRecoveryItems.Add(new(){OrganizationId=org,BatchId=batch,SourceLine=1,Input=Key(),AccessKey=Key(),Model=DocumentModel.NFe,State=HistoricalItemState.Submitting,ProviderCode="TEST_HISTORY",ResultCode="EXTERNAL_SUBMITTING"});await db.SaveChangesAsync();}
   await using(var runtime=new FoundationRuntime(options)){await runtime.InitializeAsync();await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();Assert.Equal(JobStatus.Failed,(await db.ProcessingJobs.SingleAsync()).Status);Assert.Equal(HistoricalItemState.Submitting,(await db.HistoricalRecoveryItems.SingleAsync()).State);Assert.Equal(1,(await db.HistoricalRecoveryBatches.SingleAsync()).PaidCallsReserved);Assert.False(runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests);Assert.Empty(scope.ServiceProvider.GetRequiredService<IHistoricalRecoveryService>().Providers());}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(path,true);}
 }

 [Fact]public async Task HistoricalAdapterCanBeRegisteredThroughTheCompositionRoot()
 {string path=Path.Combine(Path.GetTempPath(),"wgs-history-registration-"+Guid.NewGuid().ToString("N"));try{await using var runtime=new FoundationRuntime(new(){Application=new(){OrganizationId=Guid.NewGuid(),OrganizationName="Synthetic",DataDirectory=path}},services=>services.AddSingleton(new HistoricalProviderRegistration(new Provider(),IsPaid:true)));await using var scope=runtime.Services.CreateAsyncScope();var providers=scope.ServiceProvider.GetRequiredService<IHistoricalRecoveryService>().Providers();Assert.Equal("TEST_HISTORY",Assert.Single(providers).Code);Assert.True(providers[0].IsPaid);}finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(path,true);}}

 [Fact]public async Task ArbitraryAdapterErrorCodeCannotLeakIntoTheRowOrCsvReport()
 {await using var h=await Harness.Create();h.External.CustomError="SENSITIVE_EXTERNAL_MESSAGE";var id=await h.Import("CHAVE\n"+Key());await h.Service.RunAsync(id,External());Assert.Equal("EXTERNAL_ERROR",Assert.Single((await h.Service.RowsAsync(id)).Rows).Code);string path=Path.Combine(h.Directory,"safe.csv");await h.Service.WriteReportAsync(id,path);Assert.DoesNotContain("SENSITIVE",await System.IO.File.ReadAllTextAsync(path));}
 private sealed class Clock:TimeProvider{private DateTimeOffset now=new(2026,10,18,16,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance()=>now=now.AddMinutes(2);}
 private sealed class Provider:IHistoricalDocumentProvider
 {
  public ProviderCapabilities Capabilities{get;}=new("TEST_HISTORY",Enum.GetValues<DocumentModel>().ToHashSet(),CertificateRequirement.None,false,true,false,true,true);
  public int Submissions,Polls,Downloads;public int Calls=>Submissions+Polls+Downloads;public bool FailSubmit,Empty;public string? CustomError,Payload;public Action? AfterSubmit,AfterDownload;public JobStatus Status=JobStatus.Completed;public List<HistoricalRequest> Requests{get;}=[];
  public Task<HistoricalSubmission> SubmitAsync(HistoricalRequest r,CancellationToken ct){Submissions++;Requests.Add(r);if(CustomError is not null)throw new HistoricalException(CustomError);if(FailSubmit)throw new InvalidOperationException("SENSITIVE");AfterSubmit?.Invoke();return Task.FromResult(new HistoricalSubmission("job-"+r.CorrelationId,"arbitrary ignored"));}
  public Task<WgsDfe.Application.Providers.HistoricalProgress> GetStatusAsync(string id,CancellationToken ct){Polls++;return Task.FromResult(new WgsDfe.Application.Providers.HistoricalProgress(id,Status,"ignored"));}
  public Task<ProviderResult> DownloadAsync(string id,CancellationToken ct){Downloads++;var request=Requests.Last(x=>"job-"+x.CorrelationId==id);var result=new ProviderResult(Empty?Availability.NotFoundInSource:Availability.Available,"ignored",Empty?[]:[new(null,null,Encoding.UTF8.GetBytes(Payload??Doc((int)request.Model)),"external.xml")],null,null,null);AfterDownload?.Invoke();return Task.FromResult(result);}
 }
 private sealed class Official(ArchiveService archive,string directory):INfeSyncService
 {
  public int Calls;public NfeSyncRequest? Last;public string Code="NFE_SYNCHRONIZED";public string? Document;
  public Task<IReadOnlyList<NfeCompany>> CompaniesAsync(CancellationToken ct=default,string source=NfeSources.SantaCatarina)=>Task.FromResult<IReadOnlyList<NfeCompany>>([]);
  public Task<IReadOnlyList<NfeSyncRow>> StatesAsync(CancellationToken ct=default,string? source=null)=>Task.FromResult<IReadOnlyList<NfeSyncRow>>([]);
  public async Task<NfeSyncResult> SynchronizeAsync(NfeSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default){Calls++;Last=request;if(Document is not null){string f=Path.Combine(directory,"official.xml");await System.IO.File.WriteAllTextAsync(f,Document,ct);await archive.ImportAsync(new([f]),ct:ct);}return new(Guid.NewGuid(),[new(request.Companies.Single(),"Synthetic",request.ProviderCode,Code,117,1,Document is null?0:1,0,"0",null)],false);}
 }
 private sealed class Harness:IAsyncDisposable
 {
  public string Directory{get;}=Path.Combine(Path.GetTempPath(),"wgs-history-"+Guid.NewGuid().ToString("N"));public string DataDirectory=>Path.Combine(Directory,"data");public Guid Org{get;}=Guid.NewGuid();public Guid Company{get;private set;}public WgsDbContext Db{get;private set;}=null!;public HistoricalRecoveryService Service{get;private set;}=null!;public ArchiveService Acervo{get;private set;}=null!;public Official Official{get;private set;}=null!;public Provider External{get;}=new();public Clock Clock{get;}=new();public ProviderOptions Network{get;}=new(){EnableExternalRequests=true};private readonly ArchiveGate archiveGate=new();private readonly HistoricalGate gate=new();
  public static async Task<Harness> Create(bool paid=false,bool configured=true,bool idempotent=false)
  {var h=new Harness();System.IO.Directory.CreateDirectory(h.Directory);h.Db=DatabaseFactory.Create(Path.Combine(h.Directory,"db.sqlite"),h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Synthetic"});var c=new Company{OrganizationId=h.Org,Cnpj="12345678000195",LegalName="Synthetic",State="SC"};h.Company=c.Id;h.Db.Companies.Add(c);await h.Db.SaveChangesAsync();var parser=new DfeParser();h.Acervo=new(h.Db,parser,new ArchiveFileStore(new(h.DataDirectory,h.Org)),h.archiveGate);h.Official=new(h.Acervo,h.Directory);h.Service=new(h.Db,h.Acervo,h.archiveGate,h.gate,parser,h.Official,configured?[new(h.External,paid,idempotent)]:[],h.Network,h.Clock);return h;}
  public string File(string text,string ext=".csv"){string f=Path.Combine(Directory,Guid.NewGuid().ToString("N")+ext);System.IO.File.WriteAllText(f,text);return f;}
  public Task<Guid> Import(string text,string ext=".csv")=>Service.ImportAsync(new(File(text,ext),FiscalEnvironment.Production));
  public async Task Archive(string xml){string path=File(xml,".xml");await Acervo.ImportAsync(new([path]));Db.ChangeTracker.Clear();}
  public string Xlsx(string type,string key)
  {string path=Path.Combine(Directory,Guid.NewGuid().ToString("N")+".xlsx");using var zip=ZipFile.Open(path,ZipArchiveMode.Create);void Part(string name,string xml){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(xml);}Part("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Keys' sheetId='1' r:id='r1'/></sheets></workbook>");Part("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/></Relationships>");string cell=type=="inlineStr"?$"<c r='A2' t='inlineStr'><is><t>{key}</t></is></c>":type=="formula"?$"<c r='A2' t='str'><f>CONCAT(A1)</f><v>{key}</v></c>":$"<c r='A2' t='{type}'><v>{key}</v></c>";Part("xl/worksheets/sheet1.xml","<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='inlineStr'><is><t>CHAVE</t></is></c></row><row r='2'>"+cell+"</row></sheetData></worksheet>");return path;}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();archiveGate.Dispose();gate.Dispose();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();System.IO.Directory.Delete(Directory,true);}
 }
}
