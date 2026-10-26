namespace WgsDfe.Application;
public interface IOrganizationContext { Guid OrganizationId { get; } }
public interface ISecretStore
{
 Task StoreAsync(string reference, ReadOnlyMemory<byte> secret, CancellationToken cancellationToken);
 Task<byte[]> ReadAsync(string reference, CancellationToken cancellationToken);
 Task RemoveAsync(string reference, CancellationToken cancellationToken);
}
// Produção: DPAPI CurrentUser; sem fallback em texto puro.
public enum TechnicalEvent { Startup, DatabaseReady, Shutdown, StartupFailed, ConfigurationRejected, CertificateImportFailed }
public enum TechnicalLevel { Info, Warning, Error, Debug }
public interface ITechnicalLog
{
 void Write(TechnicalLevel level, TechnicalEvent eventCode, Guid correlationId);
}
