using System.Security.Cryptography.X509Certificates;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
namespace WgsDfe.Application.Nfe;
public static class NfeSources
{
 public const string SantaCatarina="SEF_SC_NFE",National="NFE_NACIONAL",SantaCatarinaNfce="SEF_SC_NFCE",SantaCatarinaNfcom="NFCOM";
 public static bool IsSantaCatarina(string code)=>code is SantaCatarina or SantaCatarinaNfce or SantaCatarinaNfcom;
 public static DocumentModel Model(string code)=>code switch{SantaCatarinaNfce=>DocumentModel.NFCe,SantaCatarinaNfcom=>DocumentModel.NFCom,_=>DocumentModel.NFe};
 public static EnabledDocuments Documents(string code)=>code switch{SantaCatarinaNfce=>EnabledDocuments.NFCe,SantaCatarinaNfcom=>EnabledDocuments.NFCom,_=>EnabledDocuments.NFe};
 public static bool IsKnown(string code)=>code is SantaCatarina or National or SantaCatarinaNfce or SantaCatarinaNfcom;
}
public sealed record NfeSyncRequest(IReadOnlyCollection<Guid> Companies,string ProviderCode,FiscalEnvironment Environment,
 ActorFilter Actor=ActorFilter.Either,PayloadMode PayloadMode=PayloadMode.FullXml,string? AccessKey=null,int MaxPages=10,DateOnly? From=null,DateOnly? To=null);
public sealed record NfeCompany(Guid Id,string Name,string Cnpj,string State,string? TradeName=null);
public sealed record NfeSyncRow(Guid CompanyId,string Company,string Provider,string Code,int? CStat,int Pages,int Documents,int Summaries,string Nsu,DateTime? NextAllowedAtUtc)
{
 public DateTime? NextAllowedAtLocal=>NextAllowedAtUtc is DateTime utc?DateTime.SpecifyKind(utc,DateTimeKind.Utc).ToLocalTime():null;
 public string Result=>Code=="WAIT_UNTIL_ALLOWED"&&NextAllowedAtLocal is DateTime next?$"{NfeMessages.Describe(Code)} — liberada em {next:dd/MM/yyyy} às {next:HH:mm:ss} (horário local)":NfeMessages.Describe(Code);
}
public sealed record NfeSyncResult(Guid JobId,IReadOnlyList<NfeSyncRow> Rows,bool Cancelled);
public sealed record NfeProgress(string Company,int Page,string Code,NfeSyncRow? Row=null,WgsDfe.Application.Cte.CteSyncRow? CteRow=null,int NewXml=0,int ExistingXml=0,int InPeriod=0,int OutsidePeriod=0,int Undated=0);
public interface INfeSyncService
{
 Task<IReadOnlyList<NfeCompany>> CompaniesAsync(CancellationToken ct=default,string source=NfeSources.SantaCatarina);
 Task<IReadOnlyList<NfeSyncRow>> StatesAsync(CancellationToken ct=default,string? source=null);
 Task<NfeSyncResult> SynchronizeAsync(NfeSyncRequest request,IProgress<NfeProgress>? progress=null,CancellationToken ct=default);
}
public sealed class NfeException(string code):Exception(NfeMessages.Describe(code)){public string Code{get;}=code;}
public sealed class NfeCredential(string taxId,string stateCode,X509Certificate2 certificate):IDisposable
{
 public string TaxId{get;}=taxId;public string StateCode{get;}=stateCode;public X509Certificate2 Certificate{get;}=certificate;
 public void Dispose()=>Certificate.Dispose();
}
public interface INfeCredentialSource{Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct);}
public sealed record NfeWireRequest(Uri Endpoint,string SoapAction,byte[] Body,bool Soap12=false);
public interface INfeTransport{Task<byte[]> SendAsync(NfeWireRequest request,X509Certificate2 certificate,CancellationToken ct);}
public sealed record NfeItem(string? Key,string Nsu,byte[]? Xml,string Schema);
public sealed record NfePage(int Status,string? NextNsu,bool HasMore,TimeSpan Delay,IReadOnlyList<NfeItem> Items);
public interface INfeDistributionProvider:IDocumentProvider
{
 Task<byte[]> ReceiveAsync(DocumentRequest request,CancellationToken ct);
 NfePage Decode(ReadOnlyMemory<byte> response,DocumentRequest request);
}

