using WgsDfe.Domain;
namespace WgsDfe.Application.Parsing;
public enum DfePayloadKind{FullDocument,DocumentSummary,Event,EventSummary}
public enum DfeParseError{FileUnavailable,SizeLimit,UnsafeOrMalformedXml,ComplexityLimit,UnsupportedRoot,UnsupportedVersion,InvalidStructure,InvalidKey,InconsistentData,InvalidValue}
public sealed class DfeParseException(DfeParseError code):Exception(code switch
{
 DfeParseError.FileUnavailable=>"Não foi possível ler o arquivo XML.",DfeParseError.SizeLimit=>"O XML está vazio ou excede o limite de 10 MiB.",
 DfeParseError.UnsafeOrMalformedXml=>"XML malformado ou com recurso não permitido (DTD/entidade externa).",
 DfeParseError.ComplexityLimit=>"O XML excede os limites de complexidade.",DfeParseError.UnsupportedRoot=>"Tipo ou namespace de XML não suportado nesta fase.",
 DfeParseError.UnsupportedVersion=>"Versão de leiaute ainda não suportada.",DfeParseError.InvalidKey=>"O XML contém chave de acesso inválida.",
 DfeParseError.InconsistentData=>"Os dados do XML, da chave ou do protocolo divergem.",DfeParseError.InvalidValue=>"O XML contém data, valor ou identificador inválido.",
 _=>"Estrutura XML incompleta ou ambígua."
}){public DfeParseError Code{get;}=code;}
public sealed record DfeParseResult(DfePayloadKind Kind,DfeAccessKey Key,string LayoutVersion,FiscalEnvironment? Environment,
 string? IssuerTaxId,string? IssuerName,string? RecipientTaxId,string? RecipientName,DateTimeOffset? IssueDate,
 decimal? Total,DocumentStatus Status,string? ProtocolNumber,int? ResponseCode,DateTimeOffset? ReceiptDate,
 string? EventType,int? EventSequence,DateTimeOffset? EventDate,string Sha256)
{
 public string Description=>Kind switch{DfePayloadKind.FullDocument=>"Documento completo",DfePayloadKind.DocumentSummary=>"Resumo de documento",DfePayloadKind.Event=>"Evento",_=>"Resumo de evento"};
 public string Notice=>"Leitura local de metadados. Não verifica assinatura digital, XSD completo, revogação ou autenticidade na SEFAZ. Não grava no acervo.";
}
public interface IDfeParser
{
 Task<DfeParseResult> ParseFileAsync(string path,CancellationToken ct=default);
 DfeParseResult Parse(ReadOnlyMemory<byte> xml,CancellationToken ct=default);
}
