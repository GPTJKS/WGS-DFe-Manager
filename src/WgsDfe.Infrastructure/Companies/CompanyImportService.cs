using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Companies;
using WgsDfe.Database;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Companies;
public sealed class CompanyImportService(WgsDbContext db,ICompanyService companies) : ICompanyImportService
{
 public async Task<ImportPlan> AnalyzeAsync(string path,CancellationToken cancellationToken=default)
 {
  var file=await Task.Run(()=>CompanyFileReader.Read(path,cancellationToken),cancellationToken);
  var existing=(await db.Companies.AsNoTracking().Select(x=>x.Cnpj).ToListAsync(cancellationToken)).Select(s=>s.ToUpperInvariant()).ToHashSet(StringComparer.Ordinal);
  var seen=new HashSet<string>(StringComparer.Ordinal);var rows=new List<ImportRow>();
  foreach(var row in file.Rows)
  {
   cancellationToken.ThrowIfCancellationRequested();
   if(row.Error is not null){rows.Add(new(row.Number,null,row.Cnpj,ImportRowStatus.Invalid,row.Error));continue;}
   try
   {
    var data=CompanyValidation.Normalize(row.Data!);
    var status=!seen.Add(data.Cnpj)?ImportRowStatus.DuplicateInFile:existing.Contains(data.Cnpj)?ImportRowStatus.DuplicateInDatabase:ImportRowStatus.Ready;
    rows.Add(new(row.Number,data,row.Cnpj,status,status switch{ImportRowStatus.DuplicateInFile=>"CNPJ repetido no arquivo.",ImportRowStatus.DuplicateInDatabase=>"CNPJ já cadastrado; os dados serão preservados.",_=>"Pronta para importar."}));
   }
   catch(CompanyException e){rows.Add(new(row.Number,row.Data,row.Cnpj,ImportRowStatus.Invalid,e.Message));}
  }
  return new ImportPlan(Path.GetFileName(path),file.SheetName,rows.AsReadOnly());
 }
 public async Task<ImportResult> ImportAsync(ImportPlan plan,IProgress<ImportProgress>? progress=null,CancellationToken cancellationToken=default)
 {
  if(plan.Rows.Count>CompanyFileReader.MaxRows || plan.Rows.Select(r=>r.RowNumber).Distinct().Count()!=plan.Rows.Count)
   throw new CompanyException(CompanyError.InvalidData,"Plano de importação inválido. Analise o arquivo novamente.");
  var correlation=Guid.NewGuid();var job=new ProcessingJob{OrganizationId=db.OrganizationId,Kind=JobKind.Import,Status=JobStatus.Running,StartedAtUtc=DateTime.UtcNow,TotalItems=plan.Rows.Count};
  db.ProcessingJobs.Add(job);Audit(job.Id,AuditAction.CompanyImportStarted,correlation,"IMPORT_STARTED");await db.SaveChangesAsync(cancellationToken);
  var results=new List<ImportRow>();var cancelled=false;
  try
  {
   foreach(var row in plan.Rows)
   {
    cancellationToken.ThrowIfCancellationRequested();
    var outcome=row;Guid? companyId=null;
    // Cadastro, auditoria e checkpoint do item são uma única transação.
    await using var tx=await db.Database.BeginTransactionAsync(cancellationToken);
    if(row.Status==ImportRowStatus.Ready)
    {
     try
     {
      if(row.Data is null)throw new CompanyException(CompanyError.InvalidData,"Dados da linha ausentes.");
      var created=await companies.CreateAsync(row.Data,correlation,cancellationToken);companyId=created.Id;
      outcome=row with{Data=created.Data,Status=ImportRowStatus.Imported,Reason="Empresa importada."};
     }
     catch(CompanyException e)
     {
      outcome=row with{Status=e.Code==CompanyError.DuplicateCnpj?ImportRowStatus.DuplicateInDatabase:ImportRowStatus.Invalid,Reason=e.Message};
     }
    }
    else if(row.Status is not (ImportRowStatus.Invalid or ImportRowStatus.DuplicateInDatabase or ImportRowStatus.DuplicateInFile))
     outcome=row with{Status=ImportRowStatus.Invalid,Reason="Analise o arquivo novamente antes de importar."};
    // Recarregar job após eventual Clear no tratamento de concorrência do serviço.
    job=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,cancellationToken);
    job.ProcessedItems++;
    db.ProcessingJobItems.Add(new(){OrganizationId=db.OrganizationId,JobId=job.Id,CompanyId=companyId,ItemReference=row.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),
     Status=outcome.Status==ImportRowStatus.Imported?JobStatus.Completed:JobStatus.CompletedWithWarnings,
     Availability=outcome.Status==ImportRowStatus.Imported?Availability.Available:Availability.Pending,
     ErrorCode=outcome.Status.ToString()});
    await db.SaveChangesAsync(cancellationToken);await tx.CommitAsync(cancellationToken);
    results.Add(outcome);progress?.Report(new(results.Count,plan.Rows.Count,results.Count(x=>x.Status==ImportRowStatus.Imported)));
   }
  }
  catch(OperationCanceledException) when(cancellationToken.IsCancellationRequested)
  {
   cancelled=true;db.ChangeTracker.Clear();
   results.AddRange(plan.Rows.Skip(results.Count).Select(r=>r with{Status=ImportRowStatus.Cancelled,Reason="Não processada: importação cancelada."}));
  }
  catch
  {
   db.ChangeTracker.Clear();job=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,CancellationToken.None);
   job.Status=JobStatus.Failed;job.LastErrorCode="IMPORT_FAILED";job.CompletedAtUtc=DateTime.UtcNow;await db.SaveChangesAsync(CancellationToken.None);throw;
  }
  job=await db.ProcessingJobs.SingleAsync(x=>x.Id==job.Id,CancellationToken.None);
  job.CompletedAtUtc=DateTime.UtcNow;job.Status=cancelled?JobStatus.Cancelled:results.Any(r=>r.Status!=ImportRowStatus.Imported)?JobStatus.CompletedWithWarnings:JobStatus.Completed;
  Audit(job.Id,cancelled?AuditAction.CompanyImportCancelled:AuditAction.CompanyImportCompleted,correlation,cancelled?"IMPORT_CANCELLED":"IMPORT_COMPLETED");
  await db.SaveChangesAsync(CancellationToken.None);return new ImportResult(job.Id,results.AsReadOnly(),cancelled);
 }
 private void Audit(Guid id,AuditAction action,Guid correlation,string code)=>db.AuditLogs.Add(new(){OrganizationId=db.OrganizationId,Action=action,EntityId=id,CorrelationId=correlation,ResultCode=code});
}