public static class NfeMessages
{
 public static string Describe(string code)=>code switch
 {
  "NFE_CANCELLED"=>"Consulta cancelada; documentos confirmados preservados",
  "NFE_SYNCHRONIZED"=>"Documentos disponíveis consultados",
  "NFE_PROCESSING"=>"Resposta recebida; validando e salvando os XMLs",
  "NFE_PAGE_COMMITTED"=>"Limite de lotes atingido; ainda há documentos. Use continuação automática (0) e consulte novamente.",
  "NFE_CURSOR_NOT_ADVANCING"=>"A fonte repetiu a posição do lote. Consulta pausada para evitar repetição; confira o diagnóstico.",
  "SAVED_STATE"=>"Última posição salva",
  "LOCAL_XML_PRESENT"=>"XML conferido no acervo local",
  "WAIT_UNTIL_ALLOWED"=>"Aguardando prazo da próxima consulta",
  "EXTERNAL_REQUESTS_DISABLED"=>"Habilite as consultas oficiais nesta sessão",
  "PROVIDER_CERTIFICATE_REQUIRED"=>"A Distribuição Nacional exige e-CNPJ compatível com a empresa consultada. O certificado do contador não substitui esse e-CNPJ. Para consultar como contador em SC, escolha SEF/SC — contador.",
  "DEFAULT_ACCOUNTANT_UNAVAILABLE"=>"O certificado do contador padrão não está disponível ou venceu. Escolha o certificado renovado em Gerenciar certificados → Usar nas consultas SC.",
  "CHOOSE_ACCOUNTANT_CERTIFICATE"=>"Há certificados de contadores diferentes. Em Gerenciar certificados, selecione o correto e clique em Usar nas consultas SC. Essa escolha vale para todas as empresas.",
  "ACCOUNTANT_CERTIFICATE_REQUIRED"=>"Esta fonte SC exige certificado do contador. Importe-o em Gerenciar certificados com finalidade Escritório / contador. Para usar o e-CNPJ da empresa na NF-e, escolha Distribuição Nacional.",
  "PARTICIPANT_ROOT_CNPJ_MISMATCH"=>"Nacional exige certificado da empresa com a mesma raiz CNPJ",
  "CERTIFICATE_OUTSIDE_VALIDITY" or "CERTIFICATE_KEY_OR_VALIDITY_INVALID"=>"Certificado vencido, ainda não vigente ou sem chave",
  "CERTIFICATE_STORE_UNAVAILABLE"=>"Não foi possível abrir o certificado neste perfil do Windows",
  "CERTIFICATE_CHAIN_OR_REVOCATION_FAILED"=>"Verifique a cadeia do certificado e o acesso à revogação",
  "ICP_BRASIL_POLICY_REQUIRED"=>"Certificado ICP-Brasil necessário",
  "NFE_ACCOUNTANT_LINK_DENIED"=>"SEF/SC recusou o vínculo do contador",
  "NFE_KEY_ACCESS_DENIED"=>"Sem permissão para esta chave",
  "NFE_CONSUMPTION_BLOCKED"=>"SEFAZ bloqueou novas consultas por consumo",
  "NFE_OUTSIDE_SOURCE_WINDOW"=>"Documento fora do prazo da fonte",
  "NFE_NOT_FOUND_OR_OUTSIDE_WINDOW"=>"Chave não localizada ou fora do prazo",
  "NFE_CERTIFICATE_REJECTED"=>"SEFAZ recusou o certificado",
  "NETWORK_TIMEOUT"=>"Tempo de resposta excedido; aguarde antes de repetir",
  "NETWORK_DNS_FAILED"=>"Não foi possível localizar o endereço do serviço fiscal (DNS). Confira a conexão e o DNS da rede.",
  "NETWORK_CONNECTION_FAILED"=>"Não foi possível manter a conexão com o serviço fiscal. Verifique rede, proxy, firewall ou disponibilidade do serviço.",
  "NETWORK_PROXY_FAILED"=>"O proxy da rede recusou ou não conseguiu estabelecer o túnel seguro para o serviço fiscal.",
  "TLS_CLIENT_CREDENTIAL_UNAVAILABLE"=>"O Windows não conseguiu disponibilizar a chave do certificado para a conexão segura. Confira a versão do programa e o perfil Windows usado para importar o certificado.",
  "TLS_CERTIFICATE_UNTRUSTED"=>"O Windows recusou a cadeia de confiança durante a conexão TLS. Verifique as cadeias do certificado e do serviço fiscal.",
  "TLS_CERTIFICATE_EXPIRED"=>"A conexão TLS encontrou um certificado fora da validade. Confira data/hora do Windows e a validade dos certificados.",
  "TLS_CERTIFICATE_REVOKED"=>"A conexão TLS encontrou um certificado revogado. A conexão foi interrompida.",
  _ when code.StartsWith("TLS_HANDSHAKE_FAILED",StringComparison.Ordinal)=>"A negociação TLS com o serviço fiscal falhou. Envie este código completo para diagnóstico; não é um pedido de nova associação do certificado.",
  "NETWORK_OR_TLS_FAILED"=>"Falha de rede ou conexão segura",
  "MTLS_ACCESS_DENIED"=>"Acesso recusado na conexão com o certificado",
  "SC_REQUIRES_SC_COMPANY_AND_PRODUCTION"=>"SEF/SC atende empresas SC em produção",
  "NATIONAL_HAS_ONE_STREAM_NO_ACTOR_FILTER"=>"Nacional usa uma distribuição por empresa e ambiente",
  "KEY_REQUIRES_ONE_COMPANY_AND_MODEL_62"=>"Para consultar chave NFCom, selecione uma empresa e uma chave modelo 62",
  "KEY_REQUIRES_ONE_COMPANY_AND_MODEL_65"=>"Para consultar chave NFC-e, selecione uma empresa e uma chave modelo 65",
  "KEY_REQUIRES_ONE_COMPANY_AND_MODEL_55"=>"Para consultar chave NF-e, selecione uma empresa",
  "COMPANY_NOT_FOUND" or "COMPANY_NFE_DISABLED"=>"Empresa não encontrada, inativa ou sem o documento habilitado",
  "INVALID_SYNC_REQUEST"=>"Confira empresas, fonte, ambiente e limite de lotes",
  "NFE_REQUESTING"=>"Consultando a SEFAZ",
  _ when code.Contains("INBOX",StringComparison.Ordinal)||code.StartsWith("PAYLOAD_",StringComparison.Ordinal)||code.StartsWith("DOCZIP_",StringComparison.Ordinal)=>"Lote recebido pendente de conferência; cursor preservado",
  _ when code.StartsWith("NFE_OFFICIAL_REJECTION_",StringComparison.Ordinal)=>"SEFAZ recusou a consulta; confira o cStat",
  _=>"Consulta não concluída; confira os dados e o diagnóstico"
 };
}
