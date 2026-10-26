using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Cte;
using WgsDfe.Application.Nfe;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class CteWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private bool busy;private int page=1;private CancellationTokenSource? cancellation;
 public override bool CanRunInBackground=>true;
 public override bool IsWorking=>busy;
 public override bool HasOperationHistory=>liveRows.Count>0;
 public override string OperationStatus=>Message.Text;
 public override void CancelOperation()=>Cancel_Click(this,new RoutedEventArgs());
 private readonly System.Collections.ObjectModel.ObservableCollection<CteSyncRow> liveRows=new();
 private void UpdateRow(CteSyncRow row){var previous=liveRows.FirstOrDefault(x=>x.CompanyId==row.CompanyId&&x.Environment==row.Environment);if(previous is not null)liveRows[liveRows.IndexOf(previous)]=row;else liveRows.Add(row);}
 private readonly Dictionary<Guid,NfeProgress> periodCounts=new();
 private void ReportProgress(NfeProgress progress)
 {
  if(!busy)return;if(progress.CteRow is { } row){UpdateRow(row);periodCounts[row.CompanyId]=progress;}
  Message.Text=$"{progress.Company} · Lote {progress.Page} · {(progress.Code=="CTE_PAGE_COMMITTED"?"Lote gravado; verificando continuação…":CteMessages.Describe(progress.Code))}\nLotes: {liveRows.Sum(x=>x.Pages)} · Arquivados: {liveRows.Sum(x=>x.Archived)} · Preservados: {liveRows.Sum(x=>x.Retained)} · Recebidos arquivados nesta execução; total desconhecido. É possível navegar ou cancelar.";
  Message.Text+=$"\nChaves por empresa com emissão no período: {periodCounts.Values.Sum(x=>x.InPeriod)} · Fora: {periodCounts.Values.Sum(x=>x.OutsidePeriod)} · Eventos/sem emissão: {periodCounts.Values.Sum(x=>x.Undated)} · Novos: {periodCounts.Values.Sum(x=>x.NewXml)} · Existentes: {periodCounts.Values.Sum(x=>x.ExistingXml)}. Notas por período e exportação usam somente o intervalo informado.";
  if(progress.Code=="CTE_PAGE_COMMITTED")_ = Period.RefreshAsync();
  UpdateDiagnosticBanner();
 }
 public CteWindow(FoundationRuntime runtime)
 {
  InitializeComponent();ListSearch.Attach(ResultsGrid);ListSearch.Attach(RetainedGrid);PeriodFilterHost.Content=Period.DetachFilters();WindowLayout.Attach(this);this.runtime=runtime;Period.Configure(runtime,()=>new WgsDfe.Application.Archive.ArchiveQuery(Environment:FiscalEnvironment.Production,CompanyIds:selectedCompanies.ToArray(),Models:new[]{CteModel.IsChecked==true?DocumentModel.CTe:(DocumentModel?)null,OsModel.IsChecked==true?DocumentModel.CTeOS:(DocumentModel?)null}.Where(x=>x.HasValue).Select(x=>x!.Value).ToArray()));
  CompaniesGrid.SelectionChanged+=Companies_Changed;EnvironmentBox.SelectionChanged+=(_,_)=>Period.Invalidate();CteModel.Click+=(_,_)=>Period.Invalidate();OsModel.Click+=(_,_)=>Period.Invalidate();NetworkConsent.IsChecked=runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests;
  Loaded+=async(_,_)=>{if(CompaniesGrid.Items.Count>0)await Execute(Refresh);};
  FirstLoaded+=async(_,_)=>await Execute(Refresh);Closing+=(_,e)=>{if(busy){e.Cancel=true;Message.Text="Cancele e aguarde o ponto seguro de gravação antes de fechar.";}};Closed+=(_,_)=>runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=false;
 }
 private Task<T> Use<T>(Func<ICteSyncService,Task<T>> work)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await work(scope.ServiceProvider.GetRequiredService<ICteSyncService>());});
 private async Task Execute(Func<Task> work)
 {
  if(busy||preparing)return;busy=true;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=false;CompaniesGrid.IsEnabled=CompanySearch.IsEnabled=false;RunButton.IsEnabled=false;ReprocessButton.IsEnabled=false;NetworkConsent.IsEnabled=false;RetainedActions.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;
  try{await work();}catch(BackgroundTaskConflictException ex){Message.Text=ex.Message;}catch(NfeException e){Message.Text=$"{CteMessages.Describe(e.Code)} ({e.Code})";}catch(OperationCanceledException){Message.Text="Cancelado. Os recebimentos confirmados foram preservados.";}catch{Message.Text="Operação não concluída. Respostas recebidas ficam preservadas para retomada. Verifique a pasta de dados.";}
  finally{busy=false;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=true;CompaniesGrid.IsEnabled=CompanySearch.IsEnabled=true;RunButton.IsEnabled=true;ReprocessButton.IsEnabled=true;NetworkConsent.IsEnabled=true;RetainedActions.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;CancelButton.IsEnabled=false;cancellation?.Dispose();cancellation=null;}
 }
 private async Task Refresh()
 {
  var rows=await Use(s=>s.CompaniesAsync());companyRows=rows;
  selectedCompanies.IntersectWith(rows.Select(x=>x.Id));await ApplyCompanySearchAsync();await Retained();
  if(!HasOperationHistory)Message.Text="Selecione empresas e modelos. O cursor é compartilhado pelos modelos da distribuição.";
 }
 private async Task Retained(){var rows=await Use(s=>s.RetainedAsync(page));RetainedGrid.ItemsSource=rows;RetainedPageText.Text=$"Página {page} · {rows.Count} item(ns)";}
 private void All_Click(object sender,RoutedEventArgs e){if(!busy)ToggleCompanySelection();}
 private async void ViewRetained_Click(object sender,RoutedEventArgs e)
 {
  if(RetainedGrid.SelectedItem is not CteRetainedRow row){Message.Text="Selecione um item preservado.";return;}
  await Execute(async()=>
  {
   var xml=await Use(s=>s.ReadRetainedXmlAsync(row.Id));
   var panel=new System.Windows.Controls.DockPanel{Margin=new Thickness(14)};
   var topBar=new System.Windows.Controls.DockPanel{Margin=new Thickness(0,0,0,8)};
   var downloadBtn=new System.Windows.Controls.Button{Content="Baixar XML",Padding=new Thickness(12,5,12,5),Margin=new Thickness(10,0,0,0),VerticalAlignment=VerticalAlignment.Center};
   Ui.SetIcon(downloadBtn,"\uE896");
   System.Windows.Controls.DockPanel.SetDock(downloadBtn,System.Windows.Controls.Dock.Right);
   var heading=new System.Windows.Controls.TextBlock{Text=$"SHA-256 conferido: {xml.Hash}"+(xml.PreviewTruncated?" · Prévia limitada a 200.000 caracteres":""),TextWrapping=TextWrapping.Wrap,VerticalAlignment=VerticalAlignment.Center};
   downloadBtn.Click+=async(_,_)=>
   {
    if(string.IsNullOrWhiteSpace(xml.Text)){MessageBox.Show("O XML original não está disponível.","Baixar XML",MessageBoxButton.OK,MessageBoxImage.Warning);return;}
    var dlg=new Microsoft.Win32.SaveFileDialog{Title="Baixar XML preservado",Filter="Arquivo XML (*.xml)|*.xml",DefaultExt=".xml",FileName=$"{(string.IsNullOrWhiteSpace(row.AccessKey)?xml.Hash:row.AccessKey)}.xml"};
    if(dlg.ShowDialog(Window.GetWindow(this))==true){await System.IO.File.WriteAllTextAsync(dlg.FileName,xml.Text,new System.Text.UTF8Encoding(false));heading.Text=$"XML salvo com sucesso em: {dlg.FileName} · SHA-256: {xml.Hash}";}
   };
   topBar.Children.Add(downloadBtn);topBar.Children.Add(heading);
   System.Windows.Controls.DockPanel.SetDock(topBar,System.Windows.Controls.Dock.Top);panel.Children.Add(topBar);
   var tabs=new System.Windows.Controls.TabControl{Padding=new Thickness(4)};
   var danfe=new DanfeViewerControl();danfe.LoadXml(xml.Text);
   tabs.Items.Add(new System.Windows.Controls.TabItem{Header="Espelho Gráfico (DACTE / Evento)",Content=danfe});
   tabs.Items.Add(new System.Windows.Controls.TabItem{Header="XML Original Preservado",Content=new System.Windows.Controls.TextBox{Text=xml.Text,IsReadOnly=true,FontFamily=Typography.Mono,FontSize=12,HorizontalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto,VerticalScrollBarVisibility=System.Windows.Controls.ScrollBarVisibility.Auto}});
   panel.Children.Add(tabs);
   WorkspaceNavigator.Open(new WorkspaceView{Title="Visualizador de Documento / XML Preservado",PreferredDialogSize=new Size(1140,720),Content=panel});
  });
 }
 private async void Retained_Click(object sender,RoutedEventArgs e){page=1;await Execute(Retained);}
 private async void Previous_Click(object sender,RoutedEventArgs e){page=Math.Max(1,page-1);await Execute(Retained);}
 private async void Next_Click(object sender,RoutedEventArgs e){page++;await Execute(Retained);}
 private void Cancel_Click(object sender,RoutedEventArgs e){cancellation?.Cancel();CancelButton.IsEnabled=false;Message.Text="Cancelamento solicitado. Aguardando gravação segura…";}
 private async void Run_Click(object sender,RoutedEventArgs e)=>await Run(false);
 private async void Reprocess_Click(object sender,RoutedEventArgs e)=>await Run(true);
 private bool preparing;
 private async Task Run(bool offline)
 {
  if(busy||preparing)return;
  if(!Period.ValidatePeriod())return;
  var ids=selectedCompanies.ToArray();if(ids.Length==0){Message.Text="Selecione ao menos uma empresa.";return;}
  var models=(CteModel.IsChecked==true?EnabledDocuments.CTe:EnabledDocuments.None)|(OsModel.IsChecked==true?EnabledDocuments.CTeOS:EnabledDocuments.None);
  if(models==EnabledDocuments.None){Message.Text="Selecione CT-e e/ou CT-e OS.";return;}
  if(!int.TryParse(PagesBox.Text,out int pages)||pages is <0 or >100){Message.Text="Use 0 para continuação automática ou informe de 1 a 100 lotes por empresa.";return;}
  string? nsu=offline||string.IsNullOrWhiteSpace(NsuBox.Text)?null:NsuBox.Text;
  var request=new CteSyncRequest(ids,FiscalEnvironment.Production,models,nsu,pages,From:Period.SelectedPeriod.From is DateTime f?DateOnly.FromDateTime(f):null,To:Period.SelectedPeriod.To is DateTime t?DateOnly.FromDateTime(t):null);
  if(!offline)
  {
   preparing=true;
   try{int choice=await SyncPlanDialog.ChooseAsync(runtime,ids,new[]{CteSource.Code},request.Environment,ActorFilter.Either,PayloadMode.FullXml,Period.SelectedPeriod.From,Period.SelectedPeriod.To);if(choice!=2){if(choice==1)await Period.RefreshAsync();return;}}
   catch(Exception e)when(e is not OutOfMemoryException){Message.Text="Não foi possível preparar a consulta. Nenhuma requisição foi iniciada.";return;}
   finally{preparing=false;}
  }
  runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=!offline;cancellation=new();CancelButton.IsEnabled=true;
  periodCounts.Clear();liveRows.Clear();ResultsGrid.ItemsSource=liveRows;Message.Text="Aguardando a fila e a resposta da fonte; total de lotes desconhecido.";
  var progress=BackgroundTaskCoordinator.For(runtime).Observe(cancellation!,new Progress<NfeProgress>(ReportProgress));
  await Execute(async()=>{var result=await BackgroundTaskCoordinator.For(runtime).RunAsync(Title,ids.Select(id=>$"{id:N}:CTE:{request.Environment}").ToArray(),cancellation!,token=>Use(s=>offline?s.ReprocessAsync(request,progress,token):s.SynchronizeAsync(request,progress,token)));foreach(var row in result.Rows)UpdateRow(row);if(result.Cancelled)for(int i=0;i<liveRows.Count;i++)if(liveRows[i].Code=="CTE_PAGE_COMMITTED")liveRows[i]=liveRows[i] with{Code="NFE_CANCELLED"};await Period.RefreshAsync();ResultTabs.SelectedIndex=1;UpdateDiagnosticBanner();await Retained();Message.Text=result.Cancelled?"Cancelado. Recebimentos confirmados preservados; pendências serão retomadas.":offline?"Reprocessamento local concluído. Confira arquivados e preservados pendentes.":"Consulta concluída. Confira o resultado por empresa e os preservados pendentes. XMLs arquivados estão no Acervo de documentos.";});
 }
 private void ResultsGrid_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateDiagnosticBanner();
 private void UpdateDiagnosticBanner()
 {
  if(DiagnosticHeadline is null||DiagnosticDetail is null)return;
  var row=ResultsGrid.SelectedItem as CteSyncRow??liveRows.LastOrDefault();
  if(row is null)
  {
   DiagnosticHeadline.Text="DIAGNÓSTICO E ORIENTAÇÃO DA CONSULTA CT-e / CT-e OS";
   DiagnosticDetail.Text="Selecione o resultado de uma empresa na tabela acima (ou dê duplo clique) para ler o diagnóstico detalhado e o horário de liberação.";
   return;
  }
  string next=row.NextAllowedAtLocal.HasValue?$" · Próxima tentativa: {row.NextAllowedAtLocal:dd/MM/yyyy HH:mm:ss}":"";
  
  DiagnosticHeadline.Text=$"{row.Company} · {row.Archived} documento(s) arquivado(s) · {row.Retained} preservado(s){next}";
  DiagnosticDetail.Text=$"{row.Result} — Clique em Ver diagnóstico (ou dê duplo clique na linha) para abrir o painel completo e copiar para o suporte.";
 }
 private void Diagnostic_Click(object sender,RoutedEventArgs e)
 {
  if(busy||preparing)return;
  if(ResultsGrid.SelectedItem is not CteSyncRow row){Message.Text="Selecione o resultado de uma empresa na tabela para ver o diagnóstico.";return;}
  WorkspaceNavigator.Open(new QueryDiagnosticView(row.Company,"CT-e Nacional · "+row.EnvironmentLabel,row.Code,row.CStat,row.Nsu,row.Pages,row.Archived,row.Retained,row.NextAllowedAtLocal,row.Result));
 }

 private void UpdateSelectionButton(){if(SelectionText is not null)SelectionText.Text=$"{selectedCompanies.Count} selecionada(s) · {CompaniesGrid.Items.Count} no filtro";if(SelectAllButton is not null)SelectAllButton.Content=CompaniesGrid.Items.Count>0&&CompaniesGrid.SelectedItems.Count==CompaniesGrid.Items.Count?"Desmarcar todas":"Selecionar todas";}
 private void ToggleCompanySelection(){if(CompaniesGrid.Items.Count>0&&CompaniesGrid.SelectedItems.Count==CompaniesGrid.Items.Count)CompaniesGrid.UnselectAll();else CompaniesGrid.SelectAll();UpdateSelectionButton();}}
