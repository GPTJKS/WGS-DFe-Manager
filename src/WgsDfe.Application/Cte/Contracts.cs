using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
namespace WgsDfe.Application.Cte;
public static class CteSource{public const string Code="CTE";}
public sealed record CteSyncRequest(IReadOnlyCollection<Guid> Companies,FiscalEnvironment Environment,EnabledDocuments Models=EnabledDocuments.CTe|EnabledDocuments.CTeOS,string? SpecificNsu=null,int MaxPages=10,DateOnly? From=null,DateOnly? To=null);
public sealed record CteCompany(Guid Id,string Name,string Cnpj,string State,EnabledDocuments EnabledDocuments);
public sealed record CteSyncRow(Guid CompanyId,string Company,FiscalEnvironment Environment,string Code,int? CStat,int Pages,int Archived,int Retained,string Nsu,DateTime? NextAllowedAtUtc)
{public DateTime? NextAllowedAtLocal=>NextAllowedAtUtc is DateTime utc?DateTime.SpecifyKind(utc,DateTimeKind.Utc).ToLocalTime():null;public string Result=>Code=="WAIT_UNTIL_ALLOWED"&&NextAllowedAtLocal is DateTime next?$"{CteMessages.Describe(Code)} — liberada em {next:dd/MM/yyyy} às {next:HH:mm:ss} (horário local)":CteMessages.Describe(Code);public string EnvironmentLabel=>Environment==FiscalEnvironment.Production?"Produção":"Homologação";}
public sealed record CteSyncResult(Guid JobId,IReadOnlyList<CteSyncRow> Rows,bool Cancelled);
public sealed record CteRetainedRow(Guid Id,string Company,FiscalEnvironment Environment,string Nsu,string Schema,string? AccessKey,int? Model,string Code)
{public string Reason=>CteMessages.Describe(Code);public string EnvironmentLabel=>Environment==FiscalEnvironment.Production?"Produção":"Homologação";}
public interface ICteSyncService
{
 Task<WgsDfe.Application.Archive.ArchiveXml> ReadRetainedXmlAsync(Guid id,CancellationToken ct=default);
 Task<IReadOnlyList<CteCompany>> CompaniesAsync(CancellationToken ct=default);
 Task<IReadOnlyList<CteSyncRow>> StatesAsync(CancellationToken ct=default);
 Task<IReadOnlyList<CteRetainedRow>> RetainedAsync(int page=1,CancellationToken ct=default);
 Task<CteSyncResult> SynchronizeAsync(CteSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default);
 Task<CteSyncResult> ReprocessAsync(CteSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default);
}
public interface ICteDistributionProvider:IDocumentProvider
{
 Task<byte[]> ReceiveAsync(DocumentRequest request,CancellationToken ct);
 NfePage Decode(ReadOnlyMemory<byte> bytes,DocumentRequest request);
}
public static class CteMessages
{
 public static string Describe(string code)=>code switch
 {
  "CTE_LOCAL_NSU_PRESENT"=>"XML do NSU conferido no acervo local",
  "CTE_ITEM_NOT_FOUND"=>"Item preservado não encontrado; atualize a lista",
  "CTE_DOCUMENTS_RECEIVED"=>"Lote CT-e recebido",
  "CTE_SYNCHRONIZED"=>"Documentos disponíveis recebidos",
  "CTE_PAGE_COMMITTED"=>"Limite de lotes atingido; ainda há documentos. Use continuação automática (0).",
  "CTE_CURSOR_NOT_ADVANCING"=>"A fonte repetiu o NSU. Consulta pausada para evitar repetição.",
  "CTE_REPROCESSED"=>"Itens preservados reavaliados sem consulta à rede",
  "CTE_MODEL_NOT_SELECTED"=>"Modelo fora da seleção ou desabilitado na empresa",
  "CTE_LAYOUT_RETAINED"=>"XML preservado; leiaute ainda não suportado",
  "CTE_CONFLICT_RETAINED"=>"XML preservado; divergência com o acervo",
  "CTE_COMPANY_MISMATCH_RETAINED"=>"XML preservado; participação da empresa não conciliada",
  "CTE_PAYLOAD_INVALID_RETAINED"=>"XML preservado; conteúdo fiscal requer conferência",
  "CTE_CONSUMPTION_BLOCKED"=>"SEFAZ bloqueou novas consultas por consumo",
  "CTE_CERTIFICATE_REJECTED"=>"SEFAZ recusou o certificado",
  "CTE_PARTICIPANT_REQUIRED"=>"Associe certificado participante à fonte CT-e",
  "CTE_NSU_INVALID"=>"NSU deve conter até 15 dígitos; consulta pontual exige valor maior que zero",
  "CTE_KEY_LOOKUP_UNSUPPORTED"=>"Esta distribuição consulta NSU; não há consulta por chave neste contrato",
  "CTE_COMPANY_DISABLED"=>"Empresa inativa ou sem os modelos selecionados habilitados",
  "CTE_INVALID_REQUEST"=>"Confira empresas, modelos, ambiente e limite de lotes",
  "CTE_OFFICIAL_NO_DOCUMENTS"=>"Nenhum documento novo na fonte",
  "CTE_POINT_RATE_LIMIT"=>"Limite local de consultas pontuais atingido; aguarde uma hora",
  "CTE_INBOX_HASH_MISMATCH" or "CTE_ITEM_HASH_MISMATCH"=>"Integridade do recebimento divergente; cursor preservado",
  "CTE_POINT_RETURN_MISMATCH"=>"NSU devolvido não corresponde à consulta pontual",
  _ when code.StartsWith("CTE_OFFICIAL_REJECTION_",StringComparison.Ordinal)=>"SEFAZ recusou a consulta; confira o cStat",
  _=>NfeMessages.Describe(code)
 };
}
