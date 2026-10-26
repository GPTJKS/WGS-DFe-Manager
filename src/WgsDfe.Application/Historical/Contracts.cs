using WgsDfe.Domain;
using WgsDfe.Application.Providers;
namespace WgsDfe.Application.Historical;
public sealed class HistoricalException(string code):Exception(HistoricalMessages.Describe(code)){public string Code{get;}=code;}
public sealed record HistoricalCompany(Guid Id,string Name,string Cnpj);
public sealed record HistoricalImport(string File,FiscalEnvironment Environment,Guid? CompanyId=null);
public sealed record HistoricalOptions(bool UseOfficial=true,bool UseHistorical=false,string? Provider=null,bool AllowPaid=false,int MaximumPaidCalls=0);
public sealed record HistoricalRecoveryProgress(int Processed,int Total,int Line,string Code);
public sealed record HistoricalProviderRegistration(IHistoricalDocumentProvider Provider,bool IsPaid=false,bool IdempotentSubmission=false,int PollSeconds=60);
public sealed record HistoricalProviderChoice(string Code,bool IsPaid);
public sealed record HistoricalBatchRow(Guid Id,string File,FiscalEnvironment Environment,int Total,int Valid,int Invalid,int Duplicates,int Available,int Waiting,int PaidCalls,JobStatus Status)
{public string EnvironmentLabel=>Environment==FiscalEnvironment.Production?"Produção":"Homologação";}
public sealed record HistoricalGroup(string Company,string Issuer,DocumentModel? Model,int? Year,int? Month,int Count);
public sealed record HistoricalRow(Guid Id,int Line,string Input,string? Key,DocumentModel? Model,string? Issuer,int? Year,int? Month,string Company,string Code,Availability Availability,string? Provider,int Attempts,DateTime? NextAllowedAtUtc)
{public string Result=>HistoricalMessages.Describe(Code);}
public sealed record HistoricalPage(IReadOnlyList<HistoricalRow> Rows,int Total,int Page);
public interface IHistoricalRecoveryService
{
 Task<IReadOnlyList<HistoricalCompany>> CompaniesAsync(CancellationToken ct=default);
 IReadOnlyList<HistoricalProviderChoice> Providers();
 Task<Guid> ImportAsync(HistoricalImport request,CancellationToken ct=default);
 Task<IReadOnlyList<HistoricalBatchRow>> BatchesAsync(CancellationToken ct=default);
 Task<HistoricalPage> RowsAsync(Guid batch,int page=1,string search="",CancellationToken ct=default);
 Task<IReadOnlyList<HistoricalGroup>> GroupsAsync(Guid batch,CancellationToken ct=default);
 Task AssignUnassignedAsync(Guid batch,Guid company,CancellationToken ct=default);
 Task RunAsync(Guid batch,HistoricalOptions options,IProgress<HistoricalRecoveryProgress>? progress=null,CancellationToken ct=default);
 Task WriteReportAsync(Guid batch,string destination,CancellationToken ct=default);
}
public static class HistoricalMessages
{
 public static string SafeCode(string code)=>Describe(code)=="Operação pendente; confira o diagnóstico"?"EXTERNAL_ERROR":code;
 public static string Describe(string code)=>code switch
 {
 "LOCAL_INTEGRITY_ERROR"=>"Arquivo local inconsistente: confira a integridade antes de consultar fontes", "COMPANY_XML_MISMATCH"=>"XML local não pertence à empresa selecionada", "PROVIDER_CHANGE_REQUIRES_REVIEW"=>"Retome com o mesmo provedor do pedido externo",
 "READY"=>"Pronta para conferência", "INVALID_KEY"=>"Chave inválida", "INVALID_CELL"=>"Use chave como texto, sem fórmula", "DUPLICATE"=>"Chave repetida no arquivo", "LOCAL_AVAILABLE"=>"XML íntegro disponível no acervo", "RECOVERED"=>"XML recuperado e conferido", "COMPANY_REQUIRED"=>"Associe uma empresa para consultar fontes", "COMPANY_DISABLED"=>"Empresa inativa ou documento desabilitado", "NO_OFFICIAL_KEY_PROVIDER"=>"Sem consulta oficial por chave aplicável", "OFFICIAL_NOT_AVAILABLE"=>"XML não disponível na fonte oficial", "OFFICIAL_DEFERRED"=>"Consulta oficial pendente; confira consultas e prazos", "NO_HISTORICAL_PROVIDER"=>"Nenhum provedor histórico configurado", "HISTORICAL_DISABLED"=>"Recuperação externa não habilitada", "NETWORK_DISABLED"=>"Consultas externas desabilitadas nesta sessão", "PAID_NOT_ALLOWED"=>"Autorize chamadas pagas e defina um limite", "PAID_LIMIT"=>"Limite de chamadas potencialmente pagas atingido", "EXTERNAL_WAITING"=>"Provedor processando; retome após o prazo", "EXTERNAL_SUBMITTING"=>"Envio reservado; resposta ainda não confirmada", "SUBMISSION_REVIEW_REQUIRED"=>"Envio incerto: confira com o provedor antes de repetir", "EXTERNAL_JOB_FAILED"=>"Provedor encerrou o pedido sem recuperação", "NOT_FOUND"=>"Não disponível na fonte consultada", "PAYLOAD_RECEIVED"=>"XML recebido; aplicação pendente", "PAYLOAD_INVALID"=>"Retorno inconsistente preservado para conferência", "PAYLOAD_HASH_MISMATCH"=>"Retorno preservado diverge do hash", "PAYLOAD_CONFLICT"=>"XML divergente: principal preservado", "EXTERNAL_ERROR"=>"Consulta externa não concluída; retome após o prazo", "INVALID_FILE"=>"Confira o arquivo: XLSX, CSV ou TXT, com uma coluna CHAVE como texto", "NOT_FOUND_BATCH"=>"Lote não encontrado nesta organização", "INVALID_OPTIONS"=>"Confira empresa, ambiente, provedor e limite de chamadas", "WAIT_UNTIL_ALLOWED"=>"Aguarde o prazo antes de retomar", _=>"Operação pendente; confira o diagnóstico"
 };
}
