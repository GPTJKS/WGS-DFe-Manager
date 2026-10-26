using WgsDfe.Domain;
namespace WgsDfe.Application.Archive;
public enum ArchiveOutcome { Imported, Duplicate, VariantStored, Upgraded, Conflict, Restored, SummarySkipped, Ignored, Invalid, UnsafeArchive, LimitExceeded, SourceUnavailable, StorageFailed }
public enum ArchiveError { InvalidQuery, NotFound, UnsafePath, FileMissing, IntegrityMismatch, StorageUnavailable }
public sealed class ArchiveException(ArchiveError code):Exception(code switch
{
 ArchiveError.InvalidQuery=>"Confira os filtros, o período e os valores informados.",ArchiveError.NotFound=>"Registro não encontrado nesta organização.",
 ArchiveError.UnsafePath=>"Caminho ou vínculo de arquivo não permitido.",ArchiveError.FileMissing=>"O XML físico não foi encontrado. Reimporte o original para recuperá-lo.",
 ArchiveError.IntegrityMismatch=>"O XML físico diverge do hash registrado. O arquivo foi preservado para conferência.",
 _=>"Não foi possível acessar o acervo. Verifique a pasta e o espaço disponível."
}){public ArchiveError Code{get;}=code;}
public sealed record ArchiveImportRequest(IReadOnlyList<string> Sources);
public sealed record ArchiveImportRow(int Number,string Source,ArchiveOutcome Outcome,string? Key,string Code)
{
 public string Result=>Outcome switch{ArchiveOutcome.Imported=>"Novo registro",ArchiveOutcome.Duplicate=>"XML idêntico",ArchiveOutcome.VariantStored=>"Versão preservada",ArchiveOutcome.Upgraded=>"Versão mais completa",ArchiveOutcome.Conflict=>"Divergência preservada",ArchiveOutcome.Restored=>"Arquivo recuperado",ArchiveOutcome.SummarySkipped=>"Resumo não importado",ArchiveOutcome.Ignored=>"Ignorado",ArchiveOutcome.Invalid=>"Inválido",ArchiveOutcome.UnsafeArchive=>"ZIP inválido ou inseguro",ArchiveOutcome.LimitExceeded=>"Limite excedido",ArchiveOutcome.SourceUnavailable=>"Origem indisponível",_=>"Falha de gravação"};
 public string Reason=>Code.StartsWith("XML_",StringComparison.Ordinal)&&Enum.TryParse<WgsDfe.Application.Parsing.DfeParseError>(Code[4..],out var error)?new WgsDfe.Application.Parsing.DfeParseException(error).Message:Outcome switch
 {
  ArchiveOutcome.Conflict=>"A versão anterior foi mantida. Confira as diferenças nos detalhes.",ArchiveOutcome.SummarySkipped=>"Resumo/evento resumido não substitui o XML integral.",
  ArchiveOutcome.Ignored=>Code=="OWN_DATA_DIRECTORY"?"A própria pasta de dados não é reimportada.":Code=="LINK_SKIPPED"?"Links simbólicos não são seguidos.":"Arquivo sem extensão XML ou ZIP interno não expandido.",
  ArchiveOutcome.LimitExceeded=>"O arquivo ou lote ultrapassa os limites de segurança documentados.",ArchiveOutcome.StorageFailed=>"Verifique integridade, permissões, espaço e banco. Itens concluídos preservados.",
  ArchiveOutcome.UnsafeArchive=>"Confira o ZIP: estrutura, caminhos ou atributos não permitidos.",ArchiveOutcome.Invalid=>"Não foi possível validar os bytes/XML ou o CRC do arquivo.",
  ArchiveOutcome.SourceUnavailable=>"Não foi possível acessar a origem.",_=>Result
 };
}
public sealed record ArchiveProgress(int Processed,int Imported,string CurrentSource);
public sealed record ArchiveImportResult(Guid JobId,IReadOnlyList<ArchiveImportRow> Rows,bool Cancelled,bool Failed)
{
 public int Imported=>Rows.Count(x=>x.Outcome==ArchiveOutcome.Imported);
 public int Duplicates=>Rows.Count(x=>x.Outcome==ArchiveOutcome.Duplicate);
 public int Conflicts=>Rows.Count(x=>x.Outcome==ArchiveOutcome.Conflict);
}
public sealed record ArchiveQuery(string Search="",Guid? CompanyId=null,DocumentModel? Model=null,FiscalEnvironment? Environment=null,
 DocumentStatus? Status=null,string IssuerTaxId="",string RecipientTaxId="",string Number="",string Series="",DateOnly? From=null,DateOnly? To=null,
 decimal? Minimum=null,decimal? Maximum=null,ActorFilter? Role=null,int Page=1,int PageSize=50,IReadOnlyList<Guid>? CompanyIds=null,IReadOnlyList<DocumentModel>? Models=null);
