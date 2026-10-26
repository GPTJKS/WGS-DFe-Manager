using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Formats.Asn1;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Certificates;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Nfe;
public sealed class NfeCredentialSource(WgsDbContext db,ICertificateKeySource keys,ProviderOptions options):INfeCredentialSource
{
 private static readonly Dictionary<string,string> States=new(){["RO"]="11",["AC"]="12",["AM"]="13",["RR"]="14",["PA"]="15",["AP"]="16",["TO"]="17",["MA"]="21",["PI"]="22",["CE"]="23",["RN"]="24",["PB"]="25",["PE"]="26",["AL"]="27",["SE"]="28",["BA"]="29",["MG"]="31",["ES"]="32",["RJ"]="33",["SP"]="35",["PR"]="41",["SC"]="42",["RS"]="43",["MS"]="50",["MT"]="51",["GO"]="52",["DF"]="53"};
 public async Task<NfeCredential> OpenAsync(DocumentRequest request,string provider,CancellationToken ct)
 {
  if(!options.EnableExternalRequests)throw new NfeException("EXTERNAL_REQUESTS_DISABLED");
  if(request.OrganizationId!=db.OrganizationId)throw new NfeException("ORGANIZATION_MISMATCH");
  var company=await db.Companies.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==request.CompanyId,ct)??throw new NfeException("COMPANY_NOT_FOUND");
  if(!company.IsActive||(company.EnabledDocuments&(provider==WgsDfe.Application.Cte.CteSource.Code?EnabledDocuments.CTe|EnabledDocuments.CTeOS:NfeSources.Documents(provider)))==0)throw new NfeException("COMPANY_NFE_DISABLED");
  if(!States.TryGetValue(company.State,out var state)||NfeSources.IsSantaCatarina(provider)&&company.State!="SC")throw new NfeException("COMPANY_STATE_UNSUPPORTED");
  var metadata=await new WgsDfe.Infrastructure.Certificates.CertificateSelection(db).ResolveAsync(company,provider,ct);
  if(metadata.ValidFromUtc>DateTime.UtcNow||metadata.ValidToUtc<=DateTime.UtcNow)throw new NfeException("CERTIFICATE_OUTSIDE_VALIDITY");
  ValidateIdentity(provider,company.Cnpj,metadata.HolderTaxId,metadata.Use);
  X509Certificate2 certificate;
  try{certificate=await keys.OpenAsync(metadata.Id,ct);}catch(CertificateException){throw new NfeException("CERTIFICATE_STORE_UNAVAILABLE");}
  try
  {
   ValidateIdentity(provider,company.Cnpj,WgsDfe.Infrastructure.Certificates.PfxCodec.HolderTaxId(certificate),metadata.Use);
   if(!certificate.HasPrivateKey||DateTime.UtcNow<certificate.NotBefore.ToUniversalTime()||DateTime.UtcNow>=certificate.NotAfter.ToUniversalTime())throw new NfeException("CERTIFICATE_KEY_OR_VALIDITY_INVALID");
   if(!HasIcpPolicy(certificate))throw new NfeException("ICP_BRASIL_POLICY_REQUIRED");
   using var chain=new X509Chain();chain.ChainPolicy.RevocationMode=X509RevocationMode.Online;chain.ChainPolicy.RevocationFlag=X509RevocationFlag.ExcludeRoot;chain.ChainPolicy.UrlRetrievalTimeout=TimeSpan.FromSeconds(15);chain.ChainPolicy.VerificationFlags=X509VerificationFlags.NoFlag;
   if(!chain.Build(certificate))throw new NfeException("CERTIFICATE_CHAIN_OR_REVOCATION_FAILED");
   return new(company.Cnpj,state,certificate);
  }
  catch{certificate.Dispose();throw;}
 }
 public static void ValidateIdentity(string provider,string companyTaxId,string? holder,CertificateUse use)
 {
  if(NfeSources.IsSantaCatarina(provider))
  {if(use!=CertificateUse.Accountant||holder is null)throw new NfeException("ACCOUNTANT_CERTIFICATE_REQUIRED");}
  else if(provider==NfeSources.National||provider==WgsDfe.Application.Cte.CteSource.Code)
  {if(use!=CertificateUse.Participant||holder?.Length!=14||!string.Equals(holder[..8],companyTaxId[..8],StringComparison.Ordinal))throw new NfeException("PARTICIPANT_ROOT_CNPJ_MISMATCH");}
  else throw new NfeException("INVALID_PROVIDER");
 }
 private static bool HasIcpPolicy(X509Certificate2 certificate)
 {
  var policies=certificate.Extensions["2.5.29.32"];if(policies is null)return false;
  try{var reader=new AsnReader(policies.RawData,AsnEncodingRules.DER);var seq=reader.ReadSequence();while(seq.HasData){var item=seq.ReadSequence();if(item.ReadObjectIdentifier().StartsWith("2.16.76.1.2.",StringComparison.Ordinal))return true;}return false;}
  catch(AsnContentException){return false;}
 }
}
