using System.Security.Cryptography.X509Certificates;
using WgsDfe.Domain;
namespace WgsDfe.Application.Certificates;
public enum CertificateValidity { NotYetValid, Valid, Expiring, Expired }
public sealed record CertificateRow(Guid Id,long Revision,string Label,string Subject,string? HolderTaxId,string Thumbprint,DateTime ValidFromUtc,DateTime ValidToUtc,CertificateUse Use,int LinkedCompanies)
{
 public CertificateValidity ValidityAt(DateTime utc)=>utc<ValidFromUtc?CertificateValidity.NotYetValid:utc>=ValidToUtc?CertificateValidity.Expired:ValidToUtc-utc<=TimeSpan.FromDays(30)?CertificateValidity.Expiring:CertificateValidity.Valid;
 public string Status=>ValidityAt(DateTime.UtcNow) switch{CertificateValidity.NotYetValid=>"Ainda não vigente",CertificateValidity.Expired=>"Vencido",CertificateValidity.Expiring=>"Vence em até 30 dias",_=>"No prazo"};
 public string Purpose=>Use==CertificateUse.Accountant?"Escritório / contador":"Empresa participante";
 public string Holder=>HolderTaxId??"CPF/CNPJ não identificado";
}
public sealed record CertificateLink(Guid Id,long Revision,Guid CompanyId,string CompanyName,string Cnpj,string ProviderCode);
public sealed record CertificateTestResult(CertificateValidity Validity,bool PrivateKeyUsable,string Message);
public sealed record CertificateMutationResult(CertificateRow? Certificate,bool CleanupPending);
public enum CertificateError { InvalidInput, InvalidPfx, NoPrivateKey, AmbiguousBundle, UnsupportedKey, NotFound, Conflict, StoreUnavailable }
public sealed class CertificateException(CertificateError code):Exception(code switch
{
 CertificateError.InvalidInput=>"Informe nome, finalidade e arquivo PFX/P12 de até 5 MiB.",
 CertificateError.InvalidPfx=>"Arquivo inválido, senha incorreta ou limites de segurança excedidos.",
 CertificateError.NoPrivateKey=>"O arquivo não contém uma chave privada de usuário utilizável.",
 CertificateError.AmbiguousBundle=>"Há mais de um certificado com chave privada. Importe um por arquivo.",
 CertificateError.UnsupportedKey=>"A chave deve permitir assinatura RSA ou ECDSA.",
 CertificateError.NotFound=>"Certificado ou empresa não encontrado nesta organização.",
 CertificateError.Conflict=>"O cadastro mudou ou já tem outro vínculo. Atualize a lista antes de continuar.",
 _=>"Cofre indisponível. Use o mesmo perfil do Windows e verifique a pasta de dados."
}){public CertificateError Code {get;}=code;}
public interface ICertificateService
{
 Task<IReadOnlyList<CertificateRow>> ListAsync(CancellationToken ct=default);
 Task<CertificateMutationResult> ImportAsync(string path,ReadOnlyMemory<char> password,string label,CertificateUse use,Guid correlationId,Guid? replaceId=null,long? revision=null,CancellationToken ct=default);
 Task<CertificateMutationResult> RemoveAsync(Guid id,long revision,Guid correlationId,CancellationToken ct=default);
 Task<CertificateTestResult> TestAsync(Guid id,Guid correlationId,CancellationToken ct=default);
 Task<IReadOnlyList<CertificateLink>> LinksAsync(Guid id,CancellationToken ct=default);
 Task AssociateAsync(Guid id,long revision,IReadOnlyCollection<Guid> companies,string providerCode,Guid correlationId,CancellationToken ct=default);
 Task UnlinkAsync(Guid linkId,long revision,Guid correlationId,CancellationToken ct=default);
 Task<int> CleanupAsync(CancellationToken ct=default);
}
// Chamador descarta o certificado. Permite futuro A3 sem PFX; não autoriza consultas.
public interface ICertificateKeySource{Task<X509Certificate2> OpenAsync(Guid certificateId,CancellationToken ct=default);}
