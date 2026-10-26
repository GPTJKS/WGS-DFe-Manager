using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Providers;
using WgsDfe.Domain;
using WgsDfe.Desktop;
using WgsDfe.Infrastructure;

internal sealed class DistributionUiProbe
{
 public int Calls;public int HoldAt=3;public bool Repeat=false;public int Batches=5;
 public TaskCompletionSource Release {get;set;}=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public Guid[] RequestedCompanies {get;set;}=[];
}
internal sealed class ControlledDistribution(string source,DistributionUiProbe probe):INfeDistributionProvider
{
 public ProviderCapabilities Capabilities {get;}=new(source,new HashSet<DocumentModel>{NfeSources.Model(source)},CertificateRequirement.AccountantWithLink,true,true,false,false,true);
 public bool CanHandle(DocumentRequest request)=>true;
 public async Task<byte[]> ReceiveAsync(DocumentRequest request,CancellationToken token)
 {
  probe.RequestedCompanies=probe.RequestedCompanies.Append(request.CompanyId).Distinct().ToArray();int batch=Interlocked.Increment(ref probe.Calls);
  if(batch>=probe.HoldAt)await probe.Release.Task.WaitAsync(token);
  return Encoding.UTF8.GetBytes((long.Parse(request.LastNsu)+1).ToString(System.Globalization.CultureInfo.InvariantCulture));
 }
 public NfePage Decode(ReadOnlyMemory<byte> response,DocumentRequest request)
 {
  long batch=long.Parse(Encoding.UTF8.GetString(response.Span));
  var document=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"fixture.xml"));XNamespace ns=document.Root!.Name.Namespace;var info=document.Root.Element(ns+"infNFe")!;string old=info.Attribute("Id")!.Value[3..];
  string body=old[..2]+"2610"+old.Substring(6,14)+((int)request.Model).ToString("D2")+old.Substring(22,21);int sum=0;for(int i=0;i<43;i++)sum+=(body[i]-48)*(2+(42-i)%8);int remainder=sum%11;string key=body+(remainder is 0 or 1?0:11-remainder).ToString();
  info.SetAttributeValue("Id","NFe"+key);info.Element(ns+"ide")!.Element(ns+"mod")!.Value=((int)request.Model).ToString();info.Element(ns+"ide")!.Element(ns+"cDV")!.Value=key[^1].ToString();info.Element(ns+"ide")!.Element(ns+"tpAmb")!.Value="1";info.Element(ns+"ide")!.Element(ns+"dhEmi")!.Value="2026-10-01T10:00:00-03:00";
  info.Element(ns+"emit")!.AddAfterSelf(new XElement(ns+"dest",new XElement(ns+"CNPJ",request.CompanyTaxId),new XElement(ns+"xNome","Destinatário sintético")));
  string next=probe.Repeat?request.LastNsu:batch.ToString();return new(118,next,batch<probe.Batches,TimeSpan.Zero,[new(key,next,Encoding.UTF8.GetBytes(document.ToString()),"synthetic")]);
 }
 public Task<ProviderResult> GetDocumentsAsync(DocumentRequest request,CancellationToken token)=>throw new NotSupportedException();
}

