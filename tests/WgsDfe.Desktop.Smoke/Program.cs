using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Controls.Primitives;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WgsDfe.Database;
using WgsDfe.Desktop;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Application.Archive;

internal static class Program
{
 [STAThread]private static int Main(string[] args)
 {
  if(args.Contains("--v0175-checks"))return InterfaceScenario.Run();
  string output=Path.GetFullPath(args.FirstOrDefault()??"docs/validation/v0.16.0/ui");Directory.CreateDirectory(output);
  string data=Path.Combine(Path.GetTempPath(),"wgs-ui-smoke-"+Guid.NewGuid().ToString("N"));string exports=data+"-exports";Directory.CreateDirectory(exports);int exit=1;
  Motion.Disabled=true;var app=new System.Windows.Application();app.Resources=(ResourceDictionary)System.Windows.Application.LoadComponent(new Uri("/WgsDfe.Desktop;component/Theme.xaml",UriKind.Relative));app.ShutdownMode=ShutdownMode.OnExplicitShutdown;
  System.Globalization.CultureInfo.DefaultThreadCurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
  if(args.Skip(1).Contains("--compare-only")){VisualComparison.CompareExisting(output);return 0;}
  typeof(MainWindow).Assembly.GetType("WgsDfe.Desktop.DateInput")!.GetMethod("Register")!.Invoke(null,null);
  var runtime=new FoundationRuntime(new FoundationOptions{Application=new(){OrganizationId=Guid.NewGuid(),OrganizationName="TESTE SINTÉTICO",DataDirectory=data}},services=>{services.AddSingleton<DistributionUiProbe>();services.RemoveAll<WgsDfe.Application.Nfe.INfeDistributionProvider>();foreach(var source in new[]{WgsDfe.Application.Nfe.NfeSources.National,WgsDfe.Application.Nfe.NfeSources.SantaCatarina,WgsDfe.Application.Nfe.NfeSources.SantaCatarinaNfce,WgsDfe.Application.Nfe.NfeSources.SantaCatarinaNfcom})services.AddSingleton<WgsDfe.Application.Nfe.INfeDistributionProvider>(provider=>new ControlledDistribution(source,provider.GetRequiredService<DistributionUiProbe>()));services.AddSingleton<CertificateUiProbe>();services.AddScoped<WgsDfe.Application.Certificates.ICertificateService>(provider=>new ProbedCertificates(provider.GetRequiredService<WgsDfe.Infrastructure.Certificates.CertificateService>(),provider.GetRequiredService<CertificateUiProbe>()));});
  var status=runtime.InitializeAsync().GetAwaiter().GetResult();
  using(var scope=runtime.Services.CreateScope())
  {var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();db.Companies.Add(new(){OrganizationId=db.OrganizationId,LegalName="Empresa sintética",Cnpj="12345678000195",State="SC"});db.Companies.Add(new(){OrganizationId=db.OrganizationId,LegalName="Cliente sintético",Cnpj="12345678000276",State="SC"});db.SaveChanges();}
  string fixture=data+"-fixture.xml";File.WriteAllText(fixture,File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"fixture.xml")).Replace("<tpAmb>2</tpAmb>","<tpAmb>1</tpAmb>"));
  using(var scope=runtime.Services.CreateScope())scope.ServiceProvider.GetRequiredService<IArchiveService>().ImportAsync(new([fixture])).GetAwaiter().GetResult();
  var main=new MainWindow(status,runtime){WindowState=WindowState.Normal,Width=1280,Height=900};app.MainWindow=main;
  var log=new List<string>();main.Show();
  main.Dispatcher.BeginInvoke(new Action(async()=>
  {
   try
   {
    await Until(()=>main.ActiveView is HomeDashboardView);((Button)main.FindName("QueriesNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    await Until(()=>main.ActiveView! is QueriesView {ActiveView:NfeWindow});await Task.Delay(300);
    var query=(NfeWindow)((QueriesView)main.ActiveView!).ActiveView!;
    await Until(()=>((DataGrid)query.FindName("CompaniesGrid")).Items.Count==2);
    var companies=(DataGrid)query.FindName("CompaniesGrid");companies.SelectAll();Check(companies.SelectedItems.Count==2,"multiple company selection");
    var period=(PeriodDocumentsPanel)query.FindName("Period");
    var from=(DatePicker)period.FindName("FromDate");var to=(DatePicker)period.FindName("ToDate");
    from.SelectedDate=new DateTime(2026,10,20);to.SelectedDate=new DateTime(2026,10,1);Check(!period.ValidatePeriod(),"reversed dates rejected");from.SelectedDate=new DateTime(2026,9,1);to.SelectedDate=new DateTime(2026,9,30);Check(period.ValidatePeriod(),"valid period accepted");
    var dateText=(DatePickerTextBox)from.Template.FindName("PART_TextBox",from);dateText.Text="31/02/2026";Check(!period.ValidatePeriod(),"impossible typed date rejected");from.SelectedDate=null;from.SelectedDate=new DateTime(2026,9,1);
    var letter=new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice,new TextComposition(InputManager.Current,dateText,"A")){RoutedEvent=TextCompositionManager.PreviewTextInputEvent};dateText.RaiseEvent(letter);Check(letter.Handled,"date field blocks letters");
    ((ComboBox)query.FindName("EnvironmentBox")).SelectedIndex=0;await period.RefreshAsync();Check(((DataGrid)period.FindName("Documents")).Items.Count==1,"local XML appears in consultation results");
    Snapshot("consultas-1280x900");
    main.Width=1000;main.Height=700;await Task.Delay(150);main.UpdateLayout();Snapshot("consultas-1000x700");
    Check(companies.ActualHeight>=75,"company table has visible rows at minimum size");
    var results=(DataGrid)period.FindName("Documents");Check(results.ActualHeight>=75,"result table has visible rows at minimum size");
    main.UpdateLayout();var renderedRow=(DataGridRow)results.ItemContainerGenerator.ContainerFromIndex(0);Check(renderedRow is not null&&renderedRow.TransformToAncestor(results).Transform(new Point(0,renderedRow.ActualHeight)).Y<=results.ActualHeight,"result row fits below header at minimum size");
    await ClickReady(main,"Exportar XMLs do período");await Until(()=>main.ActiveView! is ExportWindow exportView&&((DataGrid)exportView.FindName("CompaniesGrid")).Items.Count==2);
    var export=(ExportWindow)main.ActiveView!;((TextBox)export.FindName("DestinationBox")).Text=exports;await ClickReady(main,"Exportar para o destino");await Until(()=>((TextBox)export.FindName("OutputPath")).Text.Length>0);
    var package=((TextBox)export.FindName("OutputPath")).Text;Check(Directory.GetFiles(package,"*.xml",SearchOption.AllDirectories).Length==1,"UI export writes the selected local XML");Check(Directory.GetFiles(package,"*",SearchOption.AllDirectories).All(x=>Path.GetExtension(x)==".xml"),"UI export writes only XMLs with no reports or manifest");Snapshot("exportacao-concluida");
    var exportGate=runtime.Services.GetRequiredService<WgsDfe.Infrastructure.Export.ExportGate>();
    var semaphore=(SemaphoreSlim)typeof(WgsDfe.Infrastructure.Export.ExportGate).GetProperty("Semaphore",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(exportGate)!;
    await semaphore.WaitAsync();
    try
    {
     int published=Directory.GetFiles(exports,"*.xml",SearchOption.AllDirectories).Length;
     await ClickReady(main,"Exportar para o destino");
     Check(((ProgressBar)export.FindName("BusyBar")).Visibility==Visibility.Visible,"export shows active progress");main.UpdateLayout();var exportFrame=(Border)main.Dialogs.FindName("Frame");var exportCancel=(Button)export.FindName("CancelButton");Check(exportCancel.TransformToAncestor(exportFrame).Transform(new Point(0,exportCancel.ActualHeight)).Y<=exportFrame.ActualHeight,"export cancellation remains visible in compact modal");
     main.Back();Check(ReferenceEquals(main.ActiveView!,export),"active export prevents unsafe navigation");
     Snapshot("exportacao-em-andamento");await ClickReady(main,"Cancelar");
     await Until(()=>((ProgressBar)export.FindName("BusyBar")).Visibility==Visibility.Collapsed);
     Check(((TextBlock)export.FindName("Message")).Text.StartsWith("Cancelado"),"export cancellation reaches safe completion");
     Check(Directory.GetFiles(exports,"*.xml",SearchOption.AllDirectories).Length==published,"cancelled export publishes no package");Descendants(export).OfType<ScrollViewer>().First().ScrollToBottom();main.UpdateLayout();Snapshot("exportacao-cancelada");
    }
    finally{semaphore.Release();}
    await Until(()=>Ready(main.ActiveView));main.Back();await Task.Delay(150);
    await ClickReady(main,"Empresas");await Task.Delay(200);await ClickReady(main,"+  Nova empresa");await Until(()=>main.ActiveView! is CompanyEditor);Check(app.Windows.Count==1,"company editor stays inside main window");await ClickReady(main,"Cancelar");await Task.Delay(150);
    await ClickReady(main,"Consultas");await Task.Delay(150);Check(ReferenceEquals(query,((QueriesView)main.ActiveView!).ActiveView),"consultation screen state is preserved");Check(companies.SelectedItems.Count==2,"company selection preserved after navigation");
    await Until(()=>Ready(main.ActiveView));await ClickReady(main,"Documentos");await Until(()=>main.ActiveView! is ArchiveWindow);await Until(()=>((ProgressBar)((ArchiveWindow)main.ActiveView!).FindName("BusyBar")).Visibility==Visibility.Collapsed);log.Add("TRACE archive loaded; ready="+Ready(main.ActiveView));await ClickReady(main,"Histórico de exportações");await Task.Delay(200);log.Add("TRACE history requested; active="+main.ActiveView?.GetType().Name+" ready="+Ready(main.ActiveView)+" modal="+main.Dialogs.IsOpen);await Until(()=>main.ActiveView! is ExportHistoryView);Check(app.Windows.Count==1,"export history stays inside main window");await Until(()=>Ready(main.ActiveView));main.Back();await Task.Delay(150);
    main.Width=1280;main.Height=900;Snapshot("documentos");
    var archive=(ArchiveWindow)main.ActiveView!;
    ((DataGrid)archive.FindName("Documents")).SelectedIndex=0;
    foreach(var expander in Descendants(archive).OfType<Expander>().Where(x=>x.Header?.ToString()=="Diagnóstico avançado"))expander.IsExpanded=true;
    await ClickReady(main,"Detalhes / versões / XML");await Until(()=>main.ActiveView! is ArchiveDetailWindow);
    var detail=(ArchiveDetailWindow)main.ActiveView!;
    ((DataGrid)detail.FindName("Versions")).SelectedIndex=0;await ClickReady(main,"Ver XML da versão selecionada");
    await Until(()=>((TextBox)detail.FindName("Xml")).Text.Length>0);Snapshot("documento-detalhes-xml");await Until(()=>Ready(main.ActiveView));main.Back();await Task.Delay(100);
    await ClickReady(main,"Conferir integridade");await Until(()=>((ProgressBar)archive.FindName("BusyBar")).Visibility==Visibility.Collapsed);Snapshot("integridade");
    await ClickReady(main,"Configurações");await Until(()=>main.ActiveView! is SettingsView);Snapshot("configuracoes");
    await ClickReady(main,"Certificados");await Until(()=>main.ActiveView! is CertificateWindow);await Task.Delay(200);Check(app.Windows.Count==1,"certificates stay inside main window");main.Back();
    main.OpenView(new ExportWindow(runtime));await Task.Delay(200);Snapshot("exportacao");
    await Until(()=>Ready(main.ActiveView));main.Back();main.OpenView(new HistoricalWindow(runtime));await Task.Delay(300);Snapshot("historico");
    await ValidateMatrix();await RefinementScenario.Run(main,runtime,Snapshot,Check,label=>Click(main,label));await BackgroundUiScenario.Run(main,runtime,Snapshot,Check);await CertificateUiScenario.Run(main,runtime,data,Snapshot,Check);await ExportStructureScenario.Run(main,runtime,output,Snapshot,Check,label=>Click(main,label));await VisualComparison.PrepareFixtures(runtime);await VisualComparison.Capture(main,output,Snapshot,label=>Click(main,label));VisualComparison.CompareUserEvidence(output);VisualComparison.CompareRoundTwoEvidence(output);VisualComparison.CompareExisting(Path.GetFullPath("artifacts/stitch-v2/initial"));VisualComparison.CompareExisting(Path.GetFullPath("artifacts/stitch-v2/round1"));
    Check(!runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests,"navigation alone never enables official requests");await BackgroundUiScenario.SafeExit(main,runtime,Snapshot,Check);exit=0;
    async Task ValidateMatrix()
    {
     await Until(()=>Ready(main.ActiveView));main.Back();await Until(()=>!main.Dialogs.IsOpen);
     ((TabControl)archive.FindName("Tabs")).SelectedIndex=0;foreach(var expander in Descendants(archive).OfType<Expander>())expander.IsExpanded=false;foreach(var size in new[]{(1000d,700d),(1366d,768d),(1920d,1080d)})
     {
      main.Width=size.Item1;main.Height=size.Item2;await Task.Delay(100);
      foreach(bool expanded in new[]{true,false})
      {
       typeof(MainWindow).GetMethod("ApplyNavigation",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(main,new object[]{!expanded});
       foreach(string section in new[]{"Consultas","Empresas","Documentos","Configurações","Certificados","Ajuda","Sobre"})
       {
        await ClickReady(main,section);await Task.Delay(200);await Until(()=>!Descendants(main.ActiveView??(DependencyObject)main.FindName("CompanyPage")).OfType<ProgressBar>().Any(x=>x.IsVisible));main.UpdateLayout();
        string name=$"{section.ToLowerInvariant()}-{size.Item1}x{size.Item2}-{(expanded?"expandido":"recolhido")}";
        Snapshot(name);
        if(section=="Documentos")Check(((DataGrid)main.ActiveView!.FindName("Documents")).ActualHeight>=115,name+" useful archive table");
        if(section=="Empresas")Check(((DataGrid)main.FindName("CompaniesGrid")).ActualHeight>=100,name+" usable table");
        if(section=="Consultas")
        {
         Check(companies.ActualHeight>=75,name+" usable company list");
         Check(results.ActualHeight>=75,name+" usable result list");
         var advanced=Descendants(query).OfType<Expander>().First(x=>x.Header?.ToString()=="Fonte, ambiente e opções de consulta");
         advanced.IsExpanded=true;main.UpdateLayout();
         Check(companies.ActualHeight>=75&&results.ActualHeight>=75,name+" advanced options preserve both lists");
         if(size.Item1==1000)Snapshot(name+"-opcoes-abertas");
         advanced.IsExpanded=false;
        }
       }
      }
     }
     main.Width=1366;main.Height=768;
     await ClickReady(main,"Consultas");await Task.Delay(100);
     var models=Descendants((DependencyObject)main.ActiveView!).OfType<ComboBox>().First(x=>x.Items.Count==5&&x.Items[0]?.ToString()=="NF-e");
     for(int index=1;index<5;index++){models.SelectedIndex=index;await Task.Delay(200);Snapshot("consulta-modelo-"+index);}
     models.SelectedIndex=0;
     await ClickReady(main,"Empresas");await Task.Delay(100);await ClickReady(main,"+  Nova empresa");await Task.Delay(100);Snapshot("editor-empresa");await ClickReady(main,"Cancelar");
     main.OpenView(new AutomationWindow(runtime));await Task.Delay(200);Snapshot("automacao");await Until(()=>Ready(main.ActiveView));main.Back();await Until(()=>!main.Dialogs.IsOpen);
     main.OpenView(new QueryDiagnosticView("Empresa sintética","Simulação local","WAIT_UNTIL_ALLOWED",137,"42",1,1,0,DateTime.Now.AddHours(1),"Aguarde a liberação da fonte. "+new string('X',400)));await Task.Delay(100);Snapshot("diagnostico-mensagem-extensa");main.Back();
     using(var scope=runtime.Services.CreateScope())
     {
      var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();
      for(int i=0;i<300;i++)db.Companies.Add(new(){OrganizationId=db.OrganizationId,LegalName=$"Empresa de volume sintético {i:D4}",Cnpj=$"VOLUME{i:D8}",State="SC"});
      await db.SaveChangesAsync();
     }
     await ClickReady(main,"Empresas");await Task.Delay(300);
     var all=(DataGrid)main.FindName("CompaniesGrid");Check(all.Items.Count==302,"complete company list retains volume");
     Check(Descendants(all).OfType<DataGridRow>().Count()<100,"company volume is virtualized");Snapshot("empresas-volume");
     ((TextBox)main.FindName("SearchBox")).Text="SEM RESULTADO SINTETICO";await ClickReady(main,"Pesquisar");await Task.Delay(200);Check(all.Items.Count==0,"empty search state");((TextBox)main.FindName("SearchBox")).Focus();Check(((TextBox)main.FindName("SearchBox")).IsKeyboardFocusWithin,"search receives keyboard focus");Check(((TextBox)main.FindName("SearchBox")).MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)),"tab traversal reaches next control");Snapshot("empresas-vazio");
     ((TextBox)main.FindName("SearchBox")).Clear();await ClickReady(main,"Pesquisar");await Task.Delay(200);
     // Render-scale coverage is explicit: this does not change the operating system DPI.
     foreach(double scale in new[]{1.0,1.25,1.5})
     {
      main.Width=1920;main.Height=1080;
      ((FrameworkElement)main.Content).LayoutTransform=new ScaleTransform(scale,scale);
      foreach(string section in new[]{"Consultas","Empresas","Documentos","Configurações"})
      {await Until(()=>Ready(main.ActiveView)&&((StackPanel)main.FindName("Navigation")).IsEnabled);await ClickReady(main,section);await Until(()=>Ready(main.ActiveView)&&((StackPanel)main.FindName("Navigation")).IsEnabled);await Task.Delay(150);Snapshot($"escala-layout-{scale:0.00}-{section.ToLowerInvariant()}");}
     }
     ((FrameworkElement)main.Content).LayoutTransform=Transform.Identity;
     log.Add("DPI: "+VisualTreeHelper.GetDpi(main).DpiScaleX+"; 125%/150% exercised with WPF LayoutTransform, not OS settings.");
    }
    void Check(bool valid,string description){if(!valid)throw new InvalidOperationException(description);log.Add("PASS "+description);}
   }
   catch(Exception ex){Snapshot("failure");log.Add("FAIL "+ex);}
   finally
   {
    Directory.CreateDirectory(Path.Combine(output,"diagnostic"));if(Directory.Exists(Path.Combine(data,"Logs")))foreach(var safeLog in Directory.GetFiles(Path.Combine(data,"Logs"),"sync-*.jsonl"))File.Copy(safeLog,Path.Combine(output,"diagnostic",Path.GetFileName(safeLog)),true);File.WriteAllLines(Path.Combine(output,"smoke.txt"),log);Console.WriteLine(string.Join(Environment.NewLine,log));main.Close();await runtime.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(data,true);Directory.Delete(exports,true);Dispatcher.CurrentDispatcher.InvokeShutdown();
   }
  }));
  Dispatcher.Run();return exit;
  void Snapshot(string name)
  {main.UpdateLayout();log.Add($"LAYOUT {name}: window={main.ActualWidth}x{main.ActualHeight}, content={((FrameworkElement)main.Content).ActualWidth}x{((FrameworkElement)main.Content).ActualHeight}");var root=(FrameworkElement)main.Content;double scale=root.LayoutTransform is ScaleTransform transform?transform.ScaleX:1;int width=(int)Math.Ceiling(root.ActualWidth*scale),height=(int)Math.Ceiling(root.ActualHeight*scale);var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name+".png"));encoder.Save(file);}
 }
 private static bool Ready(WorkspaceView? view)
 {
  if(view is null)return true;
  if(view is QueriesView queries&&!Ready(queries.ActiveView))return false;
  foreach(string name in new[]{"busy","saving"})if(view.GetType().GetField(name,System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)?.GetValue(view) is true)return false;
  return true;
 }
 private static async Task Until(Func<bool> condition)
 {for(int i=0;i<1000;i++){if(condition())return;await Task.Delay(50);}throw new TimeoutException("UI condition was not reached.");}
 private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
 {for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}}
 private static async Task ClickReady(MainWindow window,string label)
 {
  await Until(()=>
  {
   window.UpdateLayout();DependencyObject surface=window.Dialogs.IsOpen?window.Dialogs:window;
   return Descendants(surface).OfType<Button>().Any(x=>(x.Content?.ToString()==label||System.Windows.Automation.AutomationProperties.GetName(x)==label)&&x.IsEnabled);
  });
  Click(window,label);
 }
 private static void Click(Window window,string label)
 {window.UpdateLayout();DependencyObject surface=window is MainWindow shell&&shell.Dialogs.IsOpen?shell.Dialogs:window;var button=Descendants(surface).OfType<Button>().First(x=>x.Content?.ToString()==label||System.Windows.Automation.AutomationProperties.GetName(x)==label);if(!button.IsEnabled)throw new InvalidOperationException("Button disabled: "+label);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
}
