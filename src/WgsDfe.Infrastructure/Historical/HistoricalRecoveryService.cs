using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Historical;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Parsing;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Archive;
using WgsDfe.Infrastructure.Companies;
namespace WgsDfe.Infrastructure.Historical;
public sealed class HistoricalGate:IDisposable
{internal SemaphoreSlim Semaphore{get;}=new(1,1);public void Dispose()=>Semaphore.Dispose();}
public sealed partial class HistoricalRecoveryService(WgsDbContext db,ArchiveService archive,ArchiveGate archiveGate,HistoricalGate gate,
 IDfeParser parser,INfeSyncService official,IEnumerable<HistoricalProviderRegistration> registrations,ProviderOptions network,TimeProvider clock):IHistoricalRecoveryService
{
 private DateTime Now=>clock.GetUtcNow().UtcDateTime;
 private Dictionary<string,HistoricalProviderRegistration> Registry()
 {
  var list=registrations.ToArray();
  if(list.Any(x=>!x.Provider.Capabilities.IsHistorical||!x.Provider.Capabilities.IsImplemented||x.Provider.Capabilities.Code.Length is <1 or >64||x.Provider.Capabilities.Code.Any(c=>!char.IsAsciiLetterOrDigit(c)&&c!='_')||x.PollSeconds is <1 or >86400)||list.Select(x=>x.Provider.Capabilities.Code).Distinct().Count()!=list.Length)throw new HistoricalException("INVALID_OPTIONS");
  return list.ToDictionary(x=>x.Provider.Capabilities.Code,StringComparer.Ordinal);
 }
 public IReadOnlyList<HistoricalProviderChoice> Providers()=>Registry().Select(x=>new HistoricalProviderChoice(x.Key,x.Value.IsPaid)).ToArray();
 public async Task<IReadOnlyList<HistoricalCompany>> CompaniesAsync(CancellationToken ct=default)=>await db.Companies.AsNoTracking().OrderBy(x=>x.LegalName).Select(x=>new HistoricalCompany(x.Id,x.LegalName,x.Cnpj)).ToListAsync(ct);
 public async Task<Guid> ImportAsync(HistoricalImport request,CancellationToken ct=default)
 {
  if(!Enum.IsDefined(request.Environment))throw new HistoricalException("INVALID_OPTIONS");
  if(request.CompanyId.HasValue&&!await db.Companies.AnyAsync(x=>x.Id==request.CompanyId,ct))throw new HistoricalException("INVALID_OPTIONS");
  IReadOnlyList<SourceRow> source;
  try
  {
   var f=new FileInfo(request.File);if(!f.Exists||f.Length is <1 or >20*1024*1024)throw new HistoricalException("INVALID_FILE");
   source=f.Extension.ToLowerInvariant() switch{".xlsx"=>CompanyFileReader.Xlsx(request.File,ct,100000).Rows,".csv" or ".txt"=>CompanyFileReader.Csv(request.File,ct,100000),_=>throw new HistoricalException("INVALID_FILE")};
  }
  catch(OperationCanceledException){throw;}catch(HistoricalException){throw;}catch{throw new HistoricalException("INVALID_FILE");}
  if(source.Count==0)throw new HistoricalException("INVALID_FILE");
  string Normalize(string x)=>new(SearchNormalizer.Normalize(x).Where(char.IsLetterOrDigit).ToArray());
  var columns=source[0].Cells.Where(x=>Normalize(x.Value.Value) is "CHAVE" or "CHAVEDEACESSO" or "CHAVENFE" or "CHAVECTE").ToArray();
  if(columns.Length>1)throw new HistoricalException("INVALID_FILE");
  bool header=columns.Length==1;int column=header?columns[0].Key:1;
  if(header&&columns[0].Value.Invalid||!header&&source[0].Cells.Count!=1)throw new HistoricalException("INVALID_FILE");
  var input=source.Skip(header?1:0).ToArray();if(input.Length is <1 or >100000)throw new HistoricalException("INVALID_FILE");
  var companies=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Cnpj,x=>x.Id,ct);
  await gate.Semaphore.WaitAsync(ct);
  try
  {
   await using var tx=await db.Database.BeginTransactionAsync(ct);
   var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.HistoricalRecovery,Status=JobStatus.Pending,TotalItems=input.Length};
   var batch=new HistoricalRecoveryBatch{OrganizationId=db.OrganizationId,JobId=job.Id,Environment=request.Environment,FileName=Path.GetFileName(request.File)};
   db.ProcessingJobs.Add(job);db.HistoricalRecoveryBatches.Add(batch);await db.SaveChangesAsync(ct);
   var seen=new HashSet<string>(StringComparer.Ordinal);int count=0;
   foreach(var row in input)
   {
    ct.ThrowIfCancellationRequested();var cell=row.Cells.GetValueOrDefault(column)??new SourceCell("");
    var item=new HistoricalRecoveryItem{OrganizationId=db.OrganizationId,BatchId=batch.Id,SourceLine=row.Number,Input=cell.Value.Length>200?cell.Value[..200]:cell.Value,CompanyId=request.CompanyId};
    try
    {
     if(cell.Invalid||cell.IsNumeric||cell.Value.Length>200)throw new HistoricalException("INVALID_CELL");
     var key=DfeAccessKey.Parse(cell.Value);item.AccessKey=key.Value;item.Model=key.Model;item.IssuerTaxId=key.IssuerTaxId;item.Year=key.Year;item.Month=key.Month;
     item.CompanyId??=companies.TryGetValue(key.IssuerTaxId,out var company)?company:null;
     if(!seen.Add(key.Value)){item.State=HistoricalItemState.Duplicate;item.ResultCode="DUPLICATE";}
    }
    catch(Exception e)when(e is AccessKeyException or HistoricalException){item.State=HistoricalItemState.Invalid;item.Availability=Availability.InvalidKey;item.ResultCode=e is HistoricalException?"INVALID_CELL":"INVALID_KEY";}
    db.HistoricalRecoveryItems.Add(item);
    if(++count%500==0){await db.SaveChangesAsync(ct);db.ChangeTracker.Clear();}
   }
   await db.SaveChangesAsync(ct);await tx.CommitAsync(ct);return batch.Id;
  }
  finally{db.ChangeTracker.Clear();gate.Semaphore.Release();}
 }
 public async Task<IReadOnlyList<HistoricalBatchRow>> BatchesAsync(CancellationToken ct=default)
 {
  return await db.HistoricalRecoveryBatches.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).Take(50).Select(b=>new HistoricalBatchRow(b.Id,b.FileName,b.Environment,
   db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id),db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id&&i.State!=HistoricalItemState.Invalid&&i.State!=HistoricalItemState.Duplicate),
   db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id&&i.State==HistoricalItemState.Invalid),db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id&&i.State==HistoricalItemState.Duplicate),
   db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id&&i.State==HistoricalItemState.Available),db.HistoricalRecoveryItems.Count(i=>i.BatchId==b.Id&&(i.State==HistoricalItemState.Waiting||i.State==HistoricalItemState.Submitting)),b.PaidCallsReserved,db.ProcessingJobs.Where(j=>j.Id==b.JobId).Select(j=>j.Status).Single())).ToListAsync(ct);
 }
 public async Task<HistoricalPage> RowsAsync(Guid batch,int page=1,string search="",CancellationToken ct=default)
 {
  if(page<1||search.Length>200)throw new HistoricalException("INVALID_OPTIONS");
  var query=db.HistoricalRecoveryItems.AsNoTracking().Where(x=>x.BatchId==batch);
  if(search!="")query=query.Where(x=>x.AccessKey!=null&&x.AccessKey.Contains(search)||x.Input.Contains(search)||x.ResultCode.Contains(search));
  int total=await query.CountAsync(ct);
  var rows=await query.OrderBy(x=>x.SourceLine).Skip(checked((page-1)*50)).Take(50).Select(x=>new HistoricalRow(x.Id,x.SourceLine,x.Input,x.AccessKey,x.Model,x.IssuerTaxId,x.Year,x.Month,
   db.Companies.Where(c=>c.Id==x.CompanyId).Select(c=>c.LegalName).FirstOrDefault()??"Não associada",x.ResultCode,x.Availability,x.ProviderCode,x.Attempts,x.NextAllowedAtUtc)).ToListAsync(ct);
  return new(rows,total,page);
 }
 public async Task<IReadOnlyList<HistoricalGroup>> GroupsAsync(Guid batch,CancellationToken ct=default)
 {
  var groups=await db.HistoricalRecoveryItems.AsNoTracking().Where(x=>x.BatchId==batch&&x.State!=HistoricalItemState.Invalid&&x.State!=HistoricalItemState.Duplicate).GroupBy(x=>new{x.CompanyId,x.IssuerTaxId,x.Model,x.Year,x.Month}).Select(g=>new{g.Key,Count=g.Count()}).ToListAsync(ct);
  var names=await db.Companies.AsNoTracking().ToDictionaryAsync(x=>x.Id,x=>x.LegalName,ct);
  return groups.Select(g=>new HistoricalGroup(g.Key.CompanyId.HasValue?names.GetValueOrDefault(g.Key.CompanyId.Value,"Não associada"):"Não associada",g.Key.IssuerTaxId??"",g.Key.Model,g.Key.Year,g.Key.Month,g.Count)).ToArray();
 }
 public async Task AssignUnassignedAsync(Guid batch,Guid company,CancellationToken ct=default)
 {
  if(!await db.Companies.AnyAsync(x=>x.Id==company,ct)||!await db.HistoricalRecoveryBatches.AnyAsync(x=>x.Id==batch,ct))throw new HistoricalException("INVALID_OPTIONS");
  await gate.Semaphore.WaitAsync(ct);try{await db.HistoricalRecoveryItems.Where(x=>x.BatchId==batch&&x.CompanyId==null&&x.State==HistoricalItemState.Pending&&x.ExternalJobId==null).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.CompanyId,company),ct);}finally{gate.Semaphore.Release();}
 }
 public async Task WriteReportAsync(Guid batch,string destination,CancellationToken ct=default)
 {
  var reportBatch=await db.HistoricalRecoveryBatches.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==batch,ct)??throw new HistoricalException("NOT_FOUND_BATCH");
  string temporary=destination+"."+Guid.NewGuid().ToString("N")+".part";
  static string Field(string? value){var s=value??"";if(s.TrimStart().StartsWith('=')||s.TrimStart().StartsWith('+')||s.TrimStart().StartsWith('-')||s.TrimStart().StartsWith('@'))s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
  try
  {
   await using(var writer=new StreamWriter(temporary,false,new UTF8Encoding(true)))
   {
    await writer.WriteLineAsync("Arquivo;Linha;Ambiente;Entrada;Chave;Modelo;Emitente;Ano;Mes;Empresa;Resultado;Codigo;Disponibilidade;Provedor;Tentativas;ProximaTentativaUTC");
    int page=1;while(true){var result=await RowsAsync(batch,page++,ct:ct);foreach(var row in result.Rows){ct.ThrowIfCancellationRequested();await writer.WriteLineAsync(string.Join(';',new[]{reportBatch.FileName,row.Line.ToString(CultureInfo.InvariantCulture),reportBatch.Environment==FiscalEnvironment.Production?"Produção":"Homologação",row.Input,row.Key,row.Model?.ToString(),row.Issuer,row.Year?.ToString(CultureInfo.InvariantCulture),row.Month?.ToString(CultureInfo.InvariantCulture),row.Company,row.Result,row.Code,row.Availability.ToString(),row.Provider,row.Attempts.ToString(CultureInfo.InvariantCulture),row.NextAllowedAtUtc?.ToString("O",CultureInfo.InvariantCulture)}.Select(Field)));}if(result.Rows.Count<50)break;}
   }
   ct.ThrowIfCancellationRequested();File.Move(temporary,destination,true);
  }
  finally{if(File.Exists(temporary))File.Delete(temporary);}
 }
}
