using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application;
using WgsDfe.Application.Companies;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Certificates;
namespace WgsDfe.Infrastructure.Companies;
// One database transaction: company + certificate metadata + national provider links.
// A failed commit removes the newly encrypted blob; previous certificates remain intact.
public sealed class CompanyRegistrationService(WgsDbContext db,ICompanyService companies,ISecretStore store)
{
 public static string ValidateCertificate(X509Certificate2 certificate,string cnpj)
 {
  if(!Cnpj.TryNormalize(cnpj,out var normalized))throw Error("Informe um CNPJ válido antes de importar o certificado.");
  var holder=PfxCodec.HolderTaxId(certificate);
  if(holder?.Length!=14)throw Error("Selecione um e-CNPJ. Este certificado é de pessoa física ou não possui CNPJ identificável.");
  if(holder!=normalized)throw Error($"O certificado pertence ao CNPJ {Cnpj.Format(holder)}, diferente do CNPJ da empresa. Selecione o arquivo correto.");
  if(DateTime.UtcNow<certificate.NotBefore.ToUniversalTime()||DateTime.UtcNow>=certificate.NotAfter.ToUniversalTime())throw Error("O certificado está vencido ou ainda não entrou em vigor.");
  return holder;
 }
 public async Task<CompanyRow> SaveAsync(CompanyRow? previous,CompanyInput input,ReadOnlyMemory<byte> pfx,ReadOnlyMemory<char> password,CancellationToken ct=default)
 {
  input=CompanyValidation.Normalize(input);
  if(pfx.IsEmpty)return previous is null?await companies.CreateAsync(input,Guid.NewGuid(),ct):await companies.UpdateAsync(previous.Id,previous.Revision,input,Guid.NewGuid(),ct);
  using var loaded=PfxCodec.Load(pfx.Span,password.Span);var holder=ValidateCertificate(loaded,input.Cnpj);
  string reference=Guid.NewGuid().ToString("N");byte[]? envelope=null;bool committed=false,stored=false;
  await using var transaction=await db.Database.BeginTransactionAsync(ct);
  try
  {
   var correlation=Guid.NewGuid();
   var company=previous is null?await companies.CreateAsync(input,correlation,ct):await companies.UpdateAsync(previous.Id,previous.Revision,input,correlation,ct);
   envelope=PfxCodec.Pack(pfx.Span,password.Span);stored=true;await store.StoreAsync(reference,envelope,ct);
   var certificate=new Certificate{OrganizationId=db.OrganizationId,Label=$"e-CNPJ {Cnpj.Format(holder)}",Subject=loaded.Subject,HolderTaxId=holder,Thumbprint=loaded.GetCertHashString(HashAlgorithmName.SHA256),ValidFromUtc=loaded.NotBefore.ToUniversalTime(),ValidToUtc=loaded.NotAfter.ToUniversalTime(),Use=CertificateUse.Participant,SecureStoreReference=reference};
   db.Certificates.Add(certificate);
   foreach(var provider in new[]{NfeSources.National,CteSource.Code})
   {
    var link=await db.CertificateAssociations.SingleOrDefaultAsync(x=>x.CompanyId==company.Id&&x.ProviderCode==provider,ct);
    if(link is null)db.CertificateAssociations.Add(new(){OrganizationId=db.OrganizationId,CompanyId=company.Id,CertificateId=certificate.Id,ProviderCode=provider});
    else link.CertificateId=certificate.Id;
   }
   db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=AuditAction.CertificateAdded,EntityId=certificate.Id,CorrelationId=correlation,ResultCode="COMPANY_CERTIFICATE_SAVED"});
   db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=AuditAction.CertificateAssociated,EntityId=certificate.Id,CorrelationId=correlation,ResultCode="NATIONAL_LINKS_SAVED"});
   await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);committed=true;return company;
  }
  finally
  {
   if(envelope is not null)CryptographicOperations.ZeroMemory(envelope);
   if(!committed)
   {
    await transaction.RollbackAsync(CancellationToken.None);await transaction.DisposeAsync();db.ChangeTracker.Clear();
    if(stored)
    {
     try{await store.RemoveAsync(reference,CancellationToken.None);}
     catch
     {
      // Recoverable encrypted orphan; reuse the existing startup cleanup mechanism.
      try{db.ApplicationSettings.Add(new(){OrganizationId=db.OrganizationId,Key="CertificateCleanup/"+reference,Value=reference});await db.SaveChangesAsync(CancellationToken.None);}catch{ /* Original failure remains visible. */ }
     }
    }
   }
  }
 }
 private static CompanyException Error(string text)=>new(CompanyError.InvalidData,text);
}
