namespace WgsDfe.Domain;
public sealed class Organization
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public string Name { get; set; } = "";
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
public abstract class OrganizationEntity
{
 public Guid Id { get; set; } = Guid.NewGuid();
 public Guid OrganizationId { get; set; }
 public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
 public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
 public long Revision { get; set; }
}
public sealed class Company : OrganizationEntity
{
 public bool IsDeleted { get; set; }
 public string LegalName { get; set; } = "";
 public string SearchText { get; set; } = "";
 public string? TradeName { get; set; }
 // String: CNPJ alfanumérico, nunca long ou decimal.
 public string Cnpj { get; set; } = "";
 public string? StateRegistration { get; set; }
 public string State { get; set; } = "SC";
 public string? City { get; set; }
 public bool IsActive { get; set; } = true;
 public EnabledDocuments EnabledDocuments { get; set; } = EnabledDocuments.All;
 public string? StorageRelativePath { get; set; }
 public string? Notes { get; set; }
 public DateTime? LastSynchronizationAtUtc { get; set; }
 public string? ConnectionStatusCode { get; set; }
 public string? AccountantLinkStatusCode { get; set; }
}
public sealed class Certificate : OrganizationEntity
{
 public CertificateUse Use { get; set; }
 public string Label { get; set; } = "";
 public string Subject { get; set; } = "";
 public string? HolderTaxId { get; set; }
 public string Thumbprint { get; set; } = "";
 public DateTime ValidFromUtc { get; set; }
 public DateTime ValidToUtc { get; set; }
 // Referência ao cofre/Windows Store. Sem PFX ou senha neste banco.
 public string SecureStoreReference { get; set; } = "";
}
public sealed class CertificateAssociation : OrganizationEntity
{
 public Guid CompanyId { get; set; }
 public Guid CertificateId { get; set; }
 public string ProviderCode { get; set; } = "";
}
public sealed class DocumentProvider : OrganizationEntity
{
 public string Code { get; set; } = "";
 public string DisplayName { get; set; } = "";
 public bool IsEnabled { get; set; }
 public int Priority { get; set; }
 public CertificateRequirement CertificateRequirement { get; set; }
}
public sealed class FiscalDocument : OrganizationEntity
{
 public string SearchText { get; set; } = "";
 public string? IssueDateOriginal { get; set; }
 public string? IssueLocalDate { get; set; }
 public long? TotalCents { get; set; }
 public DocumentStatus DeclaredStatus { get; set; }
 public int? ResponseCode { get; set; }
 public string AccessKey { get; set; } = "";
 public DocumentModel Model { get; set; }
 public FiscalEnvironment Environment { get; set; }
 public string? IssuerTaxId { get; set; }
 public string? RecipientTaxId { get; set; }
 public string? IssuerName { get; set; }
 public string? RecipientName { get; set; }
 public DateTime? IssueDateUtc { get; set; }
 public int? ReferenceYear { get; set; }
 public int? ReferenceMonth { get; set; }
 public string? Series { get; set; }
 public string? DocumentNumber { get; set; }
 public decimal? TotalValue { get; set; }
 public DocumentStatus Status { get; set; }
 public string? ProtocolNumber { get; set; }
 public DateTime? AuthorizationDateUtc { get; set; }
 public DateTime? CancellationDateUtc { get; set; }
 public string FileRelativePath { get; set; } = "";
 public string XmlHash { get; set; } = "";
 public string SourceProviderCode { get; set; } = "";
 public DateTime RetrievedAtUtc { get; set; }
}
// Um único XML pode pertencer ao acervo de dois clientes (emitente/destinatário).
public sealed class FiscalDocumentCompany : OrganizationEntity
{
 public Guid CompanyId { get; set; }
 public Guid FiscalDocumentId { get; set; }
 public ActorFilter Role { get; set; }
 public string Participation {get;set;}="";
}
public sealed class FiscalDocumentEvent : OrganizationEntity
{
 public DocumentModel Model { get; set; }
 public int? ResponseCode { get; set; }
 public DateTime? ReceiptDateUtc { get; set; }
 public string? EventDateOriginal { get; set; }
 public Guid? FiscalDocumentId { get; set; }
 // Evento pode chegar antes da nota; vinculação posterior pela chave.
 public string AccessKey { get; set; } = "";
 public FiscalEnvironment Environment { get; set; }
 public string EventTypeCode { get; set; } = "";
 public int SequenceNumber { get; set; }
 public string? ProtocolNumber { get; set; }
 public DateTime? EventDateUtc { get; set; }
 public string FileRelativePath { get; set; } = "";
 public string XmlHash { get; set; } = "";
 public string SourceProviderCode { get; set; } = "";
}
public sealed class DocumentSyncState : OrganizationEntity
{
 public Guid CompanyId { get; set; }
 public string ProviderCode { get; set; } = "";
 public DocumentModel Model { get; set; }
 public FiscalEnvironment Environment { get; set; }
 public ActorFilter Actor { get; set; }
 public PayloadMode PayloadMode { get; set; }
 public string LastNsu { get; set; } = "0";
 public DateTime? LastSuccessAtUtc { get; set; }
 public DateTime? NextAllowedAtUtc { get; set; }
 public int? LastResponseCode { get; set; }
 public void Advance(string nsu, DateTime committedAtUtc)
 {
  var next=Nsu.Parse(nsu); var current=Nsu.Parse(LastNsu);
  if (next.Value < current.Value) throw new InvalidOperationException("O NSU não pode retroceder.");
  LastNsu=next.ToString(); LastSuccessAtUtc=committedAtUtc;
 }
}
public sealed class ProcessingJob : OrganizationEntity
{
 public JobKind Kind { get; set; }
 public JobStatus Status { get; set; }
 public DateTime? StartedAtUtc { get; set; }
 public DateTime? CompletedAtUtc { get; set; }
 public int TotalItems { get; set; }
 public int ProcessedItems { get; set; }
 public string? LastErrorCode { get; set; }
}
public sealed class ProcessingJobItem : OrganizationEntity
{
 public Guid JobId { get; set; }
 public Guid? CompanyId { get; set; }
 public string ItemReference { get; set; } = "";
 public DocumentModel? Model { get; set; }
 public Availability Availability { get; set; }
 public JobStatus Status { get; set; }
 public string? ProviderCode { get; set; }
 public string? ErrorCode { get; set; }
 public int AttemptCount { get; set; }
}
public sealed class KnownDocumentKey : OrganizationEntity
{
 public Guid CompanyId { get; set; }
 public string AccessKey { get; set; } = "";
 public FiscalEnvironment Environment { get; set; }
 public Availability Availability { get; set; }
 public string Source { get; set; } = "";
}
public sealed class AuditLog : OrganizationEntity
{
 public AuditAction Action { get; set; }
 public Guid? EntityId { get; set; }
 public Guid CorrelationId { get; set; }
 // Sem texto arbitrário, XML, tokens ou exception.Message.
 public string ResultCode { get; set; } = "";
}
public sealed class ApplicationSetting : OrganizationEntity
{
 public string Key { get; set; } = "";
 // Apenas configurações não secretas. Cofre separado na Fase 3.
 public string Value { get; set; } = "";
}

