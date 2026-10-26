using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Database;
using WgsDfe.Desktop;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;

internal static class InterfaceScenario
{
 private static readonly BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 private static int assertions;
 internal static int Run()
 {
  var directory=Path.Combine(Path.GetTempPath(),"wgs-v0175-"+Guid.NewGuid().ToString("N"));
  var app=new System.Windows.Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
  app.Resources=(ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/WgsDfe.Desktop;component/Theme.xaml",UriKind.Relative));
  Motion.Disabled=true;
  var runtime=new FoundationRuntime(new FoundationOptions{Application=new(){OrganizationId=Guid.NewGuid(),OrganizationName="TESTE SINTÉTICO",DataDirectory=directory}});
  var status=runtime.InitializeAsync().GetAwaiter().GetResult();
  using(var scope=runtime.Services.CreateScope())
  {
   var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();
   for(int i=0;i<60;i++)db.Companies.Add(new(){OrganizationId=db.OrganizationId,LegalName=i==0?"S.T.J Ferramentaria":$"Empresa teste {i:D2}",Cnpj=$"12345678{i:D4}95",State="SC",EnabledDocuments=EnabledDocuments.All});
   db.SaveChanges();
   var xml=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture.xml")).Replace("<tpAmb>2</tpAmb>","<tpAmb>1</tpAmb>");
   var fixture=directory+"-fixture.xml";File.WriteAllText(fixture,xml);
   var imported=scope.ServiceProvider.GetRequiredService<IArchiveService>().ImportAsync(new([fixture])).GetAwaiter().GetResult();
   Check(imported.Imported==1,"fixture import: "+string.Join(";",imported.Rows.Select(x=>x.Code+" "+x.Reason)));
  }
  var main=new MainWindow(status,runtime){Width=1280,Height=900,WindowState=WindowState.Normal,WindowStartupLocation=WindowStartupLocation.Manual,Left=-12000,Top=0,ShowActivated=false};
  app.MainWindow=main;int exit=1;
  main.Show();
  main.Dispatcher.BeginInvoke(new Action(async()=>
  {
   try
   {
    await Until(()=>main.ActiveView is HomeDashboardView);
    var searchTime=System.Diagnostics.Stopwatch.StartNew();
    var matches=await Task.Run(()=>Enumerable.Range(0,10000).Count(i=>SearchMatcher.Rank("STJ",[i==0?"S.T.J Ferramentaria":$"Empresa {i:D5}"],[$"12345678{i:D4}95"]).HasValue));
    searchTime.Stop();Check(matches==1,"company matcher scans ten thousand synthetic companies correctly");
    Console.WriteLine($"Company search: 10,000 synthetic rows in {searchTime.ElapsedMilliseconds} ms.");
    foreach(var size in new[]{(760d,540d),(1000d,700d),(1280d,900d),(1920d,1080d)})
    {
     main.Width=size.Item1;main.Height=size.Item2;
     foreach(var zoom in new[]{.75,1d,1.5})
     {
      Invoke(main,"SetZoom",zoom);main.UpdateLayout();
      var viewport=(ScrollViewer)main.Content;var surface=(FrameworkElement)viewport.Content;
      Check(surface.LayoutTransform is ScaleTransform scale&&Math.Abs(scale.ScaleX-zoom)<.001,"zoom scales entire interface");
      Check(surface.ActualWidth>=959&&surface.ActualHeight>=639,"logical canvas stays accessible");
      Check(Math.Abs(UserPreferences.Load(directory).InterfaceZoom-zoom)<.001,"zoom preference persisted");
     }
    }
    Shortcut(main,Key.D0,ModifierKeys.Control);Check(UserPreferences.Load(directory).InterfaceZoom==1,"Ctrl+0 resets zoom");
    main.Width=1280;main.Height=900;
    Click(main,"CompaniesNav");await Until(()=>((DataGrid)main.FindName("CompaniesGrid")).Items.Count==60);
    Shortcut(main,Key.F,ModifierKeys.Control);Check(((TextBox)main.FindName("SearchBox")).IsKeyboardFocused,"Ctrl+F focuses company search");
    ((TextBox)main.FindName("SearchBox")).Text="STJ";await Until(()=>((DataGrid)main.FindName("CompaniesGrid")).Items.Count==1);
    Check(true,"company service finds STJ in S.T.J");
    Click(main,"QueriesNav");await Until(()=>main.ActiveView is QueriesView {ActiveView:NfeWindow});
    var queries=(QueriesView)main.ActiveView!;
    var selector=(ComboBox)typeof(QueriesView).GetField("model",Private)!.GetValue(queries)!;
    foreach(int type in new[]{0,1,2,3,4})
    {
     selector.SelectedIndex=type;await Until(()=>queries.ActiveView is { } v&&((DataGrid)v.FindName("CompaniesGrid")).Items.Count==60);
     var view=queries.ActiveView!;await Until(()=>((Button)view.FindName("RunButton")).IsEnabled);var grid=(DataGrid)view.FindName("CompaniesGrid");
     grid.UnselectAll();Click(view,"SelectAllButton");Check(grid.SelectedItems.Count==60,"select all visible companies");
     Check(((Button)view.FindName("SelectAllButton")).Content.ToString()=="Desmarcar todas","selection label updates");
     Click(view,"SelectAllButton");Check(grid.SelectedItems.Count==0,"deselect all visible companies");
     Check(!((ComboBox)view.FindName("EnvironmentBox")).IsVisible,"environment selector removed");
     if(view is NfeWindow or CteWindow)
     {
      grid.SelectedIndex=1;
      ((TextBox)view.FindName("CompanySearch")).Text="STJ";await Until(()=>grid.Items.Count==1);
      Click(view,"SelectAllButton");Check(grid.SelectedItems.Count==1,"select all respects search filter");
      Click(view,"SelectAllButton");((TextBox)view.FindName("CompanySearch")).Clear();await Until(()=>grid.Items.Count==60);
      Check(grid.SelectedItems.Count==1,"selection outside search is preserved");
      grid.SelectedItems.Add(grid.Items[0]);
      Click(view,"ShowSelectedButton");await Until(()=>grid.Items.Count==2);
      Check(((Button)view.FindName("ShowSelectedButton")).Content.ToString()=="Mostrar todos","selected-only toggle label updates");
      main.UpdateLayout();
      var selectButton=(Button)view.FindName("SelectAllButton");var showButton=(Button)view.FindName("ShowSelectedButton");
      var actions=(FrameworkElement)selectButton.Parent;
      Check(selectButton.TransformToAncestor(actions).Transform(new Point(selectButton.ActualWidth,0)).X<showButton.TransformToAncestor(actions).Transform(new Point()).X,"company action buttons have separate space");
      var column=(ColumnDefinition)view.FindName("CompanyColumn");var previousWidth=column.Width;column.Width=new GridLength(195);main.UpdateLayout();
      Check(showButton.TransformToAncestor(actions).Transform(new Point(showButton.ActualWidth,0)).X<=actions.ActualWidth+.1&&selectButton.ActualWidth>0&&showButton.ActualWidth>0,"both company buttons fit the narrowest company panel");
      column.Width=previousWidth;main.UpdateLayout();
      var removed=grid.Items[0];grid.SelectedItems.Remove(removed);await Until(()=>grid.Items.Count==1);
      Check(!grid.Items.Contains(removed)&&grid.SelectedItems.Count==1,"deselected company immediately leaves selected-only list");
      Click(view,"SelectAllButton");await Until(()=>grid.Items.Count==0);
      Check(((Button)view.FindName("ShowSelectedButton")).IsEnabled,"empty selected-only list can return to all companies");
      Click(view,"ShowSelectedButton");await Until(()=>grid.Items.Count==60);
      Check(grid.SelectedItems.Count==0,"show all restores full list without restoring deselected companies");
      grid.UnselectAll();
     }
     var export=new ExportWindow(runtime);main.OpenView(export);await Until(()=>((DataGrid)export.FindName("CompaniesGrid")).Items.Count==60);
     main.UpdateLayout();Check(!((ComboBox)export.FindName("EnvironmentBox")).IsVisible,"export uses production without chooser");
     var destination=(TextBox)export.FindName("DestinationBox");var from=(DatePicker)export.FindName("From");
     Check(destination.TransformToAncestor(export).Transform(new Point()).Y<from.TransformToAncestor(export).Transform(new Point()).Y,"destination precedes always visible period");
     Check(!Parents(from).OfType<Expander>().Any(),"period is not in an expander");
     Check(Descendants(export).OfType<DataGrid>().All(g=>AdornerLayer.GetAdornerLayer(g) is { } layer&&export.IsAncestorOf(layer)),"empty feedback belongs to export area");
     var exportBody=(ScrollViewer)export.FindName("ExportBody");
     var exportCompanies=(DataGrid)export.FindName("CompaniesGrid");var inner=Descendants(exportCompanies).OfType<ScrollViewer>().First();
     ((CheckBox)export.FindName("AllCompanies")).IsChecked=false;
     inner.ScrollToTop();exportBody.ScrollToTop();main.UpdateLayout();
     var first=Descendants(exportCompanies).OfType<TextBlock>().First(x=>x.IsVisible);
     Wheel(first,-120);main.UpdateLayout();Check(inner.VerticalOffset>0&&exportBody.VerticalOffset==0,"wheel scrolls company list first");
     inner.ScrollToBottom();main.UpdateLayout();Wheel(first,-120);main.UpdateLayout();Check(exportBody.VerticalOffset>0,"wheel continues in outer panel at list boundary");
     ((ComboBox)export.FindName("EnvironmentBox")).SelectedIndex=2;
     var request=(WgsDfe.Application.Export.ExportRequest)typeof(ExportWindow).GetMethod("Request",Private)!.Invoke(export,null)!;
     Check(request.Filter.Environment==FiscalEnvironment.Production,"export request is production regardless of legacy selector state");
     if(type==0)
     {
      Directory.CreateDirectory(directory+"-exports");
      ((TextBox)export.FindName("DestinationBox")).Text=directory+"-exports";
      Descendants(export).OfType<Button>().Single(x=>x.Content?.ToString()=="Exportar para o destino").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
      await Until(()=>!export.IsWorking);
      Check(((TextBox)export.FindName("OutputPath")).Text.Length>0,"export completed: "+((TextBlock)export.FindName("Message")).Text);
      Check(Directory.GetFiles(((TextBox)export.FindName("OutputPath")).Text,"*.xml",SearchOption.AllDirectories).Length==1,"export UI writes original synthetic XML to selected folder");
     }
     main.Back();await Until(()=>!main.Dialogs.IsOpen);
    }
    Click(main,"DocumentsNav");await Until(()=>main.ActiveView is ArchiveWindow a&&((DataGrid)a.FindName("Documents")).ItemsSource is not null&&((ProgressBar)a.FindName("BusyBar")).Visibility==Visibility.Collapsed);
    Check(main.ActiveView is ArchiveWindow a2&&((DataGrid)a2.FindName("Documents")).Items.Count>0,"production document visible: "+(main.ActiveView is ArchiveWindow a3?((TextBlock)a3.FindName("Message")).Text:main.ActiveView?.Title));
    var archive=(ArchiveWindow)main.ActiveView!;var documents=(DataGrid)archive.FindName("Documents");documents.SelectedIndex=0;main.UpdateLayout();
    var fields=Descendants(archive).OfType<SelectableText>().Where(x=>x.IsVisible).ToArray();
    Check(fields.Length>=7&&fields.All(x=>x.IsReadOnly),"inspector fields support selecting and copying");
    var copy=Descendants(archive).OfType<Button>().First(x=>x.Content?.ToString()=="Copiar chave");
    copy.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var key=((ArchiveDocumentRow)documents.SelectedItem).Key;
    Check(Clipboard.GetText()==key&&key.Length==44&&key.All(char.IsAsciiDigit),"copy key writes exactly 44 validated digits");
    var clipboardType=typeof(MainWindow).Assembly.GetType("WgsDfe.Desktop.FiscalClipboard")!;string feedback="";
    clipboardType.GetMethod("CopyKey",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,["123",new Action<string>(value=>feedback=value)]);
    Check(feedback.StartsWith("Chave inválida")&&Clipboard.GetText()==key,"invalid key rejected without changing clipboard");
    Shortcut(main,Key.Escape,ModifierKeys.None);Check(documents.SelectedItem is null,"Esc closes inspector");
    var diagnostic=new QueryDiagnosticView("Empresa","NF-e","WAIT_UNTIL_ALLOWED",null,"0",0,0,0,null,"Aguardando prazo");main.OpenView(diagnostic);main.UpdateLayout();
    var details=Descendants(diagnostic).OfType<Expander>().Single(x=>x.Header?.ToString()=="Detalhes técnicos");
    Check(!details.IsExpanded,"technical diagnostic details collapsed by default");
    Check(Descendants(diagnostic).OfType<TextBlock>().Any(x=>x.Text.Contains("horário não informado")),"diagnostic does not invent release time");
    details.IsExpanded=true;main.UpdateLayout();
    var diagnosticText=Descendants(diagnostic).OfType<TextBox>().First();diagnosticText.Focus();
    RoutedEscape(diagnosticText);await Until(()=>!main.Dialogs.IsOpen);
    Check(true,"routed Esc from a text field closes the modal");
    Shortcut(main,Key.F1,ModifierKeys.None);Check(main.ActiveView is InformationView {About:false},"F1 opens Help");
    Check(Descendants(main.ActiveView!).OfType<DataGrid>().Any(x=>x.Items.Count==6),"Help documents implemented shortcuts");
    RoutedEscape(Descendants(main.ActiveView!).OfType<DataGrid>().First());Check(main.ActiveView is ArchiveWindow,"routed Esc restores previous screen");
    Shortcut(main,Key.F1,ModifierKeys.None);Shortcut(main,Key.Left,ModifierKeys.Alt);Check(main.ActiveView is ArchiveWindow,"Alt+Left restores previous screen");
    var editor=new CompanyEditor(null,runtime.Services.GetRequiredService<WgsDfe.Infrastructure.Companies.CompanyLookup>(),"",(_,_,_)=>Task.CompletedTask);
    main.OpenView(editor);main.UpdateLayout();
    // Exercise the existing unsaved-change guard through the same Back path used by shortcuts.
    typeof(WorkspaceView).GetProperty("HasUnsavedChanges")!.SetValue(editor,true);
    RoutedEscape((TextBox)editor.FindName("LegalNameBox"));Check(main.Dialogs.IsConfirmingDiscard&&ReferenceEquals(main.ActiveView,editor),"Esc protects unsaved changes");
    RoutedEscape((Button)main.Dialogs.FindName("ContinueEditing"));Check(!main.Dialogs.IsConfirmingDiscard&&ReferenceEquals(main.ActiveView,editor),"Esc dismisses discard prompt and keeps editing");
    ((Button)main.Dialogs.FindName("DiscardButton"))?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    Check(!runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests,"no fiscal network requests enabled");
    Console.WriteLine($"PASS: {assertions} interface assertions; isolated synthetic data; no screenshots or fiscal requests.");exit=0;
   }
   catch(Exception ex){Console.Error.WriteLine(ex);}
   finally
   {
    main.Dialogs.DisposeAllForTest();app.Shutdown(exit);
   }
  }));
  app.Run();runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();return exit;
 }
 private static void Invoke(object target,string method,params object[] args)=>target.GetType().GetMethod(method,Private)!.Invoke(target,args);
 private static void Shortcut(MainWindow main,Key key,ModifierKeys modifiers){Invoke(main,"HandleShortcut",key,modifiers);main.UpdateLayout();}
 private static void Click(FrameworkElement parent,string name)=>((Button)parent.FindName(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
 private static void Wheel(UIElement target,int delta)=>target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,Environment.TickCount,delta){RoutedEvent=UIElement.PreviewMouseWheelEvent});
 private static void RoutedEscape(UIElement target)=>target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(target),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
 private static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);assertions++;}
 private static async Task Until(Func<bool> ready){var until=DateTime.UtcNow.AddSeconds(15);while(!ready()){if(DateTime.UtcNow>until)throw new TimeoutException("Interface state did not become ready");await Task.Delay(30);}}
 private static IEnumerable<DependencyObject> Descendants(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
 private static IEnumerable<DependencyObject> Parents(DependencyObject root){for(var node=VisualTreeHelper.GetParent(root);node is not null;node=VisualTreeHelper.GetParent(node))yield return node;}
 private static void DisposeAllForTest(this WorkspaceDialogHost host)=>typeof(WorkspaceDialogHost).GetMethod("DisposeAll",Private)!.Invoke(host,null);
}
