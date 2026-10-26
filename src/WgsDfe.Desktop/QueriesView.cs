using System.Windows;
using System.Windows.Controls;
using WgsDfe.Infrastructure;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
/// <summary>All fiscal sources share one navigation section and document selector.</summary>
public sealed class QueriesView:WorkspaceView
{
 private readonly Dictionary<int,WorkspaceView> views=new();
 private readonly ComboBox model=new(){Width=165,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0)};
 private readonly ContentControl host=new(){HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
 private int selected=-1;private bool reverting;
 private (DateTime? From,DateTime? To)? sharedPeriod;
 private readonly SemaphoreSlim preferenceGate=new(1,1);
 private Task periodSaved=Task.CompletedTask;
 private bool periodReady;
 public Task PeriodSaved=>periodSaved;
 public WorkspaceView? ActiveView=>host.Content as WorkspaceView;
 public IEnumerable<WorkspaceView> AllViews=>views.Values;
 public override bool CanRunInBackground=>true;
 public override bool IsWorking=>views.Values.Any(x=>x.IsWorking);
 public void Select(WorkspaceView view){foreach(var pair in views)if(ReferenceEquals(pair.Value,view)){model.SelectedIndex=pair.Key;return;}}
 public QueriesView(FoundationRuntime runtime)
 {
  Title="Consultas e downloads";
  FirstLoaded+=async(_,_)=>
  {
   IsEnabled=false;
   try{var preferences=await Task.Run(()=>UserPreferences.Load(runtime.DataDirectory));if(preferences.HasQueryPeriod){sharedPeriod=(preferences.QueryFrom,preferences.QueryTo);if(ActiveView?.FindName("Period") is PeriodDocumentsPanel panel)panel.SelectedPeriod=sharedPeriod.Value;}periodReady=true;}
   finally{IsEnabled=true;}
  };
  var selector=new StackPanel{Margin=new Thickness(0,0,8,2)};selector.Children.Add(new TextBlock{Text="Tipo de documento fiscal",Style=(Style)FindResource("Eyebrow")});selector.Children.Add(model);
  model.ItemsSource=new[]{"NF-e","NFC-e (SC)","NF-e e NFC-e (SC)","NFCom (SC)","CT-e e CT-e OS"};Content=host;
  model.SelectionChanged+=(_,_)=>
  {
   if(reverting)return;
   if(ActiveView is not null&&!ActiveView.CanDeactivate()){reverting=true;model.SelectedIndex=selected;reverting=false;return;}
   if(ActiveView?.FindName("Period") is PeriodDocumentsPanel previousPeriod&&previousPeriod.TryGetSharedPeriod(out var latest))sharedPeriod=latest;
   selected=model.SelectedIndex;
   if(!views.TryGetValue(selected,out var view))
   {
    view=selected switch{1=>new NfeWindow(runtime,DocumentModel.NFCe),2=>new NfeWindow(runtime,combined:true),3=>new NfeWindow(runtime,DocumentModel.NFCom),4=>new CteWindow(runtime),_=>new NfeWindow(runtime)};
    if(view is NfeWindow nfe)((ComboBox)nfe.FindName("TypeBox")).Visibility=Visibility.Collapsed;
    views.Add(selected,view);
    if(view.FindName("Period") is PeriodDocumentsPanel panel)panel.PeriodChanged+=(_,_)=>
    {
     if(!periodReady||!panel.TryGetSharedPeriod(out var valid))return;
     sharedPeriod=valid;periodSaved=SavePeriodAsync(runtime,valid);
    };
   }
   if(selector.Parent is ContentControl previous)previous.Content=null;
   ((ContentControl)view.FindName("DocumentTypeHost")).Content=selector;host.Content=view;Motion.Slide(view);
   if(sharedPeriod.HasValue&&!view.IsWorking&&view.FindName("Period") is PeriodDocumentsPanel next)next.SelectedPeriod=sharedPeriod.Value;
  };
  model.SelectedIndex=0;
  Closing+=(_,args)=>{if(views.Values.Any(x=>!x.CanLeave()))args.Cancel=true;};
  Closed+=(_,_)=>{foreach(var view in views.Values)view.DisposeView();};
 }
 private async Task SavePeriodAsync(FoundationRuntime runtime,(DateTime? From,DateTime? To) period)
 {
  await preferenceGate.WaitAsync();
  try{await Task.Run(()=>{UserPreferences.Update(runtime.DataDirectory,current=>current with{QueryFrom=period.From,QueryTo=period.To,HasQueryPeriod=true});});}
  catch(System.IO.IOException){ }catch(UnauthorizedAccessException){ }
  finally{preferenceGate.Release();}
 }
}
