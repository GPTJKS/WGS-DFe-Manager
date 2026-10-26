using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Nfe;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Certificates;
// Resolves existing certificates without requiring provider-by-provider manual links.
public sealed class CertificateSelection(WgsDbContext db)
{
 public const string AccountantKey="Certificates/DefaultAccountant";
 public async Task SetAccountantAsync(Guid id,CancellationToken ct=default)
 {
  var certificate=await db.Certificates.SingleOrDefaultAsync(x=>x.Id==id,ct);
  if(certificate is null||certificate.Use!=CertificateUse.Accountant||certificate.HolderTaxId is null)throw new NfeException("ACCOUNTANT_CERTIFICATE_REQUIRED");
  if(certificate.ValidFromUtc>DateTime.UtcNow||certificate.ValidToUtc<=DateTime.UtcNow)throw new NfeException("CERTIFICATE_OUTSIDE_VALIDITY");
  var setting=await db.ApplicationSettings.SingleOrDefaultAsync(x=>x.Key==AccountantKey,ct);
  if(setting is null)db.ApplicationSettings.Add(new(){OrganizationId=db.OrganizationId,Key=AccountantKey,Value=id.ToString()});else setting.Value=id.ToString();
  await db.SaveChangesAsync(ct);
 }
 public async Task<Certificate> ResolveAsync(Company company,string provider,CancellationToken ct=default)
 {
  if(company.OrganizationId!=db.OrganizationId)throw new NfeException("ORGANIZATION_MISMATCH");
  if(!NfeSources.IsKnown(provider)&&provider!=WgsDfe.Application.Cte.CteSource.Code)throw new NfeException("INVALID_PROVIDER");
  var all=await db.Certificates.AsNoTracking().ToListAsync(ct);var now=DateTime.UtcNow;
  bool Compatible(Certificate c)=>NfeSources.IsSantaCatarina(provider)?c.Use==CertificateUse.Accountant&&c.HolderTaxId is not null:c.Use==CertificateUse.Participant&&c.HolderTaxId?.Length==14&&company.Cnpj.Length==14&&c.HolderTaxId[..8]==company.Cnpj[..8];
  bool Valid(Certificate c)=>c.ValidFromUtc<=now&&c.ValidToUtc>now;
  var matches=all.Where(Compatible).ToArray();var valid=matches.Where(Valid).ToArray();
  var link=await db.CertificateAssociations.AsNoTracking().SingleOrDefaultAsync(x=>x.CompanyId==company.Id&&x.ProviderCode==provider,ct);
  var linked=valid.FirstOrDefault(x=>x.Id==link?.CertificateId);
  if(NfeSources.IsSantaCatarina(provider))
  {
   var configured=await db.ApplicationSettings.AsNoTracking().SingleOrDefaultAsync(x=>x.Key==AccountantKey,ct);
   if(Guid.TryParse(configured?.Value,out var preferred))
   {
    var selected=valid.FirstOrDefault(x=>x.Id==preferred);if(selected is not null)return selected;
    // Do not silently switch accountants when an explicitly selected one expires.
    throw new NfeException("DEFAULT_ACCOUNTANT_UNAVAILABLE");
   }
   if(linked is not null)return linked;
   var holders=valid.Select(x=>x.HolderTaxId).Distinct().ToArray();
   if(holders.Length>1)throw new NfeException("CHOOSE_ACCOUNTANT_CERTIFICATE");
   if(valid.Length>0)return valid.OrderByDescending(x=>x.ValidToUtc).ThenByDescending(x=>x.UpdatedAtUtc).ThenBy(x=>x.Id).First();
   throw new NfeException(matches.Length>0?"CERTIFICATE_OUTSIDE_VALIDITY":"ACCOUNTANT_CERTIFICATE_REQUIRED");
  }
  // Prefer the full company CNPJ; retain an existing compatible choice within that group.
  var exact=valid.Where(x=>x.HolderTaxId==company.Cnpj).ToArray();
  if(exact.Length>0)return exact.FirstOrDefault(x=>x.Id==linked?.Id)??exact.OrderByDescending(x=>x.ValidToUtc).ThenByDescending(x=>x.UpdatedAtUtc).ThenBy(x=>x.Id).First();
  if(linked is not null)return linked;
  if(valid.Length>0)return valid.OrderByDescending(x=>x.ValidToUtc).ThenByDescending(x=>x.UpdatedAtUtc).ThenBy(x=>x.Id).First();
  throw new NfeException(matches.Length>0?"CERTIFICATE_OUTSIDE_VALIDITY":"PROVIDER_CERTIFICATE_REQUIRED");
 }
}
