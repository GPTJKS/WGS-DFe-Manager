using WgsDfe.Application.Archive;
using WgsDfe.Domain;
namespace WgsDfe.Application.Export;
public sealed record ExportHistoryEntry(Guid JobId,DateTime CreatedAtUtc,string Output,int Written,int Skipped,int Companies);
public enum ExportFormat { Zip, Folder }

public sealed record ExportRequest(ArchiveQuery Filter,string Destination,ExportFormat Format=ExportFormat.Folder,
 IReadOnlyCollection<Guid>? Companies=null,EnabledDocuments Models=EnabledDocuments.All,bool IncludeEvents=true,bool IncludeOrphanEvents=false,
 bool IncludeVersions=false,bool IncludeConflicts=false,bool IncludeUnassigned=true,
 bool SkipInvalid=false,IReadOnlyCollection<Guid>? DocumentIds=null,IReadOnlyCollection<Guid>? EventIds=null);
public sealed record ExportPreview(int Documents,int Events,int Files,long EstimatedBytes,int SharedCopies);
public sealed record ExportProgress(int Processed,int Total,int Written,int Skipped,string Code);
public sealed record ExportRow(string Path,string Key,DocumentModel Model,FiscalEnvironment Environment,string Kind,string Hash,long Bytes,string Code)
{
 public ExportDetails Details {get;init;}=new();
 public string Result=>ExportMessages.Describe(Code);
}
public sealed record ExportDetails(string Company="",string Cnpj="",string Number="",string Series="",string IssueDate="",string KnownStatus="Não confirmada",decimal? Total=null,string Notes="",string Folder="");
public sealed record ExportResult(Guid JobId,string? Output,int Written,int Skipped,bool Cancelled,bool Failed,string Code,IReadOnlyList<ExportRow> Rows)
{public string Message=>ExportMessages.Describe(Code);}
public sealed record ExportLimits(int MaxFiles=100000,long MaxBytes=10L*1024*1024*1024);
public sealed class ExportException(string code):Exception(ExportMessages.Describe(code)){public string Code{get;}=code;}
public interface IExportService
{
 Task<ExportPreview> PreviewAsync(ExportRequest request,CancellationToken ct=default);
 Task<ExportResult> ExportAsync(ExportRequest request,IProgress<ExportProgress>? progress=null,CancellationToken ct=default);

}
public static class ExportMessages
{
 public static string Describe(string code)=>code switch
 {
 "NOT_PUBLISHED"=>"XML conferido, mas pacote final não publicado", "NOT_PROCESSED"=>"Não processado: exportação interrompida", "EXPORT_COPYING"=>"Preparando XMLs", "EXPORT_VERIFYING"=>"Conferindo os arquivos gerados",
 "EXPORT_INVALID_DATE"=>"Informe datas válidas em DD/MM/AAAA e confira a ordem do período", "EXPORT_PUBLISHED_RECEIPT_PENDING"=>"Arquivos publicados e conferidos; histórico de exportação pendente. Limpeza não será autorizada sem comprovante local.", "EXPORT_COMPLETED"=>"Exportação concluída e conferida", "ALREADY_EXISTS"=>"XML idêntico já existente; preservado sem duplicação", "EXPORT_DESTINATION_CONFLICT"=>"Já existe conteúdo diferente neste caminho; compare os XMLs ou escolha outro destino", "EXPORT_PATH_TOO_LONG"=>"Caminho longo demais; escolha um destino mais curto", "EXPORT_WARNINGS"=>"Exportação concluída com arquivos não exportados; confira o resumo", "EXPORT_CANCELLED"=>"Cancelada; arquivos concluídos e preexistentes preservados", "EXPORT_FAILED"=>"Exportação não concluída; nenhum pacote final foi publicado", "EXPORT_PUBLISHED_AUDIT_PENDING"=>"Pacote publicado e conferido; registro no banco pendente de conferência", "EXPORT_INVALID_REQUEST"=>"Confira filtros, empresas, modelos e opções", "EXPORT_DESTINATION_INVALID"=>"Escolha uma pasta de destino existente, fora da pasta de dados e sem links", "EXPORT_NO_FILES"=>"Nenhum XML corresponde à seleção", "EXPORT_LIMIT"=>"A seleção excede o limite de arquivos ou tamanho; reduza os filtros", "EXPORT_SOURCE_INVALID"=>"Há XML ausente ou inconsistente; confira o resumo ou habilite ignorar inválidos", "EXPORT_NO_VALID_FILES"=>"Nenhum XML íntegro foi encontrado; pacote final não publicado", "EXPORT_VERIFY_FAILED"=>"O pacote gerado não passou na conferência; não foi publicado", "EXPORTED"=>"XML exportado e conferido", "FileMissing"=>"XML físico ausente", "IntegrityMismatch"=>"Arquivo diverge do hash registrado", "UnsafePath"=>"Caminho ou link não permitido", "StorageUnavailable"=>"Arquivo indisponível", "XML_INVALID"=>"XML ou metadados inconsistentes", _=>"Operação não concluída; confira integridade e espaço disponível"
 };
}
