using System.Data.Common;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application;
using WgsDfe.Application.Certificates;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Certificates;
public sealed class CertificateService(WgsDbContext db,ISecretStore store):ICertificateService,ICertificateKeySource
{
 private const string CleanupPrefix="CertificateCleanup/";
 private CertificateRow Row(Certificate c,int links=0)=>new(c.Id,c.Revision,c.Label,c.Subject,c.HolderTaxId,c.Thumbprint,c.ValidFromUtc,c.ValidToUtc,c.Use,links);
 private void Audit(AuditAction action,Guid id,Guid correlation,string result)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode=result});
 private async Task<Certificate> Find(Guid id,CancellationToken ct)=>await db.Certificates.SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new CertificateException(CertificateError.NotFound);
 private static void CheckRevision(Certificate c,long revision){if(c.Revision!=revision)throw new CertificateException(CertificateError.Conflict);}
 private async Task Save(CancellationToken ct)
 {try{await db.SaveChangesAsync(ct);}catch(DbUpdateException){throw new CertificateException(CertificateError.Conflict);}}
 public async Task<IReadOnlyList<CertificateRow>> ListAsync(CancellationToken ct=default)
 {
  var certificates=await db.Certificates.AsNoTracking().OrderBy(x=>x.Label).ToListAsync(ct);var links=await db.CertificateAssociations.AsNoTracking().ToListAsync(ct);
  return certificates.Select(c=>Row(c,links.Where(l=>l.CertificateId==c.Id).Select(l=>l.CompanyId).Distinct().Count())).ToArray();
 }
 public async Task<CertificateMutationResult> ImportAsync(string path,ReadOnlyMemory<char> password,string label,CertificateUse use,Guid correlationId,Guid? replaceId=null,long? revision=null,CancellationToken ct=default)
 {
  label=label.Trim();if(label.Length is <1 or >200||label.Any(char.IsControl)||!Enum.IsDefined(use)||password.Length>4096||!new[]{".pfx",".p12"}.Contains(Path.GetExtension(path).ToLowerInvariant()))throw new CertificateException(CertificateError.InvalidInput);
  byte[]? pfx=null,secret=null;string? newReference=null;bool committed=false;
  try
  {
   var cert=replaceId.HasValue?await Find(replaceId.Value,ct):new Certificate{OrganizationId=db.OrganizationId};if(replaceId.HasValue)CheckRevision(cert,revision??-1);
   int links=replaceId.HasValue?await db.CertificateAssociations.Where(x=>x.CertificateId==cert.Id).Select(x=>x.CompanyId).Distinct().CountAsync(ct):0;
   await using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
   {if(file.Length is <1 or >PfxCodec.MaximumPfxBytes)throw new CertificateException(CertificateError.InvalidInput);pfx=new byte[(int)file.Length];await file.ReadExactlyAsync(pfx,ct);}
   using var loaded=PfxCodec.Load(pfx,password.Span);var oldReference=cert.SecureStoreReference;newReference=Guid.NewGuid().ToString("N");
   secret=PfxCodec.Pack(pfx,password.Span);await store.StoreAsync(newReference,secret,ct);
   cert.Label=label;cert.Use=use;cert.Subject=loaded.Subject;cert.HolderTaxId=PfxCodec.HolderTaxId(loaded);cert.Thumbprint=loaded.GetCertHashString(HashAlgorithmName.SHA256);cert.ValidFromUtc=loaded.NotBefore.ToUniversalTime();cert.ValidToUtc=loaded.NotAfter.ToUniversalTime();cert.SecureStoreReference=newReference;
   if(!replaceId.HasValue)db.Certificates.Add(cert);else QueueCleanup(oldReference);
   Audit(replaceId.HasValue?AuditAction.CertificateReplaced:AuditAction.CertificateAdded,cert.Id,correlationId,"CERTIFICATE_SAVED");await Save(ct);committed=true;
   return new(Row(cert,links),await HasCleanupPending());
  }
  catch(Exception e)when(e is IOException or UnauthorizedAccessException){throw new CertificateException(CertificateError.InvalidInput);}
  finally
  {
   if(pfx is not null)CryptographicOperations.ZeroMemory(pfx);if(secret is not null)CryptographicOperations.ZeroMemory(secret);
   if(!committed&&newReference is not null){try{await store.RemoveAsync(newReference,CancellationToken.None);}catch(CertificateException){/* Blob cifrado órfão se compensação falhar. */}}
  }
 }
 private async Task<bool> HasCleanupPending()
 {try{return await CleanupAsync(CancellationToken.None)>0;}catch(Exception e)when(e is DbException or DbUpdateException){return true;}}
 private void QueueCleanup(string reference)
 {if(Guid.TryParseExact(reference,"N",out _))db.ApplicationSettings.Add(new(){OrganizationId=db.OrganizationId,Key=CleanupPrefix+reference,Value=reference});}
 public async Task<CertificateMutationResult> RemoveAsync(Guid id,long revision,Guid correlationId,CancellationToken ct=default)
 {
  var certificate=await Find(id,ct);CheckRevision(certificate,revision);var links=await db.CertificateAssociations.Where(x=>x.CertificateId==id).ToListAsync(ct);
  db.CertificateAssociations.RemoveRange(links);db.Certificates.Remove(certificate);QueueCleanup(certificate.SecureStoreReference);Audit(AuditAction.CertificateRemoved,id,correlationId,"CERTIFICATE_REMOVED");await Save(ct);
  return new(null,await HasCleanupPending());
 }
 public async Task<X509Certificate2> OpenAsync(Guid certificateId,CancellationToken ct=default)
 {
  var c=await Find(certificateId,ct);var bytes=await store.ReadAsync(c.SecureStoreReference,ct);
  try{var loaded=PfxCodec.Unpack(bytes,forTls:true);if(!string.Equals(loaded.GetCertHashString(HashAlgorithmName.SHA256),c.Thumbprint,StringComparison.Ordinal)){loaded.Dispose();throw new CertificateException(CertificateError.StoreUnavailable);}return loaded;}
  finally{CryptographicOperations.ZeroMemory(bytes);}
 }
 public async Task<CertificateTestResult> TestAsync(Guid id,Guid correlationId,CancellationToken ct=default)
 {
  var c=await Find(id,ct);using var loaded=await OpenAsync(id,ct);PfxCodec.ProvePrivateKey(loaded);var validity=Row(c).ValidityAt(DateTime.UtcNow);
  Audit(AuditAction.CertificateTested,id,correlationId,"LOCAL_KEY_OK");await Save(ct);
  return new(validity,true,$"Chave privada testada. Validade: {Row(c).Status}. Cadeia ICP-Brasil, revogação e autorização fiscal ainda não verificadas.");
 }
 public async Task<IReadOnlyList<CertificateLink>> LinksAsync(Guid id,CancellationToken ct=default)
 {
  await Find(id,ct);return await(from l in db.CertificateAssociations.AsNoTracking() join c in db.Companies.AsNoTracking() on l.CompanyId equals c.Id where l.CertificateId==id orderby c.LegalName select new CertificateLink(l.Id,l.Revision,c.Id,c.LegalName,c.Cnpj,l.ProviderCode)).ToListAsync(ct);
 }
 public async Task AssociateAsync(Guid id,long revision,IReadOnlyCollection<Guid> companies,string providerCode,Guid correlationId,CancellationToken ct=default)
 {
  if(companies.Count is <1 or >10000||providerCode.Length is <1 or >64||providerCode.Any(c=>!(char.IsAsciiLetterOrDigit(c)||c is '_' or '-')))throw new CertificateException(CertificateError.InvalidInput);
  providerCode=providerCode.ToUpperInvariant();var cert=await Find(id,ct);CheckRevision(cert,revision);var ids=companies.Distinct().ToArray();if(await db.Companies.CountAsync(c=>ids.Contains(c.Id),ct)!=ids.Length)throw new CertificateException(CertificateError.NotFound);
  foreach(var companyId in ids)
  {
   var link=await db.CertificateAssociations.SingleOrDefaultAsync(x=>x.CompanyId==companyId&&x.ProviderCode==providerCode,ct);
   if(link is not null&&link.CertificateId!=id)throw new CertificateException(CertificateError.Conflict);
   if(link is null)db.CertificateAssociations.Add(new(){OrganizationId=db.OrganizationId,CompanyId=companyId,CertificateId=id,ProviderCode=providerCode});
  }
  db.Entry(cert).Property(x=>x.UpdatedAtUtc).IsModified=true;Audit(AuditAction.CertificateAssociated,id,correlationId,"LOCAL_ASSOCIATION_SAVED");await Save(ct);
 }
 public async Task UnlinkAsync(Guid linkId,long revision,Guid correlationId,CancellationToken ct=default)
 {
  var link=await db.CertificateAssociations.SingleOrDefaultAsync(x=>x.Id==linkId,ct)??throw new CertificateException(CertificateError.NotFound);if(link.Revision!=revision)throw new CertificateException(CertificateError.Conflict);
  var cert=await Find(link.CertificateId,ct);db.Entry(cert).Property(x=>x.UpdatedAtUtc).IsModified=true;db.CertificateAssociations.Remove(link);Audit(AuditAction.CertificateUnlinked,link.CertificateId,correlationId,"LOCAL_ASSOCIATION_REMOVED");await Save(ct);
 }
 public async Task<int> CleanupAsync(CancellationToken ct=default)
 {
  var items=await db.ApplicationSettings.Where(x=>x.Key.StartsWith(CleanupPrefix)).ToListAsync(ct);
  foreach(var item in items)
  {
   if(await db.Certificates.AnyAsync(x=>x.SecureStoreReference==item.Value,ct))continue;
   try{await store.RemoveAsync(item.Value,ct);db.ApplicationSettings.Remove(item);await db.SaveChangesAsync(ct);}
   catch(CertificateException){continue;}catch(DbUpdateException){db.Entry(item).State=EntityState.Unchanged;}
  }
  return await db.ApplicationSettings.CountAsync(x=>x.Key.StartsWith(CleanupPrefix),ct);
 }
}
