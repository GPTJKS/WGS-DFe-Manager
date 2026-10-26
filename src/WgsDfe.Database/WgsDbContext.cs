using Microsoft.EntityFrameworkCore;
using WgsDfe.Domain;
namespace WgsDfe.Database;
public sealed class WgsDbContext(DbContextOptions<WgsDbContext> options, Guid organizationId) : DbContext(options)
{
 public DbSet<HistoricalRecoveryBatch> HistoricalRecoveryBatches=>Set<HistoricalRecoveryBatch>();
 public DbSet<HistoricalRecoveryItem> HistoricalRecoveryItems=>Set<HistoricalRecoveryItem>();
 public DbSet<SyncSchedule> SyncSchedules=>Set<SyncSchedule>();
 public DbSet<SyncScheduleRun> SyncScheduleRuns=>Set<SyncScheduleRun>();
 public Guid OrganizationId { get; } = organizationId;
 public DbSet<CteDistributionReceipt> CteDistributionReceipts=>Set<CteDistributionReceipt>();
 public DbSet<CteDistributionItem> CteDistributionItems=>Set<CteDistributionItem>();
 public DbSet<NfeDistributionReceipt> NfeDistributionReceipts=>Set<NfeDistributionReceipt>();
 public DbSet<Organization> Organizations => Set<Organization>();
 public DbSet<Company> Companies => Set<Company>();
 public DbSet<Certificate> Certificates => Set<Certificate>();
 public DbSet<CertificateAssociation> CertificateAssociations => Set<CertificateAssociation>();
 public DbSet<DocumentProvider> DocumentProviders => Set<DocumentProvider>();
 public DbSet<ArchiveFile> ArchiveFiles => Set<ArchiveFile>();
 public DbSet<FiscalDocument> FiscalDocuments => Set<FiscalDocument>();
 public DbSet<FiscalDocumentCompany> FiscalDocumentCompanies => Set<FiscalDocumentCompany>();
 public DbSet<FiscalDocumentEvent> FiscalDocumentEvents => Set<FiscalDocumentEvent>();
 public DbSet<DocumentSyncState> DocumentSyncStates => Set<DocumentSyncState>();
 public DbSet<ProcessingJob> ProcessingJobs => Set<ProcessingJob>();
 public DbSet<ProcessingJobItem> ProcessingJobItems => Set<ProcessingJobItem>();
 public DbSet<KnownDocumentKey> KnownDocumentKeys => Set<KnownDocumentKey>();
 public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
 public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
 protected override void OnModelCreating(ModelBuilder b)
 {
  b.Entity<Organization>().HasKey(x=>x.Id);
  b.Entity<Organization>().Property(x=>x.Name).HasMaxLength(200).IsRequired();
  b.Entity<Organization>().HasQueryFilter(x=>x.Id==OrganizationId);
  Tenant<SyncSchedule>(b);Tenant<SyncScheduleRun>(b);
  b.Entity<SyncSchedule>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<SyncSchedule>().Property(x=>x.ProviderCode).HasMaxLength(40).IsRequired();
  b.Entity<SyncSchedule>().Property(x=>x.TimeZoneId).HasMaxLength(100).IsRequired();
  b.Entity<SyncSchedule>().HasIndex(x=>new{x.OrganizationId,x.Enabled,x.NextRunAtUtc});
  b.Entity<SyncSchedule>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.ProviderCode,x.Environment,x.Hour,x.Minute,x.TimeZoneId}).IsUnique();
  b.Entity<SyncSchedule>().ToTable(t=>t.HasCheckConstraint("CK_Schedule_Time","Hour >= 0 AND Hour <= 23 AND Minute >= 0 AND Minute <= 59 AND MaxPages >= 1 AND MaxPages <= 100"));
  b.Entity<SyncScheduleRun>().HasOne<SyncSchedule>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.ScheduleId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<SyncScheduleRun>().HasOne<ProcessingJob>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.JobId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<SyncScheduleRun>().HasIndex(x=>new{x.OrganizationId,x.StartedAtUtc});
  Tenant<HistoricalRecoveryBatch>(b);Tenant<HistoricalRecoveryItem>(b);
  b.Entity<HistoricalRecoveryBatch>().HasOne<ProcessingJob>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.JobId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<HistoricalRecoveryBatch>().HasIndex(x=>new{x.OrganizationId,x.JobId}).IsUnique();
  b.Entity<HistoricalRecoveryItem>().HasOne<HistoricalRecoveryBatch>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.BatchId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<HistoricalRecoveryItem>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<HistoricalRecoveryItem>().HasIndex(x=>new{x.OrganizationId,x.BatchId,x.SourceLine}).IsUnique();
  b.Entity<HistoricalRecoveryItem>().HasIndex(x=>new{x.OrganizationId,x.BatchId,x.AccessKey});
  b.Entity<HistoricalRecoveryItem>().Property(x=>x.AccessKey).HasMaxLength(44);
  b.Entity<HistoricalRecoveryItem>().Property(x=>x.Input).HasMaxLength(200);
  b.Entity<HistoricalRecoveryItem>().ToTable(t=>t.HasCheckConstraint("CK_HistoricalItem_KeyLength","AccessKey IS NULL OR length(AccessKey) = 44"));
  Tenant<CteDistributionReceipt>(b);Tenant<CteDistributionItem>(b);
  b.Entity<CteDistributionReceipt>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CteDistributionReceipt>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.Environment,x.Applied});
  b.Entity<CteDistributionItem>().HasOne<CteDistributionReceipt>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.ReceiptId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CteDistributionItem>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CteDistributionItem>().HasIndex(x=>new{x.OrganizationId,x.ReceiptId,x.Nsu}).IsUnique();
  b.Entity<CteDistributionItem>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.Environment,x.Archived});
  Tenant<NfeDistributionReceipt>(b);
  b.Entity<NfeDistributionReceipt>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<NfeDistributionReceipt>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.ProviderCode,x.Applied});
  Tenant<ArchiveFile>(b); Tenant<Company>(b); Tenant<Certificate>(b); Tenant<CertificateAssociation>(b);
  Tenant<DocumentProvider>(b); Tenant<FiscalDocument>(b); Tenant<FiscalDocumentCompany>(b);
  Tenant<FiscalDocumentEvent>(b); Tenant<DocumentSyncState>(b); Tenant<ProcessingJob>(b);
  Tenant<ProcessingJobItem>(b); Tenant<KnownDocumentKey>(b); Tenant<AuditLog>(b); Tenant<ApplicationSetting>(b);
  b.Entity<Company>().HasQueryFilter(x=>x.OrganizationId==OrganizationId&&!x.IsDeleted);
  b.Entity<Company>().HasIndex(x=>new{x.OrganizationId,x.Cnpj}).IsUnique();
  b.Entity<Company>().Property(x=>x.Cnpj).HasMaxLength(14).IsRequired();
  b.Entity<Company>().Property(x=>x.LegalName).HasMaxLength(300).IsRequired();
  b.Entity<Company>().Property(x=>x.SearchText).HasMaxLength(1000).IsRequired();
  b.Entity<Company>().ToTable(t=>t.HasCheckConstraint("CK_Company_CnpjLength","length(Cnpj) = 14"));
  b.Entity<DocumentProvider>().HasIndex(x=>new{x.OrganizationId,x.Code}).IsUnique();
  b.Entity<CertificateAssociation>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.ProviderCode}).IsUnique();
  b.Entity<CertificateAssociation>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<CertificateAssociation>().HasOne<Certificate>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CertificateId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ArchiveFile>().HasIndex(x=>new{x.OrganizationId,x.Hash}).IsUnique();
  b.Entity<ArchiveFile>().HasOne<FiscalDocument>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.FiscalDocumentId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ArchiveFile>().HasOne<FiscalDocumentEvent>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.FiscalDocumentEventId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ArchiveFile>().ToTable(t=>t.HasCheckConstraint("CK_ArchiveFile_Owner","(FiscalDocumentId IS NOT NULL AND FiscalDocumentEventId IS NULL) OR (FiscalDocumentId IS NULL AND FiscalDocumentEventId IS NOT NULL)"));
  b.Entity<FiscalDocument>().HasIndex(x=>new{x.OrganizationId,x.IssueLocalDate,x.TotalCents});
  b.Entity<FiscalDocument>().HasIndex(x=>new{x.OrganizationId,x.Environment,x.AccessKey}).IsUnique();
  b.Entity<FiscalDocument>().Property(x=>x.AccessKey).HasMaxLength(44).IsRequired();
  b.Entity<FiscalDocument>().Property(x=>x.TotalValue).HasConversion<string>();
  b.Entity<FiscalDocument>().HasIndex(x=>new{x.OrganizationId,x.IssueDateUtc,x.Model});
  b.Entity<FiscalDocument>().ToTable(t=>t.HasCheckConstraint("CK_Document_KeyLength","length(AccessKey) = 44"));
  b.Entity<FiscalDocumentCompany>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.FiscalDocumentId}).IsUnique();
  b.Entity<FiscalDocumentCompany>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<FiscalDocumentCompany>().HasOne<FiscalDocument>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.FiscalDocumentId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<FiscalDocumentEvent>().HasIndex(x=>new{x.OrganizationId,x.Environment,x.AccessKey,x.EventTypeCode,x.SequenceNumber}).IsUnique();
  b.Entity<FiscalDocumentEvent>().HasOne<FiscalDocument>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.FiscalDocumentId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<DocumentSyncState>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.ProviderCode,x.Model,x.Environment,x.Actor,x.PayloadMode}).IsUnique();
  b.Entity<DocumentSyncState>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ProcessingJobItem>().HasOne<ProcessingJob>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.JobId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ProcessingJobItem>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<ProcessingJobItem>().HasIndex(x=>new{x.OrganizationId,x.JobId,x.ItemReference}).IsUnique();
  b.Entity<KnownDocumentKey>().HasOne<Company>().WithMany().HasForeignKey(x=>new{x.OrganizationId,x.CompanyId}).HasPrincipalKey(x=>new{x.OrganizationId,x.Id}).OnDelete(DeleteBehavior.Restrict);
  b.Entity<KnownDocumentKey>().HasIndex(x=>new{x.OrganizationId,x.CompanyId,x.Environment,x.AccessKey}).IsUnique();
  b.Entity<ApplicationSetting>().HasIndex(x=>new{x.OrganizationId,x.Key}).IsUnique();
  b.Entity<AuditLog>().HasIndex(x=>new{x.OrganizationId,x.CreatedAtUtc});
 }
 private void Tenant<T>(ModelBuilder b) where T:OrganizationEntity
 {
  // Cada entidade é tabela independente; base compartilhada não é hierarquia TPH.
  var e=b.Entity<T>(); e.HasBaseType((Type?)null); e.HasKey(x=>x.Id);
  e.HasAlternateKey(x=>new{x.OrganizationId,x.Id});
  e.Property(x=>x.Revision).IsConcurrencyToken();
  e.HasOne<Organization>().WithMany().HasForeignKey(x=>x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
  e.HasQueryFilter(x=>x.OrganizationId==OrganizationId);
 }
 private void GuardChanges()
 {
  if(OrganizationId==Guid.Empty) throw new InvalidOperationException("Organização não configurada.");
  foreach(var entry in ChangeTracker.Entries<Organization>().Where(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
   if(entry.Entity.Id!=OrganizationId || (entry.State!=EntityState.Added && entry.OriginalValues.GetValue<Guid>(nameof(Organization.Id))!=OrganizationId))
    throw new InvalidOperationException("Organização divergente.");
  foreach(var entry in ChangeTracker.Entries<OrganizationEntity>().Where(e=>e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
  {
   if(entry.Entity.OrganizationId!=OrganizationId || (entry.State!=EntityState.Added && entry.OriginalValues.GetValue<Guid>(nameof(OrganizationEntity.OrganizationId))!=OrganizationId))
    throw new InvalidOperationException("Entidade pertence a outra organização.");
   if(entry.State==EntityState.Added) entry.Entity.Revision=1;
   if(entry.State==EntityState.Modified)
   {
    entry.Entity.Revision=checked(entry.OriginalValues.GetValue<long>(nameof(OrganizationEntity.Revision))+1);
    entry.Entity.UpdatedAtUtc=DateTime.UtcNow;
   }
  }
 }
 public override int SaveChanges(bool acceptAllChangesOnSuccess){GuardChanges();return base.SaveChanges(acceptAllChangesOnSuccess);}
 public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,CancellationToken cancellationToken=default)
 {GuardChanges();return base.SaveChangesAsync(acceptAllChangesOnSuccess,cancellationToken);}
}