internal static class BackgroundUiScenario
{
 public static async Task SafeExit(MainWindow main,FoundationRuntime runtime,Action<string> snapshot,Action<bool,string> check)
 {
  main.OpenView(new ExportWindow(runtime));await Wait(()=>main.ActiveView is ExportWindow view&&!view.IsWorking);var export=(ExportWindow)main.ActiveView!;
  var gate=runtime.Services.GetRequiredService<WgsDfe.Infrastructure.Export.ExportGate>();var semaphore=(SemaphoreSlim)gate.GetType().GetProperty("Semaphore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(gate)!;await semaphore.WaitAsync();
  bool closed=false;main.Closed+=(_,_)=>closed=true;
  try
  {
   ((TextBox)export.FindName("DestinationBox")).Text=runtime.DataDirectory+"-exports";await Click(main,"Exportar para o destino");await Wait(()=>export.IsWorking);main.Close();await Wait(()=>main.ActiveView?.Title=="Há tarefas em andamento");snapshot("encerramento-seguro-com-exportacao");
   await Click(main,"Cancelar tarefas e fechar");await Wait(()=>closed);check(!export.IsWorking,"cancel and close waits for foreground export safe completion");check(BackgroundTaskCoordinator.For(runtime).Operations.All(x=>!x.Running),"application exits with no running coordinated operation");
  }
  finally{semaphore.Release();}
 }
 public static async Task Run(MainWindow main,FoundationRuntime runtime,Action<string> snapshot,Action<bool,string> check)
 {
  main.Width=1920;main.Height=1080;await Task.Delay(100);await Click(main,"Consultas");var queries=(QueriesView)main.ActiveView!;
  var selector=Walk(queries).OfType<ComboBox>().Single(x=>x.Items.Count==5&&x.Items[0]?.ToString()=="NF-e");
  var nfe=(NfeWindow)queries.ActiveView!;var period=(PeriodDocumentsPanel)nfe.FindName("Period");
  period.SelectedPeriod=(new DateTime(2026,9,1),new DateTime(2026,9,30));await queries.PeriodSaved;
  selector.SelectedIndex=1;main.UpdateLayout();await Wait(()=>queries.ActiveView is NfeWindow view&&((DataGrid)view.FindName("CompaniesGrid")).Items.Count>0&&!view.IsWorking);
  nfe=(NfeWindow)queries.ActiveView!;period=(PeriodDocumentsPanel)nfe.FindName("Period");
  check(period.SelectedPeriod==(new DateTime(2026,9,1),new DateTime(2026,9,30)),"shared September period survives NF-e to NFC-e");
  period.SelectedPeriod=(new DateTime(2026,10,1),new DateTime(2026,10,1));await queries.PeriodSaved;
  var preferences=UserPreferences.Load(runtime.DataDirectory);check(preferences.HasQueryPeriod&&preferences.QueryFrom==new DateTime(2026,10,1)&&preferences.QueryTo==preferences.QueryFrom,"shared valid period persisted for restart");
  var grid=(DataGrid)nfe.FindName("CompaniesGrid");grid.UnselectAll();main.UpdateLayout();var row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);var box=Walk(row).OfType<CheckBox>().First();
  Mouse(box);await Task.Yield();await Task.Delay(120);main.UpdateLayout();row=(DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);box=Walk(row).OfType<CheckBox>().First();check(row.IsSelected&&box.IsChecked==true&&grid.SelectedItems.Count==1,"checkbox mouse route selects once and remains selected: row="+row.IsSelected+", box="+box.IsChecked+", count="+grid.SelectedItems.Count+", attached="+ReferenceEquals(grid.ItemContainerGenerator.ContainerFromIndex(0),row));snapshot("checkbox-marcado-estavel");
  Mouse(box);await Task.Yield();check(!row.IsSelected&&box.IsChecked==false&&grid.SelectedItems.Count==0,"checkbox mouse route deselects once");Mouse(box);
  var toggle=(System.Windows.Automation.Provider.IToggleProvider)new System.Windows.Automation.Peers.CheckBoxAutomationPeer(box).GetPattern(System.Windows.Automation.Peers.PatternInterface.Toggle);toggle.Toggle();check(!row.IsSelected,"accessibility Toggle deselects actual row");toggle.Toggle();check(row.IsSelected,"accessibility Toggle selects actual row");
  ((TextBox)nfe.FindName("CompanySearch")).Text="SEM CORRESPONDENCIA";((TextBox)nfe.FindName("CompanySearch")).Clear();main.UpdateLayout();check(grid.SelectedItems.Count==1,"search filter preserves company selection by identity");
  var chosen=((NfeCompany)grid.SelectedItem).Id;var probe=runtime.Services.GetRequiredService<DistributionUiProbe>();probe.Calls=0;probe.RequestedCompanies=[];
  ((DataGrid)nfe.FindName("ResultsGrid")).ItemsSource=new[]{new NfeSyncRow(chosen,"Sintético",NfeSources.SantaCatarinaNfce,"ACCOUNTANT_CERTIFICATE_REQUIRED",null,0,0,0,"0",null)};
  await Click(main,"Consultar e baixar XMLs");await Wait(()=>main.ActiveView?.Title=="Conferir escopo da consulta");check(probe.Calls==0,"scope plan precedes all provider requests");snapshot("consulta-plano-nsu-periodo");await Click(main,"Sincronizar pendências e filtrar período");await Wait(()=>nfe.IsWorking);check(!((DataGrid)nfe.FindName("ResultsGrid")).Items.OfType<NfeSyncRow>().Any(x=>x.Code=="ACCOUNTANT_CERTIFICATE_REQUIRED"),"new operation removes stale certificate diagnostic");await Wait(()=>probe.Calls>=3&&((DataGrid)nfe.FindName("ResultsGrid")).Items.Count>0);
  check(((Button)nfe.FindName("CancelButton")).IsEnabled,"cancel enabled through complete ancestor chain during query");
  check(grid.SelectedItems.Count==1&&((NfeCompany)grid.SelectedItem).Id==chosen,"request selection matches visible checkbox");check(probe.RequestedCompanies.SequenceEqual(new[]{chosen}),"service receives exactly selected company");
  check(BackgroundTaskCoordinator.For(runtime).Operations.Any(x=>x.Running&&x.Work is not null),"coordinator owns running task independently of visible page");
  var active=BackgroundTaskCoordinator.For(runtime).Operations.Single(x=>x.Running);check(active.LatestProgress is NfeProgress,"coordinator retains progress independently of visual page");
  using(var conflicting=new CancellationTokenSource())
  {bool rejected=false;try{await BackgroundTaskCoordinator.For(runtime).RunAsync("Teste de conflito",active.Resources,conflicting,_=>Task.FromResult(true));}catch(InvalidOperationException){rejected=true;}check(rejected,"coordinator rejects overlapping company and source without blocking navigation");}
  selector.SelectedIndex=0;await Wait(()=>queries.ActiveView is NfeWindow current&&!current.IsWorking);selector.SelectedIndex=1;check(ReferenceEquals(queries.ActiveView,nfe)&&nfe.IsWorking,"document selector switches without stopping running NFC-e");
  foreach(string section in new[]{"Empresas","Documentos","Certificados","Configurações","Ajuda","Sobre"}){await Click(main,section);await Wait(()=>main.ActiveView is not QueriesView);check(nfe.IsWorking,"query continues while visiting "+section);}
  await Click(main,"Consultas");check(ReferenceEquals(nfe,((QueriesView)main.ActiveView!).ActiveView)&&period.SelectedPeriod==(new DateTime(2026,10,1),new DateTime(2026,10,1)),"return restores same running view and one-day period");
  check(((DataGrid)nfe.FindName("ResultsGrid")).Items.Count>0&&nfe.OperationStatus.Contains("XMLs processados"),"partial counters and rows survive background navigation");snapshot("nfce-em-andamento-retorno");var diagnosticGrid=(DataGrid)nfe.FindName("ResultsGrid");var summaryHeader=Walk(diagnosticGrid).OfType<System.Windows.Controls.Primitives.DataGridColumnHeader>().Single(x=>x.Content?.ToString()=="Chaves/resumos");check(summaryHeader.TransformToAncestor(diagnosticGrid).TransformBounds(new Rect(summaryHeader.RenderSize)).Right<=diagnosticGrid.ActualWidth,"complete summary header fits the normal 1920 viewport");
  main.Close();await Wait(()=>main.Dialogs.IsOpen);snapshot("fechar-com-tarefa-ativa");await Click(main,"Continuar aberto");await Wait(()=>!main.Dialogs.IsOpen);check(nfe.IsWorking,"close decision keeps task and app alive");
  await Click(main,"Cancelar");await Wait(()=>!nfe.IsWorking);check(nfe.OperationStatus.Contains("cancelada",StringComparison.OrdinalIgnoreCase),"UI cancellation reaches safe final state during provider wait");snapshot("nfce-cancelada-preservada");
  check(((DataGrid)nfe.FindName("ResultsGrid")).Items.Count>0,"cancel retains committed partial rows");check(!((DataGrid)nfe.FindName("ResultsGrid")).Items.OfType<NfeSyncRow>().Any(x=>x.Code is "NFE_PROCESSING" or "NFE_REQUESTING"),"cancelled diagnostics never claim processing");
  await period.RefreshAsync();var notes=(DataGrid)period.FindName("Documents");check(notes.Items.Count==1&&notes.Items.OfType<ArchiveDocumentRow>().All(x=>x.IssueDate=="2026-10-01"),"one-day UI contains only committed documents from that day");
  int beforeLocal=probe.Calls;period.SelectedPeriod=(new DateTime(2026,10,2),new DateTime(2026,10,2));await period.RefreshAsync();check(notes.Items.Count==0,"out-of-period documents remain outside the displayed result");
  await Click(main,"Consultar e baixar XMLs");await Wait(()=>main.ActiveView?.Title=="Conferir escopo da consulta");await Click(main,"Somente filtrar acervo");await Wait(()=>!main.Dialogs.IsOpen);await period.RefreshAsync();check(probe.Calls==beforeLocal&&notes.Items.Count==0,"local plan choice performs no source request");snapshot("consulta-somente-acervo-sem-resultado");
  period.SelectedPeriod=(new DateTime(2026,10,1),new DateTime(2026,10,1));await period.RefreshAsync();
  await queries.PeriodSaved;var reopened=new QueriesView(runtime);main.OpenView(reopened);await Wait(()=>reopened.ActiveView?.FindName("Period") is PeriodDocumentsPanel p&&p.SelectedPeriod==(new DateTime(2026,10,1),new DateTime(2026,10,1)));check(true,"new query workspace reloads persisted period");await Wait(()=>!reopened.IsWorking);main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  period.SelectedPeriod=(new DateTime(2026,10,10),new DateTime(2026,10,1));await queries.PeriodSaved;check(UserPreferences.Load(runtime.DataDirectory).QueryFrom==new DateTime(2026,10,1),"invalid period does not overwrite last valid preference");period.SelectedPeriod=(new DateTime(2026,10,1),new DateTime(2026,10,1));
  main.OpenView(new ExportWindow(runtime));await Wait(()=>main.ActiveView is ExportWindow export&&!export.IsWorking);var export=(ExportWindow)main.ActiveView!;
  var gate=runtime.Services.GetRequiredService<WgsDfe.Infrastructure.Export.ExportGate>();var semaphore=(SemaphoreSlim)gate.GetType().GetProperty("Semaphore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(gate)!;await semaphore.WaitAsync();
  try
  {
   ((TextBox)export.FindName("DestinationBox")).Text=runtime.DataDirectory+"-exports";await Click(main,"Exportar para o destino");await Wait(()=>export.IsWorking);
   main.Close();await Wait(()=>main.ActiveView?.Title=="Há tarefas em andamento");check(export.IsWorking,"application close offers decision even over foreground export modal");await Click(main,"Continuar aberto");await Wait(()=>ReferenceEquals(main.ActiveView,export));
   await Click(main,"Continuar em segundo plano");check(!main.Dialogs.IsOpen,"export detaches modal without disposal");
   await Click(main,"Configurações");check(export.IsWorking,"export continues while navigating settings");
   var tasks=Walk(main).OfType<Button>().Single(x=>x.Name=="BackgroundTasksButton");tasks.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>main.Dialogs.IsOpen);snapshot("tarefas-segundo-plano");
   var resume=Walk(main.Dialogs).OfType<Button>().Last(x=>x.Content?.ToString()=="Retomar tarefa");resume.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Wait(()=>ReferenceEquals(main.ActiveView,export));check(export.IsWorking,"same export resumes with operation state");await Click(main,"Cancelar");await Wait(()=>!export.IsWorking);main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  }
  finally{semaphore.Release();}
  Motion.Disabled=false;await Click(main,"Empresas");var workspace=(FrameworkElement)main.FindName("CompanyViewport");check(!Motion.Allowed||workspace.RenderTransform.HasAnimatedProperties,"module transition animates real transform");await Task.Delay(230);Motion.Disabled=true;
  await Click(main,"Consultas");selector.SelectedIndex=0;runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=false;
 }
 private static void Mouse(CheckBox box){var e=new MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left){RoutedEvent=UIElement.PreviewMouseLeftButtonDownEvent};box.RaiseEvent(e);if(!e.Handled)throw new InvalidOperationException("Checkbox selection was not handled.");}
 private static async Task Click(MainWindow main,string label){await Wait(()=>Walk(main.Dialogs.IsOpen?main.Dialogs:main).OfType<Button>().Any(x=>(x.Content?.ToString()==label||System.Windows.Automation.AutomationProperties.GetName(x)==label)&&x.IsEnabled));Walk(main.Dialogs.IsOpen?main.Dialogs:main).OfType<Button>().First(x=>x.Content?.ToString()==label||System.Windows.Automation.AutomationProperties.GetName(x)==label).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
 private static async Task Wait(Func<bool> condition){for(int i=0;i<1000;i++){if(condition())return;await Task.Delay(30);}throw new TimeoutException("Background UI scenario.");}
 private static IEnumerable<DependencyObject> Walk(DependencyObject parent){for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var child=VisualTreeHelper.GetChild(parent,i);yield return child;foreach(var nested in Walk(child))yield return nested;}}
}