// Cada variante guarda bytes imutáveis; chave identifica documento, hash identifica arquivo.
public sealed class ArchiveFile : OrganizationEntity
{
 public Guid? FiscalDocumentId { get; set; }
 public Guid? FiscalDocumentEventId { get; set; }
 public string Hash { get; set; } = "";
 public string RelativePath { get; set; } = "";
 public long ByteLength { get; set; }
 public bool IsConflict { get; set; }
 public int? ResponseCode { get; set; }
}

// Durable inbox: received SOAP is retained until the entire page and cursor commit together.
public sealed class NfeDistributionReceipt : OrganizationEntity
{
 public Guid CompanyId {get;set;}
 public string ProviderCode {get;set;}="";
 public FiscalEnvironment Environment {get;set;}
 public ActorFilter Actor {get;set;}
 public PayloadMode PayloadMode {get;set;}
 public string? AccessKey {get;set;}
 public string RequestedNsu {get;set;}="0";
 public byte[] ResponseBytes {get;set;}=Array.Empty<byte>();
 public string ResponseHash {get;set;}="";
 public bool Applied {get;set;}
 public int? ResponseCode {get;set;}
 public string? ResultCode {get;set;}
}

public sealed class CteDistributionReceipt : OrganizationEntity
{
 public Guid CompanyId{get;set;}
 public FiscalEnvironment Environment{get;set;}
 public string RequestedNsu{get;set;}="0";
 public string? SpecificNsu{get;set;}
 public byte[] ResponseBytes{get;set;}=Array.Empty<byte>();
 public string ResponseHash{get;set;}="";
 public bool Applied{get;set;}
 public int? ResponseCode{get;set;}
}
public sealed class CteDistributionItem : OrganizationEntity
{
 public Guid ReceiptId{get;set;}
 public Guid CompanyId{get;set;}
 public FiscalEnvironment Environment{get;set;}
 public string Nsu{get;set;}="0";
 public string Schema{get;set;}="";
 public string? AccessKey{get;set;}
 public int? Model{get;set;}
 public byte[] XmlBytes{get;set;}=Array.Empty<byte>();
 public string XmlHash{get;set;}="";
 public bool Archived{get;set;}
 public string ResultCode{get;set;}="CTE_LAYOUT_RETAINED";
}

public enum HistoricalItemState { Pending, Submitting, Waiting, Downloading, PayloadReceived, Available, NotFound, NeedsReview, Invalid, Duplicate }
public sealed class HistoricalRecoveryBatch : OrganizationEntity
{
 public Guid JobId{get;set;}
 public string FileName{get;set;}="";
 public FiscalEnvironment Environment{get;set;}
 public int PaidCallsReserved{get;set;}
}
public sealed class HistoricalRecoveryItem : OrganizationEntity
{
 public Guid BatchId{get;set;}
 public Guid? CompanyId{get;set;}
 public int SourceLine{get;set;}
 public string Input{get;set;}="";
 public string? AccessKey{get;set;}
 public DocumentModel? Model{get;set;}
 public string? IssuerTaxId{get;set;}
 public int? Year{get;set;}
 public int? Month{get;set;}
 public HistoricalItemState State{get;set;}
 public Availability Availability{get;set;}
 public string ResultCode{get;set;}="READY";
 public string? ProviderCode{get;set;}
 public string? ExternalJobId{get;set;}
 public int Attempts{get;set;}
 public DateTime? NextAllowedAtUtc{get;set;}
 // Durable, bounded JSON of returned fiscal payloads; never credentials or arbitrary exceptions.
 public byte[]? ResponseBytes{get;set;}
 public string? ResponseHash{get;set;}
}
