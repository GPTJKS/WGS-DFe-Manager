using WgsDfe.Domain;
namespace WgsDfe.Application.Companies;
public sealed record CompanyInput(string LegalName,string? TradeName,string Cnpj,string? StateRegistration,
 string State,string? City,bool IsActive,EnabledDocuments EnabledDocuments,string? StorageRelativePath,string? Notes);
public sealed record CompanyRow(Guid Id,long Revision,CompanyInput Data,DateTime? LastSynchronizationAtUtc,
 string? ConnectionStatusCode,string? AccountantLinkStatusCode)
{
 public string LegalName=>Data.LegalName;
 public string TradeName=>Data.TradeName??"";
 public string Cnpj=>WgsDfe.Domain.Cnpj.Format(Data.Cnpj);
 public string State=>Data.State;
 public string City=>Data.City??"";
 public string Status=>Data.IsActive?"Ativa":"Inativa";
 public string Connection=>ConnectionStatusCode is null?"Não testada":ConnectionStatusCode.StartsWith("CTE_",StringComparison.Ordinal)?WgsDfe.Application.Cte.CteMessages.Describe(ConnectionStatusCode):ConnectionStatusCode.StartsWith("NFE_",StringComparison.Ordinal)?WgsDfe.Application.Nfe.NfeMessages.Describe(ConnectionStatusCode):ConnectionStatusCode;
 public string AccountantLink=>AccountantLinkStatusCode switch{"SEF_SC_LINK_ACCEPTED"=>"Aceito pela SEF/SC","SEF_SC_LINK_DENIED"=>"Recusado pela SEF/SC",_=>AccountantLinkStatusCode??"Não verificado"};
}
public sealed record CompanyQuery(string Search="",bool? IsActive=null,int Page=1,int PageSize=50,bool All=false);
public sealed record CompanyPage(IReadOnlyList<CompanyRow> Items,int Total,int Page,int PageSize);
public enum CompanyError { InvalidData, DuplicateCnpj, NotFound, Conflict, HasDependencies }
public sealed class CompanyException(CompanyError code,string message) : Exception(message)
{ public CompanyError Code { get; }=code; }
public interface ICompanyService
{
 Task<CompanyPage> SearchAsync(CompanyQuery query,CancellationToken cancellationToken=default);
 Task<CompanyRow?> FindAsync(Guid id,CancellationToken cancellationToken=default);
 Task<CompanyRow> CreateAsync(CompanyInput input,Guid correlationId,CancellationToken cancellationToken=default);
 Task<CompanyRow> UpdateAsync(Guid id,long revision,CompanyInput input,Guid correlationId,CancellationToken cancellationToken=default);
 Task SetActiveAsync(Guid id,long revision,bool active,Guid correlationId,CancellationToken cancellationToken=default);
 Task DeleteAsync(Guid id,long revision,Guid correlationId,CancellationToken cancellationToken=default);
}
public enum ImportRowStatus { Ready, Invalid, DuplicateInFile, DuplicateInDatabase, Imported, Cancelled }
public sealed record ImportRow(int RowNumber,CompanyInput? Data,string RawCnpj,ImportRowStatus Status,string Reason);
public sealed record ImportPlan(string SourceName,string? SheetName,IReadOnlyList<ImportRow> Rows)
{
 public int Ready=>Rows.Count(r=>r.Status==ImportRowStatus.Ready);
 public int Invalid=>Rows.Count(r=>r.Status==ImportRowStatus.Invalid);
 public int Duplicates=>Rows.Count(r=>r.Status is ImportRowStatus.DuplicateInFile or ImportRowStatus.DuplicateInDatabase);
}
public sealed record ImportProgress(int Processed,int Total,int Imported);
public sealed record ImportResult(Guid JobId,IReadOnlyList<ImportRow> Rows,bool Cancelled)
{
 public int Imported=>Rows.Count(r=>r.Status==ImportRowStatus.Imported);
 public int Invalid=>Rows.Count(r=>r.Status==ImportRowStatus.Invalid);
 public int Duplicates=>Rows.Count(r=>r.Status is ImportRowStatus.DuplicateInFile or ImportRowStatus.DuplicateInDatabase);
}
public interface ICompanyImportService
{
 Task<ImportPlan> AnalyzeAsync(string path,CancellationToken cancellationToken=default);
 Task<ImportResult> ImportAsync(ImportPlan plan,IProgress<ImportProgress>? progress=null,CancellationToken cancellationToken=default);
}
