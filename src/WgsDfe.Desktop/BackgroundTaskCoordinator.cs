using System.Runtime.CompilerServices;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;

/// <summary>Owns operation lifetime and conflict leases independently of WPF pages.</summary>
public sealed class BackgroundTaskCoordinator
{
 private static readonly ConditionalWeakTable<FoundationRuntime,BackgroundTaskCoordinator> instances=new();
 public static BackgroundTaskCoordinator For(FoundationRuntime runtime)=>instances.GetValue(runtime,_=>new());
 private readonly object gate=new();private readonly List<BackgroundOperation> operations=new();
 public IReadOnlyList<BackgroundOperation> Operations {get{lock(gate)return operations.ToArray();}}
 public IProgress<T> Observe<T>(CancellationTokenSource owner,IProgress<T> display)=>new Observer<T>(value=>
 {lock(gate){var operation=operations.LastOrDefault(x=>x.Owns(owner));if(operation is not null)operation.LatestProgress=value;}display.Report(value);});
 private sealed class Observer<T>(Action<T> report):IProgress<T>{public void Report(T value)=>report(value);}
 public async Task<T> RunAsync<T>(string title,IReadOnlyCollection<string> resources,CancellationTokenSource cancellation,Func<CancellationToken,Task<T>> work)
 {
  BackgroundOperation operation;
  lock(gate)
  {
   if(operations.Any(x=>x.Running&&x.Resources.Intersect(resources).Any()))throw new BackgroundTaskConflictException();
   operation=new(title,resources,cancellation);operations.Add(operation);
  }
  try{var task=Task.Run(()=>work(cancellation.Token));operation.Work=task;var result=await task.ConfigureAwait(false);operation.Result=result;operation.Outcome=result switch
  {
   WgsDfe.Application.Nfe.NfeSyncResult {Cancelled:true}=>"Cancelada",
   WgsDfe.Application.Export.ExportResult {Cancelled:true}=>"Cancelada",
   WgsDfe.Application.Export.ExportResult {Failed:true}=>"Falhou",
   WgsDfe.Application.Nfe.NfeSyncResult nfe when nfe.Rows.Any(x=>x.Code is not ("NFE_SYNCHRONIZED" or "NFE_PAGE_COMMITTED" or "LOCAL_XML_PRESENT" or "WAIT_UNTIL_ALLOWED"))=>"Concluída com alertas",
   _=>cancellation.IsCancellationRequested?"Cancelada":"Concluída"
  };return result;}
  catch(OperationCanceledException){operation.Outcome="Cancelada";throw;}
  catch{operation.Outcome="Falhou";throw;}
  finally{operation.CompletedAt=DateTimeOffset.Now;operation.Running=false;}
 }
}
public sealed class BackgroundTaskConflictException():InvalidOperationException("Já existe uma tarefa incompatível para esta empresa e fonte ou uma exportação em andamento. Acompanhe-a em Tarefas.");

public sealed class BackgroundOperation(string title,IReadOnlyCollection<string> resources,CancellationTokenSource cancellation)
{
 public Guid Id {get;}=Guid.NewGuid();public string Title {get;}=title;
 public IReadOnlyCollection<string> Resources {get;}=resources;
 public DateTimeOffset StartedAt {get;}=DateTimeOffset.Now;public DateTimeOffset? CompletedAt {get;internal set;}
 public volatile bool Running=true;public string Outcome {get;internal set;}="Executando";
 public object? Result {get;internal set;}public Task? Work {get;internal set;}
 public object? LatestProgress {get;internal set;}
 internal bool Owns(CancellationTokenSource source)=>ReferenceEquals(source,cancellation);
 public void Cancel(){if(Running)try{cancellation.Cancel();}catch(ObjectDisposedException){ }}
}
