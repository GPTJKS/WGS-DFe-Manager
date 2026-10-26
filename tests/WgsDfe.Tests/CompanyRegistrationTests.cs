using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application;
using WgsDfe.Application.Companies;
using WgsDfe.Application.Certificates;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Companies;
using Xunit;
namespace WgsDfe.Tests;
public sealed class CompanyRegistrationTests
{
 private const string Id="12345678000195";
 private static readonly CompanyInput Input=new("Empresa teste",null,Id,null,"SC","Joinville",true,EnabledDocuments.All,null,null);
 private static X509Certificate2 Make(string? id=Id,int offset=-1,int days=365,string oid="2.16.76.1.3.3")
 {
  using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=Synthetic registration test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  if(id is not null){var writer=new AsnWriter(AsnEncodingRules.DER);var tag=new Asn1Tag(TagClass.ContextSpecific,0,true);using(writer.PushSequence())using(writer.PushSequence(tag)){writer.WriteObjectIdentifier(oid);using(writer.PushSequence(tag))writer.WriteCharacterString(UniversalTagNumber.PrintableString,id);}request.CertificateExtensions.Add(new X509Extension("2.5.29.17",writer.Encode(),false));}
  return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(offset),DateTimeOffset.UtcNow.AddDays(offset+days));
 }
 [Fact]public void ExactCnpjAccepted(){using var c=Make();Assert.Equal(Id,CompanyRegistrationService.ValidateCertificate(c,"12.345.678/0001-95"));}
 [Theory][InlineData("12345678000276")][InlineData("11222333000181")][InlineData(null)]
 public void OtherBranchOtherCompanyAndMissingSanRejected(string? id){using var c=Make(id);Assert.Throws<CompanyException>(()=>CompanyRegistrationService.ValidateCertificate(c,Id));}
 [Fact]public void EcpfRejected(){using var c=Make("0101199052998224725",oid:"2.16.76.1.3.1");Assert.Throws<CompanyException>(()=>CompanyRegistrationService.ValidateCertificate(c,Id));}
 [Theory][InlineData(-100,5)][InlineData(5,100)]public void OutsideValidityRejected(int offset,int days){using var c=Make(offset:offset,days:days);Assert.Throws<CompanyException>(()=>CompanyRegistrationService.ValidateCertificate(c,Id));}
 [Fact]public async Task SavesCompanyCertificateAndNationalLinksTogether()
 {
  await using var h=await Harness.Create();using var c=Make();var row=await h.Service.SaveAsync(null,Input,c.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory());
  Assert.Single(await h.Db.Companies.ToListAsync());Assert.Single(h.Store.Values);Assert.Single(await h.Db.Certificates.ToListAsync());
  var links=await h.Db.CertificateAssociations.ToListAsync();Assert.Equal(2,links.Count);Assert.All(links,x=>Assert.Equal(row.Id,x.CompanyId));Assert.Equal(new[]{"CTE","NFE_NACIONAL"},links.Select(x=>x.ProviderCode).OrderBy(x=>x));
 }
 [Fact]public async Task WrongPasswordAndMismatchLeaveNothingSaved()
 {
  await using var h=await Harness.Create();using var c=Make();var bytes=c.Export(X509ContentType.Pkcs12,"pw");
  await Assert.ThrowsAsync<CertificateException>(()=>h.Service.SaveAsync(null,Input,bytes,"wrong".AsMemory()));
  await Assert.ThrowsAsync<CompanyException>(()=>h.Service.SaveAsync(null,Input with{Cnpj="11222333000181"},bytes,"pw".AsMemory()));
  Assert.Empty(await h.Db.Companies.ToListAsync());Assert.Empty(h.Store.Values);
 }
 [Fact]public async Task StoreFailureRollsBackCompany()
 {
  await using var h=await Harness.Create();using var c=Make();h.Store.Fail=true;
  await Assert.ThrowsAsync<IOException>(()=>h.Service.SaveAsync(null,Input,c.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory()));
  Assert.Empty(await h.Db.Companies.ToListAsync());Assert.Empty(await h.Db.Certificates.ToListAsync());Assert.Empty(h.Store.Values);
 }
 [Fact]public async Task DatabaseFailureRemovesNewSecretAndRollsBackCompany()
 {
  await using var h=await Harness.Create();using var c=Make();h.Store.AfterWrite=()=>{h.Db.ApplicationSettings.Add(new(){OrganizationId=h.Org,Key="duplicate",Value="1"});h.Db.ApplicationSettings.Add(new(){OrganizationId=h.Org,Key="duplicate",Value="2"});};
  await Assert.ThrowsAsync<DbUpdateException>(()=>h.Service.SaveAsync(null,Input,c.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory()));
  Assert.Empty(await h.Db.Companies.ToListAsync());Assert.Empty(await h.Db.Certificates.ToListAsync());Assert.Empty(h.Store.Values);
 }
 [Fact]public async Task EditingPreservesAccountantLinksAndOldCertificate()
 {
  await using var h=await Harness.Create();using var c=Make();var row=await h.Service.SaveAsync(null,Input,c.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory());var old=await h.Db.Certificates.SingleAsync();
  h.Db.CertificateAssociations.Add(new(){OrganizationId=h.Org,CompanyId=row.Id,CertificateId=old.Id,ProviderCode="SEF_SC_NFE"});await h.Db.SaveChangesAsync();
  using var next=Make();await h.Service.SaveAsync(row,Input with{LegalName="Atualizada"},next.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory());
  Assert.Equal(2,await h.Db.Certificates.CountAsync());Assert.Equal(old.Id,(await h.Db.CertificateAssociations.SingleAsync(x=>x.ProviderCode=="SEF_SC_NFE")).CertificateId);
  Assert.NotEqual(old.Id,(await h.Db.CertificateAssociations.SingleAsync(x=>x.ProviderCode=="NFE_NACIONAL")).CertificateId);
 }
 [Fact]public async Task OptionalCertificateAndDuplicateCnpj()
 {
  await using var h=await Harness.Create();await h.Service.SaveAsync(null,Input,ReadOnlyMemory<byte>.Empty,ReadOnlyMemory<char>.Empty);
  using var c=Make();await Assert.ThrowsAsync<CompanyException>(()=>h.Service.SaveAsync(null,Input,c.Export(X509ContentType.Pkcs12,"pw"),"pw".AsMemory()));Assert.Single(await h.Db.Companies.ToListAsync());Assert.Empty(h.Store.Values);
 }
 [Fact]public void LookupChecksReturnedIdentity()
 {
  var good=Encoding.UTF8.GetBytes("{\"cnpj\":\"12345678000195\",\"razao_social\":\"Empresa\",\"uf\":\"SC\",\"municipio\":\"Joinville\"}");
  Assert.Equal("Joinville",CompanyLookup.Parse(good,Id).City);Assert.Throws<CompanyException>(()=>CompanyLookup.Parse(good,"11222333000181"));
 }
 [Theory][InlineData(404)][InlineData(429)][InlineData(503)]public async Task LookupErrorsAreActionable(int status)
 {using var client=new HttpClient(new Handler(status));var ex=await Assert.ThrowsAsync<CompanyException>(()=>new CompanyLookup(client).FindAsync(Id));Assert.NotEmpty(ex.Message);}
 [Theory][InlineData("{}")] [InlineData("[]")] [InlineData("{\"cnpj\":\"12345678000195\"}")]
 public void IncompleteLookupRejected(string body){Assert.Throws<CompanyException>(()=>CompanyLookup.Parse(Encoding.UTF8.GetBytes(body),Id));}
 [Fact]public async Task LookupFallsBackAndCachesWithoutRepeatingCalls()
 {
  var handler=new LookupHandler();using var client=new HttpClient(handler);var lookup=new CompanyLookup(client);
  var first=await lookup.FindAsync(Id);Assert.Equal("ReceitaWS",first.Source);Assert.False(first.Cached);
  Assert.True((await lookup.FindAsync(Id)).Cached);Assert.Equal(2,handler.Calls);
  await Assert.ThrowsAsync<CompanyException>(()=>lookup.FindAsync("11222333000181"));Assert.Equal(2,handler.Calls);
 }
 private sealed class LookupHandler:HttpMessageHandler
 {
  public int Calls;
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
  {
   Calls++;if(request.RequestUri!.Host=="brasilapi.com.br"){var limited=new HttpResponseMessage(HttpStatusCode.TooManyRequests);limited.Headers.RetryAfter=new(TimeSpan.FromMinutes(2));return Task.FromResult(limited);}
   return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"cnpj\":\"12.345.678/0001-95\",\"nome\":\"Empresa\",\"uf\":\"SC\"}")});
  }
 }
 [Fact]public async Task AutomaticCertificateSelectionCannotCrossOrganizations()
 {
  await using var h=await Harness.Create();await h.Service.SaveAsync(null,Input,ReadOnlyMemory<byte>.Empty,ReadOnlyMemory<char>.Empty);var company=await h.Db.Companies.SingleAsync();
  var other=Guid.NewGuid();
  await using var otherDb=DatabaseFactory.Create(h.Db.Database.GetDbConnection().DataSource,other);otherDb.Organizations.Add(new(){Id=other,Name="Outra"});await otherDb.SaveChangesAsync();var certificate=Cert(h);certificate.OrganizationId=other;otherDb.Certificates.Add(certificate);await otherDb.SaveChangesAsync();
  var selection=new WgsDfe.Infrastructure.Certificates.CertificateSelection(h.Db);
  Assert.Equal("PROVIDER_CERTIFICATE_REQUIRED",(await Assert.ThrowsAsync<WgsDfe.Application.Nfe.NfeException>(()=>selection.ResolveAsync(company,"NFE_NACIONAL"))).Code);
  company.OrganizationId=other;Assert.Equal("ORGANIZATION_MISMATCH",(await Assert.ThrowsAsync<WgsDfe.Application.Nfe.NfeException>(()=>selection.ResolveAsync(company,"NFE_NACIONAL"))).Code);
 }
 private static Certificate Cert(Harness h,string holder=Id,CertificateUse use=CertificateUse.Participant,int days=60)=>new(){OrganizationId=h.Org,HolderTaxId=holder,Use=use,Label="Teste",Thumbprint=Guid.NewGuid().ToString("N"),SecureStoreReference=Guid.NewGuid().ToString("N"),ValidFromUtc=DateTime.UtcNow.AddDays(-2),ValidToUtc=DateTime.UtcNow.AddDays(days)};
 [Fact]public async Task CertificateIsSelectedWithoutManualAssociationAndUnrelatedLinkIgnored()
 {
  await using var h=await Harness.Create();await h.Service.SaveAsync(null,Input,ReadOnlyMemory<byte>.Empty,ReadOnlyMemory<char>.Empty);var company=await h.Db.Companies.SingleAsync();
  var matching=Cert(h);var wrong=Cert(h,"11222333000181");h.Db.Certificates.AddRange(matching,wrong);await h.Db.SaveChangesAsync();
  var selection=new WgsDfe.Infrastructure.Certificates.CertificateSelection(h.Db);
  Assert.Equal(matching.Id,(await selection.ResolveAsync(company,"NFE_NACIONAL")).Id);
  h.Db.CertificateAssociations.Add(new(){OrganizationId=h.Org,CompanyId=company.Id,CertificateId=wrong.Id,ProviderCode="CTE"});await h.Db.SaveChangesAsync();
  Assert.Equal(matching.Id,(await selection.ResolveAsync(company,"CTE")).Id);
 }
 [Fact]public async Task ExactCnpjPreferredAndExpiredNeverSelected()
 {
  await using var h=await Harness.Create();await h.Service.SaveAsync(null,Input,ReadOnlyMemory<byte>.Empty,ReadOnlyMemory<char>.Empty);var company=await h.Db.Companies.SingleAsync();
  var branch=Cert(h,"12345678000276",days:300);var exact=Cert(h);var expired=Cert(h,days:-1);h.Db.Certificates.AddRange(branch,exact,expired);await h.Db.SaveChangesAsync();
  var selection=new WgsDfe.Infrastructure.Certificates.CertificateSelection(h.Db);Assert.Equal(exact.Id,(await selection.ResolveAsync(company,"NFE_NACIONAL")).Id);
  h.Db.Certificates.RemoveRange(branch,exact);await h.Db.SaveChangesAsync();Assert.Equal("CERTIFICATE_OUTSIDE_VALIDITY",(await Assert.ThrowsAsync<WgsDfe.Application.Nfe.NfeException>(()=>selection.ResolveAsync(company,"NFE_NACIONAL"))).Code);
 }
 [Fact]public async Task AccountantChoiceAppliesAcrossScSourcesAndDoesNotSwitchWhenExpired()
 {
  await using var h=await Harness.Create();await h.Service.SaveAsync(null,Input,ReadOnlyMemory<byte>.Empty,ReadOnlyMemory<char>.Empty);var company=await h.Db.Companies.SingleAsync();
  var a=Cert(h,"52998224725",CertificateUse.Accountant);var b=Cert(h,"11222333000181",CertificateUse.Accountant);h.Db.Certificates.AddRange(a,b);await h.Db.SaveChangesAsync();
  var selection=new WgsDfe.Infrastructure.Certificates.CertificateSelection(h.Db);
  Assert.Equal("CHOOSE_ACCOUNTANT_CERTIFICATE",(await Assert.ThrowsAsync<WgsDfe.Application.Nfe.NfeException>(()=>selection.ResolveAsync(company,"SEF_SC_NFE"))).Code);
  await selection.SetAccountantAsync(b.Id);foreach(var provider in new[]{"SEF_SC_NFE","SEF_SC_NFCE","NFCOM"})Assert.Equal(b.Id,(await selection.ResolveAsync(company,provider)).Id);
  b.ValidToUtc=DateTime.UtcNow.AddDays(-1);await h.Db.SaveChangesAsync();Assert.Equal("DEFAULT_ACCOUNTANT_UNAVAILABLE",(await Assert.ThrowsAsync<WgsDfe.Application.Nfe.NfeException>(()=>selection.ResolveAsync(company,"SEF_SC_NFE"))).Code);
 }
 private sealed class Handler(int status):HttpMessageHandler {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage((HttpStatusCode)status));}
 private sealed class Store:ISecretStore
 {
  public readonly Dictionary<string,byte[]> Values=new();public bool Fail;public Action? AfterWrite;
  public Task StoreAsync(string key,ReadOnlyMemory<byte> bytes,CancellationToken ct){if(Fail)throw new IOException("Synthetic failure");Values.Add(key,bytes.ToArray());AfterWrite?.Invoke();return Task.CompletedTask;}
  public Task<byte[]> ReadAsync(string key,CancellationToken ct)=>Task.FromResult(Values[key].ToArray());
  public Task RemoveAsync(string key,CancellationToken ct){Values.Remove(key);return Task.CompletedTask;}
 }
 private sealed class Harness:IAsyncDisposable
 {
  public Guid Org=Guid.NewGuid();public WgsDbContext Db=null!;public Store Store=new();public CompanyRegistrationService Service=null!;private string directory="";
  public static async Task<Harness> Create(){var h=new Harness();h.directory=Path.Combine(Path.GetTempPath(),"wgs-registration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(h.directory);h.Db=DatabaseFactory.Create(Path.Combine(h.directory,"test.db"),h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Test"});await h.Db.SaveChangesAsync();h.Service=new(h.Db,new CompanyService(h.Db),h.Store);return h;}
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(directory,true);}
 }
}
