using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
namespace WgsDfe.Desktop;

public partial class MainWindow
{
 private readonly List<WorkspaceView> backgroundViews=new();
 private void InitializeBackgroundTasks()
 {
  DialogHost.Suspended+=TrackBackground;
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
  var notified=new HashSet<Guid>();
  timer.Tick+=(_,_)=>
  {
   var operations=OperationViews().ToArray();int running=operations.Count(x=>x.IsWorking);
   var finished=BackgroundTaskCoordinator.For(runtime).Operations.Where(x=>!x.Running&&notified.Add(x.Id)).ToArray();
   if(finished.Length>0){BackgroundTasksButton.ToolTip=string.Join("\n",finished.Select(x=>$"{x.Title} · {x.Outcome}"));BackgroundTasksButton.Content="Tarefa finalizada · conferir";Motion.Fade(BackgroundTasksButton,.4,1,180);}
   else if(running>0)BackgroundTasksButton.Content=$"Tarefas · {running} em execução";
  };
  timer.Start();Closed+=(_,_)=>timer.Stop();
 }
 private IEnumerable<WorkspaceView> OperationViews()=>pages.Values.OfType<QueriesView>().SelectMany(x=>x.AllViews).Concat(backgroundViews).Distinct();
 private void TrackBackground(WorkspaceView view)
 {
  if(backgroundViews.Contains(view))return;
  backgroundViews.Add(view);view.Closed+=(_,_)=>backgroundViews.Remove(view);
 }
 private void BackgroundTasks_Click(object sender,RoutedEventArgs e)=>OpenView(new BackgroundTasksView(OperationViews().ToArray(),ResumeOperation,BackgroundTaskCoordinator.For(runtime).Operations));
 private void ResumeOperation(WorkspaceView view)
 {
  if(view is ExportWindow){OpenView(view);return;}
  var queries=pages.Values.OfType<QueriesView>().FirstOrDefault(x=>x.AllViews.Contains(view));
  if(queries is null)return;ShowPage("consultas",()=>queries);queries.Select(view);
 }
}

internal sealed class BackgroundTasksView:WorkspaceView
{
 public BackgroundTasksView(IReadOnlyList<WorkspaceView> views,Action<WorkspaceView> resume,IReadOnlyList<BackgroundOperation> history)
 {
  Title="Tarefas e operações";
  var body=new StackPanel{Margin=new Thickness(24)};
  body.Children.Add(new TextBlock{Text="Consulte o andamento ou cancele com segurança. As operações continuam enquanto você navega.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,16)});
  var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
  var updates=new List<Action>();WorkspaceView? requested=null;
  foreach(var view in views.Where(x=>x.IsWorking||x is ExportWindow||x.HasOperationHistory))
  {
   var panel=new StackPanel();panel.Children.Add(new TextBlock{Text=view.Title,FontWeight=FontWeights.SemiBold});
   var status=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,8)};panel.Children.Add(status);
   var actions=new WrapPanel();var open=new Button{Content="Retomar tarefa"};var cancel=new Button{Content="Cancelar tarefa"};actions.Children.Add(open);actions.Children.Add(cancel);panel.Children.Add(actions);
   open.Click+=(_,_)=>{requested=view;DialogResult=true;};cancel.Click+=(_,_)=>view.CancelOperation();
   updates.Add(()=>{status.Text=view.OperationStatus;cancel.IsEnabled=view.IsWorking;});
   body.Children.Add(new Border{Style=(Style)FindResource("Card"),Child=panel,Margin=new Thickness(0,0,0,12)});
  }
  if(updates.Count==0)body.Children.Add(new TextBlock{Text="Nenhuma operação em andamento."});
  foreach(var operation in history.Where(x=>!x.Running).Reverse().Take(20))body.Children.Add(new TextBlock{Text=$"{operation.Title} · {operation.Outcome} · {operation.StartedAt:dd/MM HH:mm:ss} a {operation.CompletedAt:HH:mm:ss}",Margin=new Thickness(0,8,0,0),TextWrapping=TextWrapping.Wrap});
  void Update(){foreach(var update in updates)update();}
  timer.Tick+=(_,_)=>Update();Update();timer.Start();
  Closed+=(_,_)=>{timer.Stop();if(requested is { } selected)Dispatcher.BeginInvoke(new Action(()=>resume(selected)));};
  Content=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
 }
}

internal sealed class CloseTasksView:WorkspaceView
{
 public CloseTasksView(IReadOnlyList<WorkspaceView> tasks,Action closeApplication)
 {
  Title="Há tarefas em andamento";
  PreferredDialogSize=new Size(760,460);
  var panel=new StackPanel{Margin=new Thickness(24)};
  panel.Children.Add(new TextBlock{Text="Você pode continuar usando o programa, aguardar a conclusão ou solicitar cancelamento e fechar após a gravação segura.",TextWrapping=TextWrapping.Wrap});
  var status=new TextBlock{Text=string.Join("\n",tasks.Select(x=>x.OperationStatus)),TextWrapping=TextWrapping.Wrap};panel.Children.Add(new ScrollViewer{Content=status,MaxHeight=150,Margin=new Thickness(0,16,0,16),VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  var actions=new WrapPanel();var keep=new Button{Content="Continuar aberto"};var wait=new Button{Content="Aguardar e fechar"};var cancel=new Button{Content="Cancelar tarefas e fechar"};actions.Children.Add(keep);actions.Children.Add(wait);actions.Children.Add(cancel);panel.Children.Add(actions);
  bool closeWhenReady=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(100)};
  keep.Click+=(_,_)=>DialogResult=false;
  wait.Click+=(_,_)=>{closeWhenReady=true;wait.IsEnabled=cancel.IsEnabled=false;};
  cancel.Click+=(_,_)=>{foreach(var task in tasks)task.CancelOperation();closeWhenReady=true;wait.IsEnabled=cancel.IsEnabled=false;};
  timer.Tick+=(_,_)=>{status.Text=string.Join("\n",tasks.Select(x=>x.OperationStatus));if(closeWhenReady&&tasks.All(x=>!x.IsWorking))DialogResult=true;};
  Closed+=(_,_)=>{timer.Stop();if(DialogResult==true)Dispatcher.BeginInvoke(closeApplication);};timer.Start();Content=panel;
 }
}
