using System.Globalization;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Archive;
namespace WgsDfe.Infrastructure.Export;
internal sealed record PlannedXml(Guid OwnerId,Guid? CompanyId,string SourcePath,string Destination,string Key,DocumentModel Model,
 FiscalEnvironment Environment,string Hash,long Length,bool Event,string? EventType,int? Sequence,ExportDetails Details);
public sealed class ExportGate:IDisposable
{internal SemaphoreSlim Semaphore{get;}=new(1,1);public void Dispose()=>Semaphore.Dispose();}
public sealed partial class ExportService(WgsDbContext db,ArchiveService archive,IArchiveFileStore files,ArchiveGate archiveGate,ExportGate exportGate,ExportLimits limits,
 WgsDfe.Application.Parsing.IDfeParser parser,ArchiveLocation location,TimeProvider clock):IExportService
{
 private static readonly StringComparer PathComparer=StringComparer.OrdinalIgnoreCase;
 private DateTime Now=>clock.GetUtcNow().UtcDateTime;
 public async Task<ExportPreview> PreviewAsync(ExportRequest request,CancellationToken ct=default)
 {var plan=await Plan(request,ct);return new(plan.Where(x=>!x.Event).Select(x=>x.OwnerId).Distinct().Count(),plan.Where(x=>x.Event).Select(x=>x.OwnerId).Distinct().Count(),plan.Count,plan.Sum(x=>x.Length),plan.Count-plan.Select(x=>(x.OwnerId,x.Hash)).Distinct().Count());}
 private static DocumentModel[] Models(EnabledDocuments flags)=>Enum.GetValues<DocumentModel>().Where(m=>(flags&(m switch{DocumentModel.NFe=>EnabledDocuments.NFe,DocumentModel.NFCe=>EnabledDocuments.NFCe,DocumentModel.CTe=>EnabledDocuments.CTe,DocumentModel.CTeOS=>EnabledDocuments.CTeOS,_=>EnabledDocuments.NFCom}))!=0).ToArray();
 private async Task<List<PlannedXml>> Plan(ExportRequest r,CancellationToken ct)
 {
  if(!Enum.IsDefined(r.Format)||r.Models==EnabledDocuments.None||(r.Models&~EnabledDocuments.All)!=0||r.IncludeConflicts&&!r.IncludeVersions||
   r.Companies is {Count:0}||r.Companies?.Count>10000||r.Companies?.Distinct().Count()!=r.Companies?.Count||r.Companies?.Contains(Guid.Empty)==true||r.Companies is not null&&r.Filter.CompanyId.HasValue||
   r.DocumentIds is {Count:0}||r.DocumentIds?.Count>10000||r.EventIds is {Count:0}||r.EventIds?.Count>10000||limits.MaxFiles is <1 or >100000||limits.MaxBytes is <1 or >10L*1024*1024*1024)throw new ExportException("EXPORT_INVALID_REQUEST");
  if(r.DocumentIds is not null&&r.EventIds is not null)throw new ExportException("EXPORT_INVALID_REQUEST");
  if(r.DocumentIds?.Distinct().Count()!=r.DocumentIds?.Count||r.EventIds?.Distinct().Count()!=r.EventIds?.Count)throw new ExportException("EXPORT_INVALID_REQUEST");
  await archiveGate.Semaphore.WaitAsync(ct);
  try
  {
   var companies=await db.Companies.IgnoreQueryFilters().Where(x=>x.OrganizationId==db.OrganizationId).AsNoTracking().ToDictionaryAsync(x=>x.Id,ct);
   if(r.Companies?.Any(x=>!companies.ContainsKey(x))==true||r.Filter.CompanyId is { } selected&&selected!=Guid.Empty&&!companies.ContainsKey(selected))throw new ExportException("EXPORT_INVALID_REQUEST");
   var query=archive.FilteredDocuments(r.Filter);var models=Models(r.Models).Where(m=>r.Filter.Models is null||r.Filter.Models.Contains(m)).ToArray();query=query.Where(d=>models.Contains(d.Model));
   var scope=r.Companies??r.Filter.CompanyIds??(r.Filter.CompanyId is { } c&&c!=Guid.Empty?new[]{c}:null);
   bool unassigned=r.Filter.CompanyId==Guid.Empty||scope is null&&r.IncludeUnassigned;
   if(scope is not null)query=query.Where(d=>db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==d.Id&&scope.Contains(l.CompanyId)&&(!r.Filter.Role.HasValue||r.Filter.Role==ActorFilter.Either||l.Role==r.Filter.Role||((r.Filter.Role==ActorFilter.Issuer||r.Filter.Role==ActorFilter.Recipient)&&l.Role==ActorFilter.Both))));
   else if(!unassigned)query=query.Where(d=>db.FiscalDocumentCompanies.Any(l=>l.FiscalDocumentId==d.Id));
   if(r.DocumentIds is not null)query=query.Where(d=>r.DocumentIds.Contains(d.Id));
   if(r.EventIds is not null)query=query.Where(d=>false); // Explicit event selection never silently adds notes.
   var docs=await query.OrderBy(d=>d.AccessKey).ThenBy(d=>d.Environment).Take(limits.MaxFiles+1).ToListAsync(ct);
   if(docs.Count>limits.MaxFiles)throw new ExportException("EXPORT_LIMIT");
   var plan=new List<PlannedXml>();var paths=new Dictionary<string,string>(PathComparer);var folderOwners=new Dictionary<string,Guid?>(PathComparer);long bytes=0;
   foreach(var batch in docs.Chunk(200))
   {
    ct.ThrowIfCancellationRequested();var ids=batch.Select(x=>x.Id).ToArray();
    var links=await db.FiscalDocumentCompanies.AsNoTracking().Where(l=>ids.Contains(l.FiscalDocumentId)).ToListAsync(ct);
    var variants=await db.ArchiveFiles.AsNoTracking().Where(f=>f.FiscalDocumentId.HasValue&&ids.Contains(f.FiscalDocumentId.Value)).ToListAsync(ct);
    var events=r.IncludeEvents?await db.FiscalDocumentEvents.AsNoTracking().Where(e=>e.FiscalDocumentId.HasValue&&ids.Contains(e.FiscalDocumentId.Value)).ToListAsync(ct):[];
    var eventIds=events.Select(x=>x.Id).ToArray();var eventVariants=await db.ArchiveFiles.AsNoTracking().Where(f=>f.FiscalDocumentEventId.HasValue&&eventIds.Contains(f.FiscalDocumentEventId.Value)).ToListAsync(ct);
    foreach(var d in batch)
    {
     var key=CheckedKey(d.AccessKey,d.Model);var memberships=links.Where(x=>x.FiscalDocumentId==d.Id&&(scope is null||scope.Contains(x.CompanyId))&&(!r.Filter.Role.HasValue||r.Filter.Role==ActorFilter.Either||x.Role==r.Filter.Role||((r.Filter.Role==ActorFilter.Issuer||r.Filter.Role==ActorFilter.Recipient)&&x.Role==ActorFilter.Both))).ToArray();
     var targets=Targets(key.IssuerTaxId,memberships.Select(x=>x.CompanyId),unassigned&&!links.Any(x=>x.FiscalDocumentId==d.Id));
     var canonical=variants.SingleOrDefault(x=>x.FiscalDocumentId==d.Id&&x.Hash==d.XmlHash);
     foreach(var target in targets)
     {
      string prefix=ReadableExportPath.Prefix(target.Folder,StatusFolder(d.Status),key,d.Environment,r.Filter.From,r.Filter.To,d.IssueLocalDate);
      Add(d.Id,target.Id,d.FileRelativePath,prefix+key.Value+".xml",key,d.Environment,d.XmlHash,canonical?.ByteLength??0,false,null,null,d);
      if(r.IncludeVersions)foreach(var f in variants.Where(x=>x.FiscalDocumentId==d.Id&&x.Hash!=d.XmlHash&&(!x.IsConflict||r.IncludeConflicts)))
       Add(d.Id,target.Id,f.RelativePath,ReadableExportPath.Prefix(target.Folder,"Não confirmados",key,d.Environment,r.Filter.From,r.Filter.To,d.IssueLocalDate)+key.Value+(f.IsConflict?"-divergente-":"-versao-")+VariantNumber(variants,f)+".xml",key,d.Environment,f.Hash,f.ByteLength,false,null,null);
      foreach(var e in events.Where(x=>x.FiscalDocumentId==d.Id))
      {if(e.AccessKey!=d.AccessKey||e.Environment!=d.Environment||e.Model!=d.Model)throw new ExportException("EXPORT_SOURCE_INVALID");AddEvent(e,target.Id,prefix,eventVariants);}
     }
    }
   }
   if(r.IncludeOrphanEvents||r.EventIds is not null)
   {
    var events=db.FiscalDocumentEvents.AsNoTracking().Where(e=>models.Contains(e.Model));
    if(r.EventIds is not null)events=events.Where(e=>r.EventIds.Contains(e.Id));else events=events.Where(e=>e.FiscalDocumentId==null);
    if(r.Filter.Environment.HasValue)events=events.Where(e=>e.Environment==r.Filter.Environment);
    if(r.Filter.Model.HasValue)events=events.Where(e=>e.Model==r.Filter.Model);
    // Unlinked events cannot prove note status, amount, recipient or a non-issuer role.
    if(r.EventIds is null&&(r.Filter.Status.HasValue||r.Filter.Minimum.HasValue||r.Filter.Maximum.HasValue||r.Filter.RecipientTaxId.Length>0||r.Filter.Role is ActorFilter.Recipient or ActorFilter.Both or ActorFilter.OtherParticipant))events=events.Where(e=>false);
    var candidates=await events.OrderBy(x=>x.Id).Take(limits.MaxFiles+1).ToListAsync(ct);if(candidates.Count>limits.MaxFiles)throw new ExportException("EXPORT_LIMIT");
    foreach(var e in candidates)
    {
     ct.ThrowIfCancellationRequested();var key=CheckedKey(e.AccessKey,e.Model);
     if(r.EventIds is null)
     {
      if(r.Filter.Search.Length>0&&!e.AccessKey.Contains(r.Filter.Search.Trim().ToUpperInvariant(),StringComparison.Ordinal)&&!e.EventTypeCode.Contains(r.Filter.Search.Trim(),StringComparison.Ordinal))continue;
      if(r.Filter.IssuerTaxId.Length>0&&!key.IssuerTaxId.Contains(r.Filter.IssuerTaxId.Trim(),StringComparison.OrdinalIgnoreCase))continue;
      if(r.Filter.Number.Length>0&&key.Number!=r.Filter.Number.PadLeft(9,'0')||r.Filter.Series.Length>0&&key.Series!=r.Filter.Series.PadLeft(3,'0'))continue;
     }
     DateOnly? date=e.EventDateOriginal?.Length>=10&&DateOnly.TryParseExact(e.EventDateOriginal[..10],"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)?day:null;
     if((r.Filter.From.HasValue||r.Filter.To.HasValue)&&(!date.HasValue||date<r.Filter.From||date>r.Filter.To))continue;
     var eventLinks=e.FiscalDocumentId.HasValue?await db.FiscalDocumentCompanies.AsNoTracking().Where(l=>l.FiscalDocumentId==e.FiscalDocumentId).ToListAsync(ct):[];
     var ownerCompanies=e.FiscalDocumentId.HasValue?eventLinks.Where(l=>(scope is null||scope.Contains(l.CompanyId))&&(!r.Filter.Role.HasValue||r.Filter.Role==ActorFilter.Either||l.Role==r.Filter.Role||((r.Filter.Role==ActorFilter.Issuer||r.Filter.Role==ActorFilter.Recipient)&&l.Role==ActorFilter.Both))).Select(l=>l.CompanyId).ToArray():companies.Values.Where(c=>c.Cnpj==key.IssuerTaxId&&(scope is null||scope.Contains(c.Id))).Select(x=>x.Id).ToArray();
     bool hasOwner=e.FiscalDocumentId.HasValue?eventLinks.Count>0:companies.Values.Any(c=>c.Cnpj==key.IssuerTaxId);
     if(scope is not null&&ownerCompanies.Length==0)continue;
     if(r.Filter.CompanyId==Guid.Empty&&hasOwner)continue;
     var variants=await db.ArchiveFiles.AsNoTracking().Where(f=>f.FiscalDocumentEventId==e.Id).ToListAsync(ct);
     foreach(var target in Targets(key.IssuerTaxId,ownerCompanies,unassigned&&!hasOwner))AddEvent(e,target.Id,ReadableExportPath.Prefix(target.Folder,"Eventos sem nota",key,e.Environment,r.Filter.From,r.Filter.To,e.EventDateOriginal?.Length>=10?e.EventDateOriginal[..10]:null),variants);
    }
   }
   if(plan.Count==0)throw new ExportException("EXPORT_NO_FILES");
   if(r.Format==ExportFormat.Folder&&Path.IsPathFullyQualified(r.Destination))
   {
    var used=new Dictionary<string,Guid?>(PathComparer);
    foreach(var group in plan.GroupBy(x=>x.CompanyId).ToArray())
    {
     int budget=group.Min(x=>259-Path.GetFullPath(r.Destination).TrimEnd(Path.DirectorySeparatorChar).Length-1-(x.Destination.Length-x.Destination.Split('/')[0].Length));
     string name=group.Key.HasValue?CompanyFolder(group.Key.Value,Math.Min(100,budget)):"Sem vínculo";
     if(used.TryGetValue(name,out var owner)&&owner!=group.Key)throw new ExportException("EXPORT_PATH_TOO_LONG");used[name]=group.Key;
     foreach(var item in group){int index=plan.IndexOf(item);plan[index]=item with{Destination=name+item.Destination[item.Destination.IndexOf('/')..],Details=item.Details with{Folder=name}};}
    }
   }
   return plan.OrderBy(x=>x.Destination,StringComparer.Ordinal).ToList();
   IEnumerable<(Guid? Id,string Folder)> Targets(string issuer,IEnumerable<Guid> ids,bool allowUnassigned)
   {
    var list=ids.Distinct().ToArray();
    if(list.Length==0){if(allowUnassigned)yield return(null,"Sem vínculo");yield break;}
    foreach(var id in list){if(!companies.TryGetValue(id,out var company)||!Cnpj.TryNormalize(company.Cnpj,out var normalized)||normalized!=company.Cnpj)throw new ExportException("EXPORT_SOURCE_INVALID");yield return(id,CompanyFolder(company.Id));}
   }
   string CompanyFolder(Guid id,int maximum=100)
   {
    var company=companies[id];string name=ReadableExportPath.Segment(company.LegalName);
    var duplicates=companies.Values.Where(x=>ReadableExportPath.Segment(x.LegalName).Equals(name,StringComparison.OrdinalIgnoreCase)).ToArray();
    if(maximum<12)throw new ExportException("EXPORT_PATH_TOO_LONG");
    if(duplicates.Length>1){int digits=4;while(digits<14&&duplicates.Select(x=>x.Cnpj[^digits..]).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=duplicates.Length)digits++;string suffix=" ("+company.Cnpj[^digits..]+")";if(maximum-suffix.Length<8)throw new ExportException("EXPORT_PATH_TOO_LONG");name=ReadableExportPath.Segment(name,maximum-suffix.Length)+suffix;}else name=ReadableExportPath.Segment(name,maximum);
    return name;
   }
   void AddEvent(FiscalDocumentEvent e,Guid? company,string prefix,IReadOnlyList<ArchiveFile> variants)
   {
    var key=CheckedKey(e.AccessKey,e.Model);if(e.EventTypeCode.Length!=6||e.EventTypeCode.Any(x=>!char.IsAsciiDigit(x))||e.SequenceNumber is <1 or >999)throw new ExportException("EXPORT_SOURCE_INVALID");
    string name=$"{key.Value}-{e.EventTypeCode}-{e.SequenceNumber:D3}";var canonical=variants.SingleOrDefault(x=>x.FiscalDocumentEventId==e.Id&&x.Hash==e.XmlHash);
    Add(e.Id,company,e.FileRelativePath,prefix+name+".xml",key,e.Environment,e.XmlHash,canonical?.ByteLength??0,true,e.EventTypeCode,e.SequenceNumber);
    if(r.IncludeVersions)foreach(var f in variants.Where(x=>x.FiscalDocumentEventId==e.Id&&x.Hash!=e.XmlHash&&(!x.IsConflict||r.IncludeConflicts)))
     Add(e.Id,company,f.RelativePath,prefix+name+(f.IsConflict?"-divergente-":"-versao-")+VariantNumber(variants,f)+".xml",key,e.Environment,f.Hash,f.ByteLength,true,e.EventTypeCode,e.SequenceNumber);
   }
   void Add(Guid owner,Guid? company,string source,string dest,DfeAccessKey key,FiscalEnvironment env,string hash,long length,bool ev,string? type,int? sequence,FiscalDocument? document=null)
   {
    CheckedHash(hash);if(!Enum.IsDefined(env)||length<0)throw new ExportException("EXPORT_SOURCE_INVALID");
    if(r.Format==ExportFormat.Folder&&Path.IsPathFullyQualified(r.Destination))
    {
     string first=dest.Split('/')[0];int budget=259-Path.GetFullPath(r.Destination).TrimEnd(Path.DirectorySeparatorChar).Length-1-(dest.Length-first.Length);
     if(first.Length>budget){if(budget<12)throw new ExportException("EXPORT_PATH_TOO_LONG");first=ReadableExportPath.Segment(first,budget);}
     if(folderOwners.TryGetValue(first,out var other)&&other!=company)
     {
      if(!company.HasValue||budget<20)throw new ExportException("EXPORT_PATH_TOO_LONG");
      string tax=companies[company.Value].Cnpj;int digits=4;string candidate;
      do{string suffix=" ("+tax[^digits..]+")";candidate=ReadableExportPath.Segment(first,budget-suffix.Length)+suffix;digits++;}while(folderOwners.TryGetValue(candidate,out other)&&other!=company&&digits<=tax.Length);
      first=candidate;
     }
     folderOwners[first]=company;dest=first+dest[dest.IndexOf('/')..];
    }
    if(paths.TryGetValue(dest,out var previous)){if(previous==hash)return;throw new ExportException("EXPORT_DESTINATION_CONFLICT");}paths.Add(dest,hash);
    bytes=checked(bytes+length);if(plan.Count>=limits.MaxFiles||bytes>limits.MaxBytes)throw new ExportException("EXPORT_LIMIT");
    var firm=company.HasValue?companies[company.Value]:null;
    bool canonical=document is not null&&document.XmlHash==hash;
    var details=new ExportDetails(firm?.LegalName??("Sem vínculo"),firm?.Cnpj??key.IssuerTaxId,
     document?.DocumentNumber??key.Number,document?.Series??key.Series,document?.IssueLocalDate??"",
     canonical?StatusLabel(document!.Status):"Não confirmada",canonical?document?.TotalValue:null,
     ev?"Evento preservado; não confirma isoladamente a situação da nota.":canonical?"Situação extraída do XML/eventos locais; sem consulta de situação ou validação de assinatura.":"Versão alternativa; conferir divergências antes de usar.",
     dest.StartsWith("SemVinculo/",StringComparison.Ordinal)?string.Join('/',dest.Split('/').Take(2)):dest.Split('/')[0]);
    plan.Add(new(owner,company,source,dest,key.Value,key.Model,env,hash,length,ev,type,sequence,details));
   }
  }
  catch(ArchiveException){throw new ExportException("EXPORT_INVALID_REQUEST");}
  catch(AccessKeyException){throw new ExportException("EXPORT_SOURCE_INVALID");}
  finally{archiveGate.Semaphore.Release();}
 }
 private static string StatusFolder(DocumentStatus status)=>status switch{DocumentStatus.Authorized=>"Autorizados",DocumentStatus.Cancelled=>"Cancelados",DocumentStatus.Denied=>"Denegados",_=>"Não confirmados"};
 private static string StatusLabel(DocumentStatus status)=>status switch{DocumentStatus.Authorized=>"Autorizado (XML)",DocumentStatus.Cancelled=>"Cancelado (XML/evento)",DocumentStatus.Denied=>"Denegado (XML)",_=>"Não confirmada"};
 private static string CheckedHash(string hash)=>hash.Length==64&&hash.All(c=>char.IsAsciiDigit(c)||c is >= 'A' and <= 'F')?hash:throw new ExportException("EXPORT_SOURCE_INVALID");
 private static DfeAccessKey CheckedKey(string key,DocumentModel model){var parsed=DfeAccessKey.Parse(key);if(parsed.Value!=key||parsed.Model!=model)throw new ExportException("EXPORT_SOURCE_INVALID");return parsed;}
 private static string VariantNumber(IReadOnlyList<ArchiveFile> files,ArchiveFile selected)=>(files.OrderBy(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).ToList().IndexOf(selected)+1).ToString("D2",CultureInfo.InvariantCulture);
}
