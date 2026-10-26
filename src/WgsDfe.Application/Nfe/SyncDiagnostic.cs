namespace WgsDfe.Application.Nfe;
public sealed record SyncDiagnostic(Guid CorrelationId,string Source,string CompanyToken,int Batch,string PreviousNsu,string NextNsu,int? CStat,long ElapsedMilliseconds,string Decision,int XmlRecords,int Summaries);
public interface ISyncDiagnostic { void Write(SyncDiagnostic entry); }
