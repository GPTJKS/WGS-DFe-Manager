using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WgsDfe.Desktop;
using WgsDfe.Infrastructure;

internal static class RefinementScenario
{
 public static async Task Run(MainWindow main,FoundationRuntime runtime,Action<string> snapshot,Action<bool,string> check,Action<string> click)
 {
  foreach(var size in new[]{(1000,700),(1366,768),(1920,1080)})
  foreach(double scale in new[]{1d,1.25,1.5})
  {
   main.Width=size.Item1;main.Height=size.Item2;((FrameworkElement)main.Content).LayoutTransform=new ScaleTransform(scale,scale);
   main.OpenView(new ExportWindow(runtime));await Wait(()=>main.ActiveView is ExportWindow e&&!e.IsWorking);
   main.UpdateLayout();await Task.Delay(100);await Wait(()=>main.ActiveView is ExportWindow e&&!e.IsWorking);var export=(ExportWindow)main.ActiveView!;
   foreach(var expander in Walk(export).OfType<Expander>().ToArray())expander.IsExpanded=true;
   foreach(bool maximize in new[]{false,true})
   {
    if(main.Dialogs.IsMaximized!=maximize)click("Maximizar / restaurar janela interna");
    main.UpdateLayout();await Task.Delay(50);
    string label=$"exportacao-expandida-{size.Item1}x{size.Item2}-escala{scale:0.00}-{(maximize?"max":"normal")}";
    var scroll=(ScrollViewer)export.FindName("ExportBody");scroll.ScrollToTop();main.UpdateLayout();snapshot(label+"-inicio");
    var companies=(DataGrid)export.FindName("CompaniesGrid");check(companies.Columns[0].ActualWidth>250,label+" company column has useful width");
    var advanced=Walk(export).OfType<Expander>().Single(x=>x.Header.ToString()!.StartsWith("Opções de conteúdo"));advanced.BringIntoView();main.UpdateLayout();snapshot(label+"-opcoes");
    var boxes=Walk(export).OfType<CheckBox>().Where(x=>x.IsVisible).ToArray();check(boxes.Length>=10,label+" all advanced options are actually expanded");
    for(int i=0;i<boxes.Length;i++)for(int j=i+1;j<boxes.Length;j++)
    {var a=boxes[i].TransformToAncestor(export).TransformBounds(new Rect(boxes[i].RenderSize));var b=boxes[j].TransformToAncestor(export).TransformBounds(new Rect(boxes[j].RenderSize));check(!a.IntersectsWith(b),label+" options do not intersect");}
    scroll.ScrollToBottom();main.UpdateLayout();snapshot(label+"-fim");
    var cancel=(Button)export.FindName("CancelButton");var rect=cancel.TransformToAncestor(main).TransformBounds(new Rect(cancel.RenderSize));
    check(rect.Bottom<=main.ActualHeight&&rect.Top>=0,label+" pinned cancellation is within window");
   }
   main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  }
  ((FrameworkElement)main.Content).LayoutTransform=Transform.Identity;main.Width=1366;main.Height=768;
  click("Consultas");await Task.Delay(200);main.UpdateLayout();
  var companySearch=Walk(main).OfType<TextBox>().First(x=>x.Name=="CompanySearch");
  companySearch.Text="clietne";await Task.Delay(400);main.UpdateLayout();
  var companyGrid=Walk(main).OfType<DataGrid>().First(x=>x.Name=="CompaniesGrid");
  check(companyGrid.Items.Count==1,"company UI finds a transposed name");snapshot("pesquisa-empresa-aproximada");
  companySearch.Text="12345678000275";await Task.Delay(400);check(companyGrid.Items.Count==0,"company UI never approximates a wrong CNPJ");companySearch.Clear();await Task.Delay(200);
  foreach(var tabs in Walk(main).OfType<TabControl>().Where(x=>x.IsVisible))
  {
   var headers=Walk(tabs).OfType<TabItem>().Where(x=>x.IsVisible).ToArray();
   for(int i=0;i<headers.Length;i++)for(int j=i+1;j<headers.Length;j++)
   {var a=headers[i].TransformToAncestor(main).TransformBounds(new Rect(headers[i].RenderSize));var b=headers[j].TransformToAncestor(main).TransformBounds(new Rect(headers[j].RenderSize));check(!a.IntersectsWith(b),"segmented headers do not intersect");}
  }
  main.OpenView(new AutomationWindow(runtime));await Task.Delay(250);snapshot("automacao-refinada");
  var automation=(AutomationWindow)main.ActiveView!;var selector=(ComboBox)automation.FindName("CompanyBox");selector.SelectedIndex=0;var selected=selector.SelectedItem;selector.IsDropDownOpen=true;await Task.Delay(50);
  var popup=(System.Windows.Controls.Primitives.Popup)selector.Template.FindName("PART_Popup",selector);var search=Walk(popup.Child).OfType<TextBox>().First();search.Text="clietne";await Task.Delay(400);
  check(Walk(popup.Child).OfType<ListBox>().Single().Items.Count==1,"company selector supports transposed name search");check(ReferenceEquals(selector.SelectedItem,selected),"selector search preserves chosen identity until explicit choice");selector.IsDropDownOpen=false;
  main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  Motion.Disabled=false;typeof(Motion).GetProperty("SystemAnimationOverrideForTests",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.SetValue(null,true);
  var resultTabs=Walk(main).OfType<TabControl>().First(x=>x.Name=="ResultTabs");resultTabs.SelectedIndex=resultTabs.SelectedIndex==0?1:0;await Task.Delay(40);
  var tabContent=(FrameworkElement)resultTabs.SelectedContent;check(tabContent.Opacity>0&&tabContent.Opacity<1,"result tab has intermediate opacity without recreating page");snapshot("movimento-aba-040ms");await Task.Delay(220);snapshot("movimento-aba-260ms");
  snapshot("movimento-modulo-000ms");click("Configurações");
  var clock=System.Diagnostics.Stopwatch.StartNew();
  foreach(int frame in new[]{40,100,220}){int delay=frame-(int)clock.ElapsedMilliseconds;if(delay>0)await Task.Delay(delay);if(frame==40&&Motion.Allowed){var workspace=(FrameworkElement)main.FindName("Workspace");check(workspace.Opacity<1&&workspace.Opacity>0,"module transition has intermediate opacity on live navigation");check(workspace.RenderTransform is TranslateTransform move&&move.HasAnimatedProperties&&move.Y>=0&&move.Y<=8,"module transition uses an animated displacement in the specified range");}snapshot($"movimento-modulo-{frame:D3}ms-real{clock.ElapsedMilliseconds:D3}");}
  main.OpenView(new ExportWindow(runtime));clock.Restart();
  foreach(int frame in new[]{40,100,240}){int delay=frame-(int)clock.ElapsedMilliseconds;if(delay>0)await Task.Delay(delay);snapshot($"movimento-modal-{frame:D3}ms-real{clock.ElapsedMilliseconds:D3}");}
  main.Back();await Wait(()=>!main.Dialogs.IsOpen);Motion.Disabled=true;typeof(Motion).GetProperty("SystemAnimationOverrideForTests",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.SetValue(null,null);
 }
 private static IEnumerable<DependencyObject> Walk(DependencyObject root)
 {for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Walk(child))yield return next;}}
 private static async Task Wait(Func<bool> ready){for(int i=0;i<1000;i++){if(ready())return;await Task.Delay(20);}throw new TimeoutException();}
}
