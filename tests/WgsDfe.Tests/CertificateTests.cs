using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WgsDfe.Application;
using WgsDfe.Application.Certificates;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Certificates;
using Xunit;
namespace WgsDfe.Tests;
public sealed class CertificateTests
{
 private const string Password="synthetic-test-only-ç😀";
 private static X509Certificate2 Make(bool ec=false,int offset=-1,int days=365,string? taxId=null,bool ca=false,string oid="2.16.76.1.3.3",bool octet=false)
 {
  using var rsa=RSA.Create(2048);using var ecdsa=ECDsa.Create(ECCurve.NamedCurves.nistP256);
  var request=ec?new CertificateRequest("CN=Synthetic test",ecdsa,HashAlgorithmName.SHA256):new CertificateRequest("CN=Synthetic test",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  request.CertificateExtensions.Add(new X509BasicConstraintsExtension(ca,false,0,true));
  if(taxId is not null)
  {
   var writer=new AsnWriter(AsnEncodingRules.DER);var tag=new Asn1Tag(TagClass.ContextSpecific,0,true);
   using(writer.PushSequence())using(writer.PushSequence(tag)){writer.WriteObjectIdentifier(oid);using(writer.PushSequence(tag)){if(octet)writer.WriteOctetString(System.Text.Encoding.ASCII.GetBytes(taxId));else writer.WriteCharacterString(UniversalTagNumber.PrintableString,taxId);}}
   request.CertificateExtensions.Add(new X509Extension("2.5.29.17",writer.Encode(),false));
  }
  return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(offset),DateTimeOffset.UtcNow.AddDays(offset+days));
 }
 [Theory][InlineData(false)][InlineData(true)]public void RsaAndEcdsa_CanSign(bool ec)
 {using var c=Make(ec);var bytes=c.Export(X509ContentType.Pkcs12,Password);try{using var loaded=PfxCodec.Load(bytes,Password);Assert.True(loaded.HasPrivateKey);PfxCodec.ProvePrivateKey(loaded);}finally{CryptographicOperations.ZeroMemory(bytes);}}
 [Theory][InlineData(false)][InlineData(true)]public void TlsLoadPreservesIdentityAndPrivateKey(bool ec)
 {using var c=Make(ec);var bytes=PfxCodec.Pack(c.Export(X509ContentType.Pkcs12,Password),Password);try{using var loaded=PfxCodec.Unpack(bytes,forTls:true);Assert.Equal(c.Thumbprint,loaded.Thumbprint);PfxCodec.ProvePrivateKey(loaded);}finally{CryptographicOperations.ZeroMemory(bytes);}}
 [Fact]public void TlsLoadRejectsWrongPassword(){using var c=Make();Assert.Equal(CertificateError.InvalidPfx,Assert.Throws<CertificateException>(()=>PfxCodec.LoadForTls(c.Export(X509ContentType.Pkcs12,Password),"wrong")).Code);}
 [Fact]public void WrongPassword_HasSafeMessage(){using var c=Make();var e=Assert.Throws<CertificateException>(()=>PfxCodec.Load(c.Export(X509ContentType.Pkcs12,Password),"wrong"));Assert.Equal(CertificateError.InvalidPfx,e.Code);Assert.DoesNotContain(Password,e.ToString());}
 [Fact]public void PublicCertificate_IsRejected(){using var c=Make();using var pub=X509CertificateLoader.LoadCertificate(c.RawData);Assert.Equal(CertificateError.NoPrivateKey,Assert.Throws<CertificateException>(()=>PfxCodec.Load(pub.Export(X509ContentType.Pkcs12),"")).Code);}
 [Fact]public void MultipleKeys_AreRejected(){using var a=Make();using var b=Make(true);var bundle=new X509Certificate2Collection(new[]{a,b});Assert.Equal(CertificateError.AmbiguousBundle,Assert.Throws<CertificateException>(()=>PfxCodec.Load(bundle.Export(X509ContentType.Pkcs12,Password)!,Password)).Code);}
 [Fact]public void CertificateAuthority_IsRejected(){using var c=Make(ca:true);Assert.Equal(CertificateError.NoPrivateKey,Assert.Throws<CertificateException>(()=>PfxCodec.Load(c.Export(X509ContentType.Pkcs12,Password),Password)).Code);}
 [Theory][InlineData(false)][InlineData(true)]public void CnpjSan_OctetAndPrintable(bool octet){using var c=Make(taxId:"12345678000195",octet:octet);Assert.Equal("12345678000195",PfxCodec.HolderTaxId(c));}
 [Fact]public void EcpfHolder_DoesNotConfuseResponsible()
 {using var c=Make(taxId:"0101199052998224725"+new string('0',36),oid:"2.16.76.1.3.1",octet:true);Assert.Equal("52998224725",PfxCodec.HolderTaxId(c));using var other=Make(taxId:"0101199052998224725",oid:"2.16.76.1.3.4",octet:true);Assert.Null(PfxCodec.HolderTaxId(other));}
 [Theory][InlineData(null)][InlineData("INVALID")][InlineData("00000000000000")]public void AbsentOrInvalidTaxId_IsNotInferred(string? taxId){using var c=Make(taxId:taxId);Assert.Null(PfxCodec.HolderTaxId(c));}
 [Theory][InlineData(-1,365,CertificateValidity.Valid)][InlineData(-5,20,CertificateValidity.Expiring)][InlineData(-365,300,CertificateValidity.Expired)][InlineData(10,365,CertificateValidity.NotYetValid)][InlineData(-30,30,CertificateValidity.Expired)][InlineData(-1,31,CertificateValidity.Expiring)]
 public void ValidityBoundaries(int offset,int days,CertificateValidity expected){var now=DateTime.UtcNow;var row=new CertificateRow(Guid.NewGuid(),1,"","",null,"",now.AddDays(offset),now.AddDays(offset+days),CertificateUse.Participant,0);Assert.Equal(expected,row.ValidityAt(now));}
 [Fact]public void UnicodePassword_EnvelopeRoundTrip(){using var c=Make();var bytes=PfxCodec.Pack(c.Export(X509ContentType.Pkcs12,Password),Password);try{using var loaded=PfxCodec.Unpack(bytes);Assert.Equal(c.Thumbprint,loaded.Thumbprint);}finally{CryptographicOperations.ZeroMemory(bytes);}}
 [Fact]public void CorruptEnvelopeAndOversizedFile_AreRejected(){Assert.Throws<CertificateException>(()=>PfxCodec.Unpack(new byte[10]));Assert.Throws<CertificateException>(()=>PfxCodec.Load(new byte[PfxCodec.MaximumPfxBytes+1],""));}
 [Fact]public async Task ImportLinkReplaceTestRemove_PreservesCompanies()
 {
  await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;await h.Service.AssociateAsync(row.Id,row.Revision,h.CompanyIds,"DEFAULT",Guid.NewGuid());row=(await h.Service.ListAsync()).Single();Assert.Equal(2,row.LinkedCompanies);
  var reference=(await h.Db.Certificates.SingleAsync()).SecureStoreReference;using var next=Make(true);var result=await h.Service.ImportAsync(h.Write(next),Password.AsMemory(),"Renovado",CertificateUse.Accountant,Guid.NewGuid(),row.Id,row.Revision);row=result.Certificate!;
  Assert.Equal(2,row.LinkedCompanies);Assert.False(h.Store.Values.ContainsKey(reference));Assert.Single(h.Store.Values);var proof=await h.Service.TestAsync(row.Id,Guid.NewGuid());Assert.True(proof.PrivateKeyUsable);Assert.Contains("não verificadas",proof.Message);
  await h.Service.RemoveAsync(row.Id,row.Revision,Guid.NewGuid());Assert.Empty(await h.Service.ListAsync());Assert.Empty(h.Store.Values);Assert.Equal(0,await h.Db.CertificateAssociations.CountAsync());Assert.Equal(2,await h.Db.Companies.CountAsync());
 }
 [Fact]public async Task StaleRevision_DoesNotReplace(){await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;var e=await Assert.ThrowsAsync<CertificateException>(()=>h.Service.ImportAsync(h.File,Password.AsMemory(),"New",CertificateUse.Participant,Guid.NewGuid(),row.Id,row.Revision+1));Assert.Equal(CertificateError.Conflict,e.Code);Assert.Single(h.Store.Values);}
 [Fact]public async Task WrongPassword_PreservesExisting(){await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;await Assert.ThrowsAsync<CertificateException>(()=>h.Service.ImportAsync(h.File,"wrong".AsMemory(),"New",CertificateUse.Participant,Guid.NewGuid(),row.Id,row.Revision));h.Db.ChangeTracker.Clear();Assert.Equal(row.Thumbprint,(await h.Service.ListAsync()).Single().Thumbprint);Assert.Single(h.Store.Values);}
 [Fact]public async Task StoreFailure_DoesNotCommit(){await using var h=await Harness.Create();h.Store.FailWrite=true;await Assert.ThrowsAsync<CertificateException>(()=>h.Import());Assert.Empty(await h.Service.ListAsync());Assert.Empty(h.Store.Values);Assert.False(await h.Db.AuditLogs.AnyAsync(x=>x.Action==AuditAction.CertificateAdded));}
 [Fact]public async Task DatabaseFailure_CompensatesNewBlob()
 {await using var h=await Harness.Create();h.Db.ApplicationSettings.Add(new(){OrganizationId=h.Org,Key="dup",Value=""});await h.Db.SaveChangesAsync();h.Db.ApplicationSettings.Add(new(){OrganizationId=h.Org,Key="dup",Value=""});await Assert.ThrowsAsync<CertificateException>(()=>h.Import());Assert.Empty(h.Store.Values);h.Db.ChangeTracker.Clear();Assert.Empty(await h.Service.ListAsync());}
 [Fact]public async Task CleanupFailure_IsRetryable()
 {await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;h.Store.FailRemove=true;var result=await h.Service.RemoveAsync(row.Id,row.Revision,Guid.NewGuid());Assert.True(result.CleanupPending);Assert.Empty(await h.Service.ListAsync());Assert.Single(h.Store.Values);h.Store.FailRemove=false;Assert.Equal(0,await h.Service.CleanupAsync());Assert.Empty(h.Store.Values);}
 [Fact]public async Task ActiveReference_IsNeverCleaned()
 {await using var h=await Harness.Create();await h.Import();var reference=(await h.Db.Certificates.SingleAsync()).SecureStoreReference;h.Db.ApplicationSettings.Add(new(){OrganizationId=h.Org,Key="CertificateCleanup/"+reference,Value=reference});await h.Db.SaveChangesAsync();Assert.Equal(1,await h.Service.CleanupAsync());Assert.Single(h.Store.Values);}
 [Fact]public async Task Associations_CannotOverwriteOrCrossOffice()
 {
  await using var h=await Harness.Create();var a=(await h.Import()).Certificate!;await h.Service.AssociateAsync(a.Id,a.Revision,h.CompanyIds,"DEFAULT",Guid.NewGuid());using var next=Make();h.File=h.Write(next);var b=(await h.Import()).Certificate!;
  Assert.Equal(CertificateError.Conflict,(await Assert.ThrowsAsync<CertificateException>(()=>h.Service.AssociateAsync(b.Id,b.Revision,h.CompanyIds,"DEFAULT",Guid.NewGuid()))).Code);h.Db.ChangeTracker.Clear();Assert.Equal(CertificateError.NotFound,(await Assert.ThrowsAsync<CertificateException>(()=>h.Service.AssociateAsync(b.Id,b.Revision,new[]{Guid.NewGuid()},"OTHER",Guid.NewGuid()))).Code);
  await using var other=DatabaseFactory.Create(h.DbPath,Guid.NewGuid());var service=new CertificateService(other,h.Store);Assert.Empty(await service.ListAsync());await Assert.ThrowsAsync<CertificateException>(()=>service.OpenAsync(a.Id));
 }
 [Fact]public async Task Unlink_PreservesOtherLinksAndCertificate(){await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;await h.Service.AssociateAsync(row.Id,row.Revision,h.CompanyIds,"DEFAULT",Guid.NewGuid());var link=(await h.Service.LinksAsync(row.Id)).First();await h.Service.UnlinkAsync(link.Id,link.Revision,Guid.NewGuid());Assert.Single(await h.Service.LinksAsync(row.Id));Assert.Single(h.Store.Values);}
 [Fact]public async Task CancelledImport_WritesNothing(){await using var h=await Harness.Create();using var cancel=new CancellationTokenSource();cancel.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>h.Service.ImportAsync(h.File,Password.AsMemory(),"Test",CertificateUse.Participant,Guid.NewGuid(),ct:cancel.Token));Assert.Empty(h.Store.Values);Assert.Empty(await h.Service.ListAsync());}
 [Fact]public async Task MetadataAndAudit_HaveNoSecrets()
 {await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;await h.Service.TestAsync(row.Id,Guid.NewGuid());var c=await h.Db.Certificates.SingleAsync();Assert.True(Guid.TryParseExact(c.SecureStoreReference,"N",out _));var text=System.Text.Json.JsonSerializer.Serialize(c)+System.Text.Json.JsonSerializer.Serialize(await h.Db.AuditLogs.ToListAsync());Assert.DoesNotContain(Password,text);Assert.DoesNotContain(Convert.ToBase64String(System.IO.File.ReadAllBytes(h.File)),text);}
 [Fact]public async Task AnotherCertificate_InSameReference_IsRejected()
 {await using var h=await Harness.Create();var row=(await h.Import()).Certificate!;var reference=(await h.Db.Certificates.SingleAsync()).SecureStoreReference;using var other=Make();h.Store.Values[reference]=PfxCodec.Pack(other.Export(X509ContentType.Pkcs12,Password),Password);Assert.Equal(CertificateError.StoreUnavailable,(await Assert.ThrowsAsync<CertificateException>(()=>h.Service.OpenAsync(row.Id))).Code);}
 [Fact]public async Task ExpiredCertificate_DoesNotReportInDate(){await using var h=await Harness.Create();using var c=Make(offset:-400,days:365);h.File=h.Write(c);var row=(await h.Import()).Certificate!;var result=await h.Service.TestAsync(row.Id,Guid.NewGuid());Assert.Equal(CertificateValidity.Expired,result.Validity);Assert.Contains("Vencido",result.Message);}
 [Fact]public async Task Linux_HasNoInsecureFallback(){if(OperatingSystem.IsWindows())return;await Assert.ThrowsAsync<CertificateException>(()=>new WindowsDpapiSecretStore(System.IO.Path.GetTempPath(),Guid.NewGuid()).StoreAsync(Guid.NewGuid().ToString("N"),new byte[]{1},default));}
 [Fact]public async Task Phase2Upgrade_PreservesCertificateMetadata()
 {
  var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-upgrade-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var org=Guid.NewGuid();var path=System.IO.Path.Combine(dir,"db.sqlite");var id=Guid.NewGuid();
  try
  {
   await using(var db=DatabaseFactory.Create(path,org))
   {
    await db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>().MigrateAsync("20261007203626_CompanySearchIndex");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Organizations (Id,Name,CreatedAtUtc) VALUES ({org},{"Test"},{DateTime.UtcNow})");
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Certificates (Id,OrganizationId,CreatedAtUtc,UpdatedAtUtc,Revision,Label,Subject,Thumbprint,ValidFromUtc,ValidToUtc,SecureStoreReference) VALUES ({id},{org},{DateTime.UtcNow},{DateTime.UtcNow},{1},{"Existing"},{"CN=Legacy"},{"hash"},{DateTime.UtcNow},{DateTime.UtcNow.AddDays(365)},{"legacy-reference"})");
   }
   await using(var db=DatabaseFactory.Create(path,org)){await db.Database.MigrateAsync();var c=await db.Certificates.SingleAsync();Assert.Equal(id,c.Id);Assert.Equal("Existing",c.Label);Assert.Equal(CertificateUse.Participant,c.Use);Assert.False(db.Database.HasPendingModelChanges());}
  }
  finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
 }
 [WindowsFact]public async Task Windows_DpapiRoundTrip_EncryptsAndPreventsOverwrite()
 {
  var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-vault-"+Guid.NewGuid().ToString("N"));var reference=Guid.NewGuid().ToString("N");var store=new WindowsDpapiSecretStore(dir,Guid.NewGuid());var secret=System.Text.Encoding.UTF8.GetBytes(Password);
  try{await store.StoreAsync(reference,secret,default);var encrypted=await System.IO.File.ReadAllBytesAsync(System.IO.Path.Combine(dir,reference+".vault"));Assert.False(encrypted.AsSpan().IndexOf(secret)>=0);Assert.Equal(secret,await store.ReadAsync(reference,default));await Assert.ThrowsAsync<CertificateException>(()=>store.StoreAsync(reference,new byte[]{2},default));Assert.Equal(secret,await store.ReadAsync(reference,default));await Assert.ThrowsAsync<CertificateException>(()=>new WindowsDpapiSecretStore(dir,Guid.NewGuid()).ReadAsync(reference,default));await store.RemoveAsync(reference,default);Assert.Empty(Directory.GetFiles(dir));}finally{CryptographicOperations.ZeroMemory(secret);if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [WindowsFact]public async Task Windows_TamperingAndTraversal_AreRejected()
 {
  var dir=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-vault-"+Guid.NewGuid().ToString("N"));var reference=Guid.NewGuid().ToString("N");var store=new WindowsDpapiSecretStore(dir,Guid.NewGuid());
  try{await store.StoreAsync(reference,new byte[]{1,2,3},default);await Assert.ThrowsAsync<CertificateException>(()=>store.ReadAsync("../escape",default));var path=System.IO.Path.Combine(dir,reference+".vault");var bytes=await System.IO.File.ReadAllBytesAsync(path);bytes[bytes.Length/2]^=255;await System.IO.File.WriteAllBytesAsync(path,bytes);await Assert.ThrowsAsync<CertificateException>(()=>store.ReadAsync(reference,default));}finally{if(Directory.Exists(dir))Directory.Delete(dir,true);}
 }
 [Theory][InlineData("")][InlineData("senha-sintética-ç")]
 public void PfxRoundTrip_AlsoAcceptsEmptyPassword(string password)
 {using var certificate=Make();var bytes=certificate.Export(X509ContentType.Pkcs12,password);try{using var loaded=PfxCodec.Load(bytes,password);Assert.True(loaded.HasPrivateKey);PfxCodec.ProvePrivateKey(loaded);}finally{CryptographicOperations.ZeroMemory(bytes);}}
 [Fact]public async Task UnicodePath_WrongPasswordThenCorrection_DoesNotLoseFile()
 {
  await using var h=await Harness.Create();string path=System.IO.Path.Combine(h.Dir,"certificado-ação-ç.p12");System.IO.File.Copy(h.File,path);h.File=path;
  var error=await Assert.ThrowsAsync<CertificateException>(()=>h.Service.ImportAsync(path,"incorreta".AsMemory(),"Unicode",CertificateUse.Participant,Guid.NewGuid()));Assert.Equal(CertificateError.InvalidPfx,error.Code);Assert.True(System.IO.File.Exists(path));Assert.Empty(await h.Service.ListAsync());
  var saved=(await h.Import()).Certificate!;Assert.True((await h.Service.TestAsync(saved.Id,Guid.NewGuid())).PrivateKeyUsable);
 }
 [Fact]public async Task SelectedValidPfx_WithMissingName_ReproducesOldImportPrecondition()
 {
  await using var h=await Harness.Create();
  var failure=await Assert.ThrowsAsync<CertificateException>(()=>h.Service.ImportAsync(h.File,Password.AsMemory(),"",CertificateUse.Participant,Guid.NewGuid()));
  Assert.Equal(CertificateError.InvalidInput,failure.Code);Assert.Empty(await h.Service.ListAsync());Assert.Empty(h.Store.Values);
  Assert.NotNull((await h.Import()).Certificate);
 }
 private sealed class MemoryStore:ISecretStore
 {
  public Dictionary<string,byte[]> Values {get;}=new();public bool FailWrite,FailRemove;
  public Task StoreAsync(string reference,ReadOnlyMemory<byte> secret,CancellationToken ct){ct.ThrowIfCancellationRequested();if(FailWrite)throw new CertificateException(CertificateError.StoreUnavailable);Values.Add(reference,secret.ToArray());return Task.CompletedTask;}
  public Task<byte[]> ReadAsync(string reference,CancellationToken ct){ct.ThrowIfCancellationRequested();return Task.FromResult(Values[reference].ToArray());}
  public Task RemoveAsync(string reference,CancellationToken ct){ct.ThrowIfCancellationRequested();if(FailRemove)throw new CertificateException(CertificateError.StoreUnavailable);if(Values.Remove(reference,out var bytes))CryptographicOperations.ZeroMemory(bytes);return Task.CompletedTask;}
 }
 private sealed class Harness:IAsyncDisposable
 {
  public Guid Org {get;}=Guid.NewGuid();public string Dir {get;}=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"wgs-cert-test-"+Guid.NewGuid().ToString("N"));public string DbPath=>System.IO.Path.Combine(Dir,"test.db");public string File {get;set;}="";public WgsDbContext Db {get;private set;}=null!;public MemoryStore Store {get;}=new();public CertificateService Service {get;private set;}=null!;public Guid[] CompanyIds {get;private set;}=[];
  public static async Task<Harness> Create()
  {
   var h=new Harness();Directory.CreateDirectory(h.Dir);h.Db=DatabaseFactory.Create(h.DbPath,h.Org);await h.Db.Database.MigrateAsync();h.Db.Organizations.Add(new(){Id=h.Org,Name="Test"});var a=new Company{OrganizationId=h.Org,LegalName="A",Cnpj="12345678000195"};var b=new Company{OrganizationId=h.Org,LegalName="B",Cnpj="12345678000276"};h.Db.Companies.AddRange(a,b);await h.Db.SaveChangesAsync();h.CompanyIds=[a.Id,b.Id];h.Service=new(h.Db,h.Store);using var c=Make(taxId:"12345678000195");h.File=h.Write(c);return h;
  }
  public string Write(X509Certificate2 c){var path=System.IO.Path.Combine(Dir,Guid.NewGuid().ToString("N")+".pfx");System.IO.File.WriteAllBytes(path,c.Export(X509ContentType.Pkcs12,Password));return path;}
  public Task<CertificateMutationResult> Import()=>Service.ImportAsync(File,Password.AsMemory(),"Synthetic",CertificateUse.Participant,Guid.NewGuid());
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();foreach(var bytes in Store.Values.Values)CryptographicOperations.ZeroMemory(bytes);Directory.Delete(Dir,true);}
 }
}
public sealed class WindowsFactAttribute:FactAttribute{public WindowsFactAttribute(){if(!OperatingSystem.IsWindows())Skip="DPAPI requer homologação Windows.";}}
