using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Parsing;
namespace WgsDfe.Infrastructure.Archive;
public sealed partial class ArchiveService
{

 private Dictionary<Guid,int> RankedSearch(string search,CancellationToken ct=default)
 {

  var ranks=new Dictionary<Guid,int>();
  // Read only metadata; never open XMLs or change the persisted search/index schema.
  foreach(var row in db.FiscalDocuments.AsNoTracking().Select(d=>new{d.Id,d.IssuerName,d.RecipientName,d.AccessKey,d.IssuerTaxId,d.RecipientTaxId,d.DocumentNumber}).AsEnumerable())
  {ct.ThrowIfCancellationRequested();var score=SearchMatcher.RankFiscal(search,new[]{row.IssuerName,row.RecipientName},new[]{row.AccessKey,row.IssuerTaxId,row.RecipientTaxId},new[]{row.DocumentNumber});if(score.HasValue)ranks.Add(row.Id,score.Value);}
  return ranks;
 }
 public async Task<ArchiveAvailability> CheckAvailabilityAsync(string key,FiscalEnvironment environment,CancellationToken ct=default)
 {
  if(!Enum.IsDefined(environment))throw new ArchiveException(ArchiveError.InvalidQuery);
  DfeAccessKey parsedKey;try{parsedKey=DfeAccessKey.Parse(key);}catch(AccessKeyException){return new(null,Availability.InvalidKey,"INVALID_KEY");}
  var document=await db.FiscalDocuments.AsNoTracking().SingleOrDefaultAsync(d=>d.AccessKey==parsedKey.Value&&d.Environment==environment,ct);
  if(document is null)return new(null,Availability.Pending,"NOT_INDEXED");
  try
  {
   var bytes=await files.ReadAsync(document.FileRelativePath,document.XmlHash,ct);var parsed=parser.Parse(bytes,ct);
   if(parsed.Kind!=WgsDfe.Application.Parsing.DfePayloadKind.FullDocument||parsed.Key.Value!=parsedKey.Value||parsed.Environment!=environment)return new(document.Id,Availability.TemporaryError,"INDEX_XML_MISMATCH");
   return new(document.Id,Availability.Available,"LOCAL_XML_PRESENT_UNVERIFIED",document.XmlHash);
  }
  catch(ArchiveException e){return new(document.Id,e.Code==ArchiveError.FileMissing?Availability.Pending:Availability.TemporaryError,"LOCAL_"+e.Code);}
  catch(WgsDfe.Application.Parsing.DfeParseException){return new(document.Id,Availability.TemporaryError,"LOCAL_XML_INVALID");}
 }
 internal IQueryable<FiscalDocument> FilteredDocuments(ArchiveQuery q,CancellationToken ct=default,Dictionary<Guid,int>? matches=null)
 {
  Validate(q);var query=db.FiscalDocuments.AsNoTracking();string search=SearchNormalizer.Normalize(q.Search);
  if(Cnpj.TryNormalize(search,out var tax))search=tax;
  if(search.Length>0){var ids=(matches??RankedSearch(q.Search,ct)).Keys.ToArray();query=query.Where(d=>ids.Contains(d.Id));}
  if(q.CompanyId==Guid.Empty)query=query.Where(d=>!db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==d.Id));
  else if(q.CompanyId.HasValue)
  {
   var id=q.CompanyId.Value;query=query.Where(d=>db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==d.Id&&l.CompanyId==id&&(!q.Role.HasValue||q.Role==ActorFilter.Either||l.Role==q.Role||((q.Role==ActorFilter.Issuer||q.Role==ActorFilter.Recipient)&&l.Role==ActorFilter.Both))));
  }
  if(q.CompanyIds is not null)
  {
   if(q.CompanyIds.Count is <1 or >10000||q.CompanyIds.Any(x=>x==Guid.Empty))throw new ArchiveException(ArchiveError.InvalidQuery);
   var ids=q.CompanyIds.ToArray();query=query.Where(d=>db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==d.Id&&ids.Contains(l.CompanyId)));
  }
  if(q.Models is not null)
  {
   if(q.Models.Count is <1 or >5||q.Models.Any(x=>!Enum.IsDefined(x)))throw new ArchiveException(ArchiveError.InvalidQuery);
   var models=q.Models.ToArray();query=query.Where(d=>models.Contains(d.Model));
  }
  if(q.Model.HasValue)query=query.Where(d=>d.Model==q.Model);
  if(q.Environment.HasValue)query=query.Where(d=>d.Environment==q.Environment);
  if(q.Status.HasValue)query=query.Where(d=>d.Status==q.Status);
  string issuer=TaxSearch(q.IssuerTaxId),recipient=TaxSearch(q.RecipientTaxId);
  if(issuer.Length>0)query=query.Where(d=>d.IssuerTaxId!=null&&d.IssuerTaxId.Contains(issuer));
  if(recipient.Length>0)query=query.Where(d=>d.RecipientTaxId!=null&&d.RecipientTaxId.Contains(recipient));
  if(q.Number.Length>0){string cleanNum=q.Number.TrimStart('0');if(cleanNum.Length==0)cleanNum="0";string number=cleanNum.PadLeft(9,'0');query=query.Where(d=>d.DocumentNumber==q.Number||d.DocumentNumber==number||d.DocumentNumber==cleanNum);}
  if(q.Series.Length>0){string series=q.Series.PadLeft(3,'0');query=query.Where(d=>d.Series==q.Series||d.Series==series);}
  if(q.From.HasValue){string from=q.From.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);query=query.Where(d=>d.IssueLocalDate!=null&&string.Compare(d.IssueLocalDate,from)>=0);}
  if(q.To.HasValue){string to=q.To.Value.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);query=query.Where(d=>d.IssueLocalDate!=null&&string.Compare(d.IssueLocalDate,to)<=0);}
  if(q.Minimum.HasValue){long minimum=Cents(q.Minimum.Value);query=query.Where(d=>d.TotalCents>=minimum);}
  if(q.Maximum.HasValue){long maximum=Cents(q.Maximum.Value);query=query.Where(d=>d.TotalCents<=maximum);}
  return query;
 }
 public async Task<ArchivePage> SearchAsync(ArchiveQuery q,CancellationToken ct=default)
 {
  Validate(q);var ranks=string.IsNullOrWhiteSpace(q.Search)?null:RankedSearch(q.Search,ct);var query=FilteredDocuments(q,ct,ranks);
  int total=await query.CountAsync(ct);
  Guid[]? orderedIds=null;
  if(!string.IsNullOrWhiteSpace(q.Search))
  {var candidates=await query.Select(d=>new{d.Id,d.IssueLocalDate,d.AccessKey}).ToListAsync(ct);orderedIds=candidates.OrderBy(x=>ranks![x.Id]).ThenByDescending(x=>x.IssueLocalDate).ThenBy(x=>x.AccessKey).Skip((q.Page-1)*q.PageSize).Take(q.PageSize).Select(x=>x.Id).ToArray();query=query.Where(d=>orderedIds.Contains(d.Id));}
  else query=query.OrderByDescending(d=>d.IssueLocalDate).ThenBy(d=>d.AccessKey).ThenBy(d=>d.Environment).Skip((q.Page-1)*q.PageSize).Take(q.PageSize);
  var page=await query.Select(d=>new ArchiveDocumentRow(d.Id,d.AccessKey,d.Model,d.Environment,d.IssuerTaxId,d.IssuerName,d.RecipientTaxId,d.RecipientName,d.IssueLocalDate,d.IssueDateOriginal,d.DocumentNumber,d.Series,d.TotalValue,d.Status,
   db.ArchiveFiles.Count(f=>f.FiscalDocumentId==d.Id),db.ArchiveFiles.Count(f=>f.FiscalDocumentId==d.Id&&f.IsConflict),db.FiscalDocumentCompanies.Count(l=>l.FiscalDocumentId==d.Id))).ToListAsync(ct);
  if(orderedIds is not null)page=page.OrderBy(x=>Array.IndexOf(orderedIds,x.Id)).ToList();return new(page,total,q.Page,q.PageSize);
 }
 public async Task<ArchiveFinancialSummary> FinancialSummaryAsync(ArchiveFinancialQuery q,CancellationToken ct=default)
 {
  var queryObj=new ArchiveQuery(Search:q.Search,CompanyId:q.CompanyId,Model:q.Model,Environment:q.Environment,Status:null,
   IssuerTaxId:q.IssuerTaxId,RecipientTaxId:q.RecipientTaxId,From:q.From,To:q.To,Role:q.Role,CompanyIds:q.CompanyIds,Models:q.Models);
  var ranks=string.IsNullOrWhiteSpace(q.Search)?null:RankedSearch(q.Search,ct);
  var docs=FilteredDocuments(queryObj,ct,ranks);
  var aggregates=await docs.GroupBy(d=>d.Status).Select(g=>new{Status=g.Key,Count=g.Count(),TotalCents=g.Sum(x=>x.TotalCents??0L)}).ToListAsync(ct);
  int authorizedCount=0,cancelledCount=0,deniedCount=0,totalDocs=0;
  long authorizedCents=0L,cancelledCents=0L,deniedCents=0L;
  foreach(var item in aggregates)
  {
   totalDocs+=item.Count;
   if(item.Status==DocumentStatus.Authorized){authorizedCount+=item.Count;authorizedCents+=item.TotalCents;}
   else if(item.Status==DocumentStatus.Cancelled){cancelledCount+=item.Count;cancelledCents+=item.TotalCents;}
   else if(item.Status==DocumentStatus.Denied){deniedCount+=item.Count;deniedCents+=item.TotalCents;}
  }
  decimal gross=authorizedCents/100m,cancelled=cancelledCents/100m,net=gross-cancelled,denied=deniedCents/100m;
  return new ArchiveFinancialSummary(authorizedCount,gross,cancelledCount,cancelled,net,deniedCount,denied,totalDocs);
 }
 public async Task<IReadOnlyList<ArchiveCompany>> CompaniesAsync(CancellationToken ct=default)
 {var result=await db.Companies.AsNoTracking().OrderBy(x=>x.LegalName).Select(x=>new ArchiveCompany(x.Id,x.LegalName+" · "+x.Cnpj)).ToListAsync(ct);result.Insert(0,new(Guid.Empty,"Sem vínculo com empresa"));result.Insert(0,new(null,"Todas as empresas"));return result;}
 public async Task<ArchiveEventPage> SearchEventsAsync(string search,FiscalEnvironment? environment=null,DocumentModel? model=null,int page=1,CancellationToken ct=default)
 {
  if(search.Length>200||page is <1 or >1000000||(environment.HasValue&&!Enum.IsDefined(environment.Value))||(model.HasValue&&!Enum.IsDefined(model.Value)))throw new ArchiveException(ArchiveError.InvalidQuery);
  string text=search.Trim().ToUpperInvariant();var query=db.FiscalDocumentEvents.AsNoTracking();
  if(text.Length>0)query=query.Where(e=>e.AccessKey.Contains(text)||e.EventTypeCode.Contains(text)||
   db.Companies.Any(c=>(db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==e.FiscalDocumentId&&l.CompanyId==c.Id)||(e.AccessKey.Length>=20&&c.Cnpj==e.AccessKey.Substring(6,14)))&&(c.LegalName.ToUpper().Contains(text)||c.Cnpj.Contains(text)))||
   db.FiscalDocuments.Any(d=>d.Id==e.FiscalDocumentId&&((d.IssuerName!=null&&d.IssuerName.ToUpper().Contains(text))||(d.RecipientName!=null&&d.RecipientName.ToUpper().Contains(text)))));
  if(environment.HasValue)query=query.Where(e=>e.Environment==environment);
  if(model.HasValue)query=query.Where(e=>e.Model==model);
  int total=await query.CountAsync(ct);var data=await EventRows(query.OrderByDescending(e=>e.EventDateUtc).ThenBy(e=>e.Id).Skip((page-1)*50).Take(50)).ToListAsync(ct);return new(data,total);
 }
 private IQueryable<ArchiveEventRow> EventRows(IQueryable<FiscalDocumentEvent> query)=>query.Select(e=>new ArchiveEventRow(e.Id,e.FiscalDocumentId,e.AccessKey,e.Model,e.Environment,e.EventTypeCode,e.SequenceNumber,e.ResponseCode,e.ProtocolNumber,e.EventDateOriginal,db.ArchiveFiles.Count(f=>f.FiscalDocumentEventId==e.Id),
  db.Companies.Where(c=>db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==e.FiscalDocumentId&&l.CompanyId==c.Id)||(e.AccessKey.Length>=20&&c.Cnpj==e.AccessKey.Substring(6,14))).Select(c=>c.LegalName).FirstOrDefault()
   ??db.FiscalDocuments.Where(d=>d.Id==e.FiscalDocumentId).Select(d=>d.IssuerName??d.RecipientName).FirstOrDefault()
   ??"Sem vínculo"));
 public async Task<ArchiveDetail> DetailAsync(Guid id,CancellationToken ct=default)
 {
  var d=await db.FiscalDocuments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArchiveException(ArchiveError.NotFound);
  var filesList=await FileRows(db.ArchiveFiles.Where(f=>f.FiscalDocumentId==id)).ToListAsync(ct);
  var events=await EventRows(db.FiscalDocumentEvents.Where(e=>e.FiscalDocumentId==id).OrderBy(e=>e.EventDateUtc)).ToListAsync(ct);
  int links=await db.FiscalDocumentCompanies.CountAsync(l=>l.FiscalDocumentId==id,ct);
  var row=new ArchiveDocumentRow(d.Id,d.AccessKey,d.Model,d.Environment,d.IssuerTaxId,d.IssuerName,d.RecipientTaxId,d.RecipientName,d.IssueLocalDate,d.IssueDateOriginal,d.DocumentNumber,d.Series,d.TotalValue,d.Status,filesList.Count,filesList.Count(f=>f.Conflict),links);
  return new(row,events,filesList);
 }
 public async Task<IReadOnlyList<ArchiveFileRow>> EventFilesAsync(Guid id,CancellationToken ct=default)
 {if(!await db.FiscalDocumentEvents.AnyAsync(x=>x.Id==id,ct))throw new ArchiveException(ArchiveError.NotFound);return await FileRows(db.ArchiveFiles.Where(f=>f.FiscalDocumentEventId==id)).ToListAsync(ct);}
 private IQueryable<ArchiveFileRow> FileRows(IQueryable<ArchiveFile> query)=>query.OrderBy(f=>f.CreatedAtUtc).Select(f=>new ArchiveFileRow(f.Id,f.Hash,f.RelativePath,f.ByteLength,f.IsConflict,f.ResponseCode,f.CreatedAtUtc,db.FiscalDocuments.Any(d=>d.Id==f.FiscalDocumentId&&d.XmlHash==f.Hash)||db.FiscalDocumentEvents.Any(e=>e.Id==f.FiscalDocumentEventId&&e.XmlHash==f.Hash)));
 public async Task<ArchiveXml> ReadXmlAsync(Guid fileId,CancellationToken ct=default)
 {
  var item=await db.ArchiveFiles.AsNoTracking().SingleOrDefaultAsync(f=>f.Id==fileId,ct)??throw new ArchiveException(ArchiveError.NotFound);
  var bytes=await files.ReadAsync(item.RelativePath,item.Hash,ct);parser.Parse(bytes,ct);
  using var stream=new MemoryStream(bytes,false);using var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=DfeParser.MaximumBytes});
  string formatted=XDocument.Load(reader).ToString();bool truncated=formatted.Length>200000;return new(truncated?formatted[..200000]:formatted,truncated,item.Hash);
 }
 public async Task<ArchiveOriginalXml> ReadOriginalXmlAsync(Guid fileId,CancellationToken ct=default)
 {
  var item=await db.ArchiveFiles.AsNoTracking().SingleOrDefaultAsync(f=>f.Id==fileId,ct)??throw new ArchiveException(ArchiveError.NotFound);
  var bytes=await files.ReadAsync(item.RelativePath,item.Hash,ct);var parsed=parser.Parse(bytes,ct);
  string name=parsed.EventType is not null&&parsed.EventSequence.HasValue?$"{parsed.Key.Value}-{parsed.EventType}-{parsed.EventSequence.Value:D3}.xml":$"{parsed.Key.Value}.xml";
  return new(bytes,name,item.Hash);
 }
 public async Task<int> RefreshLinksAsync(CancellationToken ct=default)
 {
  await gate.Semaphore.WaitAsync(ct);int added=0;
  try
  {
   for(int skip=0;;skip+=500)
   {
    var batch=await db.FiscalDocuments.OrderBy(d=>d.Id).Skip(skip).Take(500).ToListAsync(ct);if(batch.Count==0)break;
    await using var transaction=await db.Database.BeginTransactionAsync(ct);
    foreach(var document in batch)added+=await LinkCompanies(document,ct);
    Audit(AuditAction.ArchiveLinksUpdated,Guid.Empty,Guid.NewGuid(),"ARCHIVE_LINKS_UPDATED");await db.SaveChangesAsync(ct);await transaction.CommitAsync(ct);db.ChangeTracker.Clear();
   }
   return added;
  }
  finally{gate.Semaphore.Release();}
 }
 public async Task<IntegrityResult> CheckIntegrityAsync(IProgress<int>? progress=null,CancellationToken ct=default)
 {
  await gate.Semaphore.WaitAsync(ct);int count=0,valid=0,orphans=0;bool cancelled=false;var problems=new List<IntegrityRow>();
  try
  {
   var indexed=await db.ArchiveFiles.AsNoTracking().Select(f=>new{Path=f.RelativePath,f.Hash}).ToListAsync(ct);
   var legacy=await db.FiscalDocuments.AsNoTracking().Where(d=>!db.ArchiveFiles.Any(f=>f.FiscalDocumentId==d.Id)).Select(d=>new{Path=d.FileRelativePath,Hash=d.XmlHash}).ToListAsync(ct);indexed.AddRange(legacy);
   var oldEvents=await db.FiscalDocumentEvents.AsNoTracking().Where(e=>!db.ArchiveFiles.Any(f=>f.FiscalDocumentEventId==e.Id)).Select(e=>new{Path=e.FileRelativePath,Hash=e.XmlHash}).ToListAsync(ct);indexed.AddRange(oldEvents);
   foreach(var item in indexed.Distinct())
   {
    ct.ThrowIfCancellationRequested();try{await files.ReadAsync(item.Path,item.Hash,ct);valid++;}catch(ArchiveException e){problems.Add(new(item.Path,e.Code.ToString()));}count++;progress?.Report(count);
   }
   var known=indexed.Select(f=>f.Path).ToHashSet(StringComparer.Ordinal);
   foreach(var path in files.EnumeratePhysicalFiles()){ct.ThrowIfCancellationRequested();if(!known.Contains(path)){orphans++;if(problems.Count<10000)problems.Add(new(path,"ORPHAN_PRESERVED"));}}
  }
  catch(OperationCanceledException)when(ct.IsCancellationRequested){cancelled=true;}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or ArchiveException){problems.Add(new("Acervo","SCAN_UNAVAILABLE"));}
  finally{gate.Semaphore.Release();}
  return new(count,valid,problems.AsReadOnly(),orphans,cancelled);
 }
 private static long Cents(decimal value)
 {try{if(value<0||decimal.Truncate(value*100m)!=value*100m)throw new ArchiveException(ArchiveError.InvalidQuery);return checked((long)(value*100m));}catch(OverflowException){throw new ArchiveException(ArchiveError.InvalidQuery);}}
 private static void Validate(ArchiveQuery q)
 {
  if(q.Search.Length>200||q.IssuerTaxId.Length>30||q.RecipientTaxId.Length>30||q.Page is <1 or >1000000||q.PageSize is <1 or >200||q.From>q.To||q.Minimum>q.Maximum||q.Number.Length>9||q.Series.Length>3||q.Number.Any(c=>!char.IsAsciiDigit(c))||q.Series.Any(c=>!char.IsAsciiDigit(c))||
   (q.Model.HasValue&&!Enum.IsDefined(q.Model.Value))||(q.Environment.HasValue&&!Enum.IsDefined(q.Environment.Value))||(q.Status.HasValue&&!Enum.IsDefined(q.Status.Value))||(q.Role.HasValue&&!Enum.IsDefined(q.Role.Value)))throw new ArchiveException(ArchiveError.InvalidQuery);
  if(q.Minimum.HasValue)Cents(q.Minimum.Value);if(q.Maximum.HasValue)Cents(q.Maximum.Value);
 }
 private static string TaxSearch(string value)
 {string normalized=value.Trim().ToUpperInvariant().Replace(".","").Replace("/","").Replace("-","");if(normalized.Any(c=>!char.IsAsciiLetterOrDigit(c)))throw new ArchiveException(ArchiveError.InvalidQuery);return normalized;}
}
