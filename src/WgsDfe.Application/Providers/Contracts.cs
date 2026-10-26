using WgsDfe.Domain;
namespace WgsDfe.Application.Providers;
public sealed record ProviderCapabilities(string Code, IReadOnlySet<DocumentModel> Models,
 CertificateRequirement CertificateRequirement, bool SupportsNsu, bool SupportsKeyLookup,
 bool SupportsDateRange, bool IsHistorical, bool IsImplemented);
// Período é filtro do acervo/plano; não presumir filtro por data no Web Service.
public sealed record DocumentRequest(Guid OrganizationId, Guid CompanyId, DocumentModel Model,
 FiscalEnvironment Environment, ActorFilter Actor, PayloadMode PayloadMode, string LastNsu,
 string? AccessKey, DateOnly? From, DateOnly? To, Guid CorrelationId, string? CompanyTaxId=null, string? SpecificNsu=null);
public sealed record RetrievedPayload(string? AccessKey, string? Nsu, byte[] Xml, string SchemaName);
public sealed record ProviderResult(Availability Availability, string ResultCode,
 IReadOnlyCollection<RetrievedPayload> Documents, string? NextNsu, DateTime? NextAllowedAtUtc,
 int? OfficialStatusCode);
public interface IDocumentProvider
{
 ProviderCapabilities Capabilities { get; }
 bool CanHandle(DocumentRequest request);
 Task<ProviderResult> GetDocumentsAsync(DocumentRequest request, CancellationToken cancellationToken);
}
public sealed record HistoricalRequest(Guid OrganizationId, Guid CompanyId, DocumentModel Model,
 FiscalEnvironment Environment, IReadOnlyCollection<string> AccessKeys, Guid CorrelationId);
public sealed record HistoricalSubmission(string ExternalJobId, string ResultCode);
public sealed record HistoricalProgress(string ExternalJobId, JobStatus Status, string ResultCode);
public interface IHistoricalDocumentProvider
{
 ProviderCapabilities Capabilities { get; }
 Task<HistoricalSubmission> SubmitAsync(HistoricalRequest request, CancellationToken cancellationToken);
 Task<HistoricalProgress> GetStatusAsync(string externalJobId, CancellationToken cancellationToken);
 Task<ProviderResult> DownloadAsync(string externalJobId, CancellationToken cancellationToken);
}
public interface IProviderRegistry { IReadOnlyCollection<IDocumentProvider> Providers { get; } }
