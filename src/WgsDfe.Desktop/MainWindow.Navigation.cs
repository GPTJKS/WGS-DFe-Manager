using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace WgsDfe.Desktop;
public partial class MainWindow
{
 private readonly Dictionary<string,WorkspaceView> pages=new();
 private readonly Stack<object> history=new();
 public WorkspaceView? ActiveView=>DialogHost.ActiveView??Workspace.Content as WorkspaceView;
 public WorkspaceDialogHost Dialogs=>DialogHost;
 private bool CanNavigate()=>!DialogHost.IsOpen&&(Workspace.Content is not WorkspaceView view||view.CanDeactivate());
 private void SetPage(object page)
 {
  var leave=Motion.CaptureExit(CompanyViewport.Visibility==Visibility.Visible?CompanyViewport:Workspace);
  bool companies=ReferenceEquals(page,CompanyPage);
  CompanyViewport.Visibility=companies?Visibility.Visible:Visibility.Collapsed;
  Workspace.Content=companies?null:page;Motion.Slide(companies?CompanyViewport:Workspace);
  leave();
  BackButton.Visibility=history.Count>0?Visibility.Visible:Visibility.Collapsed;
  ShellTitle.Text=companies?"Empresas":page is WorkspaceView current?current.Title:"WGS DF-e Manager 0.17.5";
  if(ShellTitle.Parent is FrameworkElement chipParent&&chipParent.Parent is FrameworkElement chipBorder)Motion.SlideChip(chipBorder);
  var section=companies?CompaniesNav:page switch{HomeDashboardView=>HomeNav,QueriesView=>QueriesNav,ArchiveWindow=>DocumentsNav,HistoricalWindow=>HistoricalNav,SettingsView=>SettingsNav,CertificateWindow=>CertificatesNav,InformationView info=>info.About?AboutNav:HelpNav,_=>null};
  if(section is not null){foreach(var button in new[]{HomeNav,QueriesNav,CompaniesNav,DocumentsNav,HistoricalNav,SettingsNav,CertificatesNav,HelpNav,AboutNav})button.Tag=ReferenceEquals(section,button)?"Selected":null;Motion.PulseNav(section);}
 }
 private void ShowPage(string key,Func<WorkspaceView> factory)
 {
  if(!CanNavigate())return;
  if(!pages.TryGetValue(key,out var page))pages.Add(key,page=factory());
  RememberPage(page);SetPage(page);
 }
 public void OpenView(WorkspaceView view)
 {
  if(view is ExportWindow)TrackBackground(view);
  if(view is CertificateWindow){ShowPage("certificados",()=>view);return;}
  if(view is InformationView information){ShowPage(information.About?"about":"help",()=>view);return;}
  if(DialogHost.IsOpen){DialogHost.Open(view);return;}
  // Opening an overlay preserves the current page and does not leave its operation.
  DialogHost.Open(view);
 }
 private void RememberPage(object next){object? current=CompanyViewport.Visibility==Visibility.Visible?CompanyPage:Workspace.Content;if(current is not null&&!ReferenceEquals(current,next))history.Push(current);}
 public void Back()
 {
  if(DialogHost.IsOpen){DialogHost.RequestClose();return;}
  if(!CanNavigate()||!history.TryPop(out var previous))return;
  SetPage(previous);
 }
 private void ClearHistory()
 {
  // Nested editors contain pending PFX bytes: dispose them when leaving the section.
  if(Workspace.Content is WorkspaceView current&&!pages.ContainsValue(current))current.DisposeView();
  foreach(var page in history.OfType<WorkspaceView>().Where(x=>!pages.ContainsValue(x)))page.DisposeView();
  history.Clear();
 }
 private bool CanCloseWorkspace()
 {
  if(OperationViews().Any(x=>x.IsWorking))
  {if(DialogHost.ActiveView is not CloseTasksView)OpenView(new CloseTasksView(OperationViews().Where(x=>x.IsWorking).ToArray(),RequestFinalClose));return false;}
  if(DialogHost.IsOpen){DialogHost.RequestClose();return false;}
  var preferences=pages.Values.OfType<QueriesView>().Select(x=>x.PeriodSaved).Where(x=>!x.IsCompleted).ToArray();
  if(preferences.Length>0){_ = FlushPreferencesAndClose(preferences);return false;}
  if(pages.Values.Concat(backgroundViews).Any(x=>!x.CanLeave()))return false;
  return true;
 }
 private bool waitingForPreferences;
 private async Task FlushPreferencesAndClose(Task[] writes)
 {
  if(waitingForPreferences)return;waitingForPreferences=true;
  try{await Task.WhenAll(writes);}finally{waitingForPreferences=false;}
  _ = Dispatcher.BeginInvoke(new Action(Close));
 }
 private void RequestFinalClose()
 {
  if(DialogHost.ActiveView is {HasUnsavedChanges:false,IsWorking:false} modal)
  {
   EventHandler? closed=null;closed=(_,_)=>{modal.Closed-=closed;Dispatcher.BeginInvoke(new Action(RequestFinalClose));};modal.Closed+=closed;
   if(!DialogHost.RequestClose())modal.Closed-=closed;return;
  }
  Close();
 }
 protected override void OnClosed(EventArgs e)
 {
  DialogHost.DisposeAll();ClearHistory();foreach(var view in pages.Values)view.DisposeView();foreach(var view in backgroundViews.ToArray())view.DisposeView();base.OnClosed(e);
 }
 private void Back_Click(object sender,RoutedEventArgs e)=>Back();
 private void Home_Click(object sender,RoutedEventArgs e)=>ShowHome();
 private void BrandHome_Click(object sender,MouseButtonEventArgs e)=>ShowHome();
 public void ShowHome(){ShowPage("home",()=>new HomeDashboardView(runtime,NavigateFromHome));if(pages.TryGetValue("home",out var p)&&p is HomeDashboardView h)_ = h.RefreshDashboardAsync();}
 private void NavigateFromHome(string target)
 {
  switch(target)
  {
   case "consultas":ShowPage("consultas",()=>new QueriesView(runtime));break;
   case "empresas":Companies_Click(this,new RoutedEventArgs());break;
   case "documentos":ShowPage("Archive",()=>new ArchiveWindow(runtime));break;
   case "recuperacao":ShowPage("Historical",()=>new HistoricalWindow(runtime));break;
   case "certificados":ShowPage("certificados",()=>new CertificateWindow(runtime));break;
   case "configuracoes":ShowPage("settings",()=>new SettingsView(runtime));break;
   case "inspecionar":Inspect_Click(this,new RoutedEventArgs());break;
   case "sobre":ShowPage("about",()=>new InformationView(true));break;
   default:ShowHome();break;
  }
 }
 private void Companies_Click(object sender,RoutedEventArgs e)
 {if(!CanNavigate())return;RememberPage(CompanyPage);SetPage(CompanyPage);_ = RefreshAsync();}
 private bool collapsed,navigationManual;
 private void ToggleNavigation_Click(object sender,RoutedEventArgs e)
 {
  navigationManual=true;ApplyNavigation(!collapsed);
 }
 private void ApplyNavigation(bool value)
 {
  DialogHost.SidebarInset=value?56:236;DialogHost.Resize();collapsed=value;SidebarColumn.Width=new GridLength(collapsed?56:236);BrandText.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;VersionLabel.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;SidebarCertCard.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;VersionFooter.Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;
  foreach(var button in new[]{HomeNav,QueriesNav,CompaniesNav,DocumentsNav,HistoricalNav,SettingsNav,CertificatesNav,HelpNav,AboutNav})
   {button.Padding=collapsed?new Thickness(6,6,0,6):new Thickness(10,6,10,6);if(button.Content is StackPanel content&&content.Children.Count>1)content.Children[1].Visibility=collapsed?Visibility.Collapsed:Visibility.Visible;}
 }
 private void Help_Click(object sender,RoutedEventArgs e)=>ShowPage("help",()=>new InformationView(false));
 private void About_Click(object sender,RoutedEventArgs e)=>ShowPage("about",()=>new InformationView(true));
 private void Settings_Click(object sender,RoutedEventArgs e)=>ShowPage("settings",()=>new SettingsView(runtime));
}