public sealed record ArchiveDocumentRow(Guid Id,string Key,DocumentModel Model,FiscalEnvironment Environment,string? Issuer,string? IssuerName,
 string? Recipient,string? RecipientName,string? IssueDate,string? OriginalDate,string? Number,string? Series,decimal? Total,DocumentStatus Status,int Versions,int Conflicts,int LinkedCompanies)
{
 public string? IssueDateLabel=>DateOnly.TryParseExact(IssueDate,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date)?date.ToString("dd/MM/yyyy"):IssueDate;
 public string ModelLabel=>Model switch{DocumentModel.NFe=>"NF-e",DocumentModel.NFCe=>"NFC-e",DocumentModel.CTe=>"CT-e",DocumentModel.CTeOS=>"CT-e OS",_=>"NFCom"};
 public string StatusLabel=>Status switch{DocumentStatus.Authorized=>"Autorizado (XML)",DocumentStatus.Cancelled=>"Cancelado (XML/evento)",DocumentStatus.Denied=>"Denegado (XML)",DocumentStatus.Other=>"Outra resposta",_=>"Não confirmado"};
 public string EnvironmentLabel=>Environment==FiscalEnvironment.Production?"Produção":"Homologação";
}
public sealed record ArchivePage(IReadOnlyList<ArchiveDocumentRow> Items,int Total,int Page,int PageSize);
public sealed record ArchiveCompany(Guid? Id,string Label);
public sealed record ArchiveEventRow(Guid Id,Guid? DocumentId,string Key,DocumentModel Model,FiscalEnvironment Environment,string Type,int Sequence,int? ResponseCode,string? Protocol,string? Date,int Versions,string? Company=null)
{
 public string EnvironmentLabel=>Environment==FiscalEnvironment.Production?"Produção":"Homologação";
 public string TypeLabel=>Type switch{"110111"=>"Cancelamento", "110112"=>"Cancelamento por substituição","110110"=>"Carta de correção","210200"=>"Confirmação da operação","210210"=>"Ciência da operação","210220"=>"Desconhecimento da operação","210240"=>"Operação não realizada",_=>"Evento "+Type};
 public string CompanyLabel=>string.IsNullOrWhiteSpace(Company)?"Sem vínculo":Company;
}
public sealed record ArchiveEventPage(IReadOnlyList<ArchiveEventRow> Items,int Total);
public sealed record ArchiveFileRow(Guid Id,string Hash,string Path,long Bytes,bool Conflict,int? ResponseCode,DateTime ImportedAtUtc,bool IsPrimary=false);
public sealed record ArchiveDetail(ArchiveDocumentRow Document,IReadOnlyList<ArchiveEventRow> Events,IReadOnlyList<ArchiveFileRow> Files);
public sealed record ArchiveXml(string Text,bool PreviewTruncated,string Hash);
public sealed record ArchiveOriginalXml(byte[] Bytes,string FileName,string Hash);
public sealed record IntegrityRow(string Path,string Code);
public sealed record IntegrityResult(int Checked,int Valid,IReadOnlyList<IntegrityRow> Problems,int Orphans,bool Cancelled);
public sealed record ArchiveAvailability(Guid? DocumentId,Availability Availability,string Code,string? Hash=null);
public sealed record ArchiveFinancialQuery(string Search="",Guid? CompanyId=null,DocumentModel? Model=null,FiscalEnvironment? Environment=null,
 string IssuerTaxId="",string RecipientTaxId="",DateOnly? From=null,DateOnly? To=null,ActorFilter? Role=null,IReadOnlyList<Guid>? CompanyIds=null,IReadOnlyList<DocumentModel>? Models=null);
public sealed record ArchiveFinancialSummary(int AuthorizedCount,decimal GrossAuthorized,int CancelledCount,decimal TotalCancelled,decimal NetAmount,int DeniedCount,decimal TotalDenied,int TotalDocuments)
{
 public static readonly ArchiveFinancialSummary Empty=new(0,0m,0,0m,0m,0,0m,0);
}
public interface IArchiveService
{
 Task<ArchiveImportResult> ImportAsync(ArchiveImportRequest request,IProgress<ArchiveProgress>? progress=null,CancellationToken ct=default);
 Task<ArchivePage> SearchAsync(ArchiveQuery query,CancellationToken ct=default);
 Task<ArchiveFinancialSummary> FinancialSummaryAsync(ArchiveFinancialQuery query,CancellationToken ct=default);
 Task<ArchiveEventPage> SearchEventsAsync(string search,FiscalEnvironment? environment=null,DocumentModel? model=null,int page=1,CancellationToken ct=default);
 Task<IReadOnlyList<ArchiveCompany>> CompaniesAsync(CancellationToken ct=default);
 Task<ArchiveDetail> DetailAsync(Guid id,CancellationToken ct=default);
 Task<IReadOnlyList<ArchiveFileRow>> EventFilesAsync(Guid id,CancellationToken ct=default);
 Task<ArchiveXml> ReadXmlAsync(Guid fileId,CancellationToken ct=default);
 /// <summary>Retorna os bytes originais do XML armazenado (hash conferido), sem reformatação.</summary>
 Task<ArchiveOriginalXml> ReadOriginalXmlAsync(Guid fileId,CancellationToken ct=default);
 Task<IntegrityResult> CheckIntegrityAsync(IProgress<int>? progress=null,CancellationToken ct=default);
 Task<int> RefreshLinksAsync(CancellationToken ct=default);
 Task<ArchiveAvailability> CheckAvailabilityAsync(string key,FiscalEnvironment environment,CancellationToken ct=default);
}
