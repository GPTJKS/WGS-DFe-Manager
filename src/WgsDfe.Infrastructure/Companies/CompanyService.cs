using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Companies;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Companies;
public sealed class CompanyService(WgsDbContext db) : ICompanyService
{
 public async Task<CompanyPage> SearchAsync(CompanyQuery query,CancellationToken cancellationToken=default)
 {
  if(query.Page<1 || query.Page>100000 || query.PageSize<1 || query.PageSize>200 || query.Search.Length>200)
   throw new CompanyException(CompanyError.InvalidData,"Parâmetros de pesquisa inválidos.");
  var data=db.Companies.AsNoTracking();
  if(query.IsActive.HasValue)data=data.Where(x=>x.IsActive==query.IsActive.Value);
  var candidates=await data.ToListAsync(cancellationToken);
  var matches=new List<(Company Company,int Rank)>();
  foreach(var company in candidates){cancellationToken.ThrowIfCancellationRequested();var rank=SearchMatcher.Rank(query.Search,new[]{company.LegalName,company.TradeName,company.City},new[]{company.Cnpj,company.StateRegistration});if(rank.HasValue)matches.Add((company,rank.Value));}
  var total=matches.Count;
  var ordered=matches.OrderBy(x=>x.Rank).ThenBy(x=>x.Company.LegalName).ThenBy(x=>x.Company.Id).Select(x=>x.Company);
  var rows=(query.All?ordered:ordered.Skip((query.Page-1)*query.PageSize).Take(query.PageSize)).ToArray();
  return new CompanyPage(rows.Select(ToRow).ToArray(),total,query.Page,query.PageSize);
 }
 public async Task<CompanyRow?> FindAsync(Guid id,CancellationToken cancellationToken=default)
 {
  var company=await db.Companies.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,cancellationToken);
  return company is null?null:ToRow(company);
 }
 public async Task<CompanyRow> CreateAsync(CompanyInput input,Guid correlationId,CancellationToken cancellationToken=default)
 {
  var normalized=CompanyValidation.Normalize(input);
  if(await db.Companies.AnyAsync(x=>x.Cnpj.ToUpper()==normalized.Cnpj,cancellationToken))throw Duplicate();
  // A re-created CNPJ recovers the original cursor and cooldown, avoiding a rewind.
  var entity=await db.Companies.IgnoreQueryFilters().SingleOrDefaultAsync(x=>x.OrganizationId==db.OrganizationId&&x.Cnpj.ToUpper()==normalized.Cnpj&&x.IsDeleted,cancellationToken);
  if(entity is null){entity=new Company{OrganizationId=db.OrganizationId};db.Companies.Add(entity);}
  entity.IsDeleted=false;Apply(entity,normalized);Audit(entity.Id,AuditAction.CompanyCreated,correlationId);
  await SaveAsync(cancellationToken);return ToRow(entity);
 }
 public async Task<CompanyRow> UpdateAsync(Guid id,long revision,CompanyInput input,Guid correlationId,CancellationToken cancellationToken=default)
 {
  var normalized=CompanyValidation.Normalize(input);var entity=await LoadAsync(id,revision,cancellationToken);
  if(await db.Companies.AnyAsync(x=>x.Id!=id&&x.Cnpj.ToUpper()==normalized.Cnpj,cancellationToken))throw Duplicate();
  if(!string.Equals(normalized.Cnpj,entity.Cnpj,StringComparison.OrdinalIgnoreCase)&&await HasDependenciesAsync(id,cancellationToken))
   throw new CompanyException(CompanyError.HasDependencies,"Esta empresa já possui vínculos ou acervo. O CNPJ não pode ser trocado; cadastre outra empresa.");
  Apply(entity,normalized);Audit(id,AuditAction.CompanyUpdated,correlationId);
  await SaveAsync(cancellationToken);return ToRow(entity);
 }
 public async Task SetActiveAsync(Guid id,long revision,bool active,Guid correlationId,CancellationToken cancellationToken=default)
 {
  var entity=await LoadAsync(id,revision,cancellationToken);
  if(entity.IsActive==active)return;
  entity.IsActive=active;Audit(id,active?AuditAction.CompanyActivated:AuditAction.CompanyInactivated,correlationId);
  await SaveAsync(cancellationToken);
 }
 public async Task DeleteAsync(Guid id,long revision,Guid correlationId,CancellationToken cancellationToken=default)
 {
  var entity=await LoadAsync(id,revision,cancellationToken);
  entity.IsDeleted=true;entity.IsActive=false;
  foreach(var schedule in await db.SyncSchedules.Where(x=>x.CompanyId==id).ToListAsync(cancellationToken))schedule.Enabled=false;
  Audit(id,AuditAction.CompanyDeleted,correlationId);await SaveAsync(cancellationToken);
 }
 private async Task<bool> HasDependenciesAsync(Guid id,CancellationToken ct)=>
  await db.SyncSchedules.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.HistoricalRecoveryItems.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.CertificateAssociations.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.FiscalDocumentCompanies.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.DocumentSyncStates.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.CteDistributionReceipts.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.NfeDistributionReceipts.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.KnownDocumentKeys.AnyAsync(x=>x.CompanyId==id,ct)||
  await db.ProcessingJobItems.AnyAsync(x=>x.CompanyId==id,ct);
 private async Task<Company> LoadAsync(Guid id,long revision,CancellationToken ct)
 {
  var entity=await db.Companies.SingleOrDefaultAsync(x=>x.Id==id,ct)
   ??throw new CompanyException(CompanyError.NotFound,"Empresa não encontrada.");
  if(entity.Revision!=revision)throw Conflict();return entity;
 }
 private async Task SaveAsync(CancellationToken ct)
 {
  try{await db.SaveChangesAsync(ct);}
  catch(DbUpdateConcurrencyException){db.ChangeTracker.Clear();throw Conflict();}
  catch(DbUpdateException e) when(e.InnerException is SqliteException{SqliteExtendedErrorCode:2067})
  {db.ChangeTracker.Clear();throw Duplicate();}
 }
 private static CompanyException Duplicate()=>new(CompanyError.DuplicateCnpj,"Já existe uma empresa cadastrada com este CNPJ.");
 private static CompanyException Conflict()=>new(CompanyError.Conflict,"O cadastro foi alterado por outra operação. Atualize a lista e abra a empresa novamente.");
 private void Audit(Guid id,AuditAction action,Guid correlation)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode="COMPANY_OK"});
 private static void Apply(Company e,CompanyInput i)
 {
  e.LegalName=i.LegalName;e.TradeName=i.TradeName;e.Cnpj=i.Cnpj;e.StateRegistration=i.StateRegistration;e.State=i.State;e.City=i.City;
  e.IsActive=i.IsActive;e.EnabledDocuments=i.EnabledDocuments;e.StorageRelativePath=i.StorageRelativePath;e.Notes=i.Notes;
  e.SearchText=SearchNormalizer.ForCompany(e);
 }
 internal static CompanyRow ToRow(Company e)=>new(e.Id,e.Revision,new(e.LegalName,e.TradeName,e.Cnpj,e.StateRegistration,e.State,e.City,e.IsActive,e.EnabledDocuments,e.StorageRelativePath,e.Notes),e.LastSynchronizationAtUtc,e.ConnectionStatusCode,e.AccountantLinkStatusCode);
}
