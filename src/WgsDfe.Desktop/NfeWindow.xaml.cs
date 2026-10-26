using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Nfe;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class NfeWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private readonly string? fixedSource;private CancellationTokenSource? cancellation;private bool busy;
 public override bool CanRunInBackground=>true;
 public override bool IsWorking=>busy;
 public override bool HasOperationHistory=>startedAt!=default;
 public override string OperationStatus=>Message.Text+(startedAt!=default?"\n"+ElapsedStatus.Text:"");
 public override void CancelOperation()=>Cancel_Click(this,new RoutedEventArgs());
 private readonly System.Collections.ObjectModel.ObservableCollection<NfeSyncRow> liveRows=new();
 private bool refreshPending;
 private DateTimeOffset? lastResponse,completedAt;private DateTimeOffset startedAt;private int selectedCompanyCount;
 private readonly Dictionary<(Guid,string),(int New,int Existing)> xmlCounts=new();
 private readonly Dictionary<(Guid,string),(int Inside,int Outside,int Undated)> periodCounts=new();
 private void ReportProgress(NfeProgress progress)
 {
  if(!busy)return;
  if(progress.Row is { } row){var previous=liveRows.FirstOrDefault(x=>x.CompanyId==row.CompanyId&&x.Provider==row.Provider);if(previous is not null)liveRows[liveRows.IndexOf(previous)]=row;else liveRows.Add(row);}
  if(progress.Code=="NFE_PAGE_COMMITTED"&&progress.Row is { } saved){lastResponse=DateTimeOffset.Now;xmlCounts[(saved.CompanyId,saved.Provider)]=(progress.NewXml,progress.ExistingXml);}
  if(progress.Code=="NFE_PAGE_COMMITTED"&&progress.Row is { } counted)periodCounts[(counted.CompanyId,counted.Provider)]=(progress.InPeriod,progress.OutsidePeriod,progress.Undated);
  string phase=progress.Code=="NFE_PAGE_COMMITTED"?"Lote gravado; verificando continuação…":NfeMessages.Describe(progress.Code);
  Message.Text=$"{progress.Company} · Empresa {liveRows.Select(x=>x.CompanyId).Distinct().Count()}/{selectedCompanyCount} · Lote {progress.Page} · {phase}\nLotes: {liveRows.Sum(x=>x.Pages)} · XMLs processados: {liveRows.Sum(x=>x.Documents)} · Novos/atualizados: {xmlCounts.Values.Sum(x=>x.New)} · Existentes: {xmlCounts.Values.Sum(x=>x.Existing)} · Resumos: {liveRows.Sum(x=>x.Summaries)} · Total desconhecido; datas filtram o acervo, distribuição segue pelo NSU. Navegação e cancelamento disponíveis.";
  if(progress.Code=="NFE_PAGE_COMMITTED")_ = RefreshPartial();
  PeriodCounts.Visibility=Visibility.Visible;PeriodCounts.Text=$"Sincronização recebida: {liveRows.Sum(x=>x.Documents)} XMLs · Chaves por empresa/fonte com emissão no período: {periodCounts.Values.Sum(x=>x.Inside)} · Fora: {periodCounts.Values.Sum(x=>x.Outside)} · Eventos/sem emissão: {periodCounts.Values.Sum(x=>x.Undated)}. A lista abaixo contém apenas as emissões do filtro.";
  UpdateDiagnosticBanner();
 }
 private async Task RefreshPartial()
 {
  if(refreshPending)return;refreshPending=true;
  try{await Period.RefreshAsync();}finally{refreshPending=false;}
 }
 public NfeWindow(FoundationRuntime runtime,DocumentModel model=DocumentModel.NFe,bool combined=false)
 {
  InitializeComponent();ListSearch.Attach(ResultsGrid);PeriodFilterHost.Content=Period.DetachFilters();WindowLayout.Attach(this);this.runtime=runtime;Period.Configure(runtime,()=>new WgsDfe.Application.Archive.ArchiveQuery(Models:SelectedModels,Environment:FiscalEnvironment.Production,CompanyIds:selectedCompanies.ToArray()));fixedSource=model switch{DocumentModel.NFCe=>NfeSources.SantaCatarinaNfce,DocumentModel.NFCom=>NfeSources.SantaCatarinaNfcom,_=>null};
  var statusClock=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(500)};
  statusClock.Tick+=(_,_)=>{if(startedAt!=default)ElapsedStatus.Text=$"Tempo decorrido: {(completedAt??DateTimeOffset.Now)-startedAt:hh\\:mm\\:ss} · Última resposta válida: {lastResponse?.ToString("HH:mm:ss")??"aguardando"}";};statusClock.Start();Closed+=(_,_)=>statusClock.Stop();
  if(fixedSource is not null)
  {
   string name=model==DocumentModel.NFCom?"NFCom":"NFC-e",references=model==DocumentModel.NFCom?"três":"dois";
   TypeBox.Visibility=Visibility.Collapsed;Title="Consulta e download de "+name;Heading.Text=name+" · SEF/SC";
   SourceNotice.Text=$"Serviço em regime de testes pela SEF/SC. O certificado do contador é selecionado automaticamente. Somente empresas SC, ambiente de produção.";
   Source.SelectedIndex=0;Source.IsEnabled=false;Source.Visibility=Visibility.Collapsed;EnvironmentBox.IsEnabled=false;
   KeyBox.ToolTip=$"Chave {name}, modelo {(int)model}. Deixe vazio para continuar pelo NSU salvo. A consulta por chave exige uma empresa.";
   FooterNotice.Text=$"A fonte disponibiliza o mês atual e os {references} anteriores, com possível atraso de até um dia. Aguarde 12 horas após o sincronismo. O período selecionado filtra as notas do acervo após a consulta. Lançamento definitivo previsto pela SEF/SC para 01/01/2027.";
  }
  Source_Changed(Source,new System.Windows.Controls.SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent,Array.Empty<object>(),Array.Empty<object>()));
  EnvironmentBox.SelectionChanged+=(_,_)=>Period.Invalidate();
  if(fixedSource is null)
  {
   var preferences=UserPreferences.Load(runtime.DataDirectory);TypeBox.SelectedIndex=Math.Clamp(combined?2:0,0,2);Source.SelectedIndex=TypeBox.SelectedIndex>0?0:Math.Clamp(preferences.QuerySource,0,1);Source.IsEnabled=TypeBox.SelectedIndex==0;
  }
  Closing+=(_,_)=>{if(!busy&&fixedSource is null)try{UserPreferences.Update(runtime.DataDirectory,preferences=>preferences with{QueryType=TypeBox.SelectedIndex,QuerySource=Source.SelectedIndex,QueryCompanies=selectedCompanies.ToArray()});}catch(System.IO.IOException){}catch(UnauthorizedAccessException){}};
  Loaded+=async(_,_)=>{if(companyRows.Count>0&&!busy)await Execute(Refresh);};
  FirstLoaded+=async(_,_)=>await Execute(Refresh);Closing+=(_,e)=>{if(busy){e.Cancel=true;Message.Text="Cancele e aguarde a gravação do lote antes de fechar.";}};
  Closed+=(_,_)=>runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=false;
 }
 private async Task<T> Use<T>(Func<INfeSyncService,Task<T>> work)=>await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await work(scope.ServiceProvider.GetRequiredService<INfeSyncService>());});
 private async Task Execute(Func<Task> work)
 {
  if(busy)return;busy=true;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=false;CompanySearch.IsEnabled=false;CompaniesGrid.IsEnabled=false;RunButton.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;
  try{await work();}catch(BackgroundTaskConflictException ex){Message.Text=ex.Message;}catch(NfeException e){Message.Text=$"{e.Message} ({e.Code})";}catch(AccessKeyException e){Message.Text=e.Message;}catch(OperationCanceledException){Message.Text="Operação cancelada. Os lotes confirmados foram preservados.";}catch{Message.Text="Falha na consulta ou na gravação. Respostas já recebidas ficam pendentes para retomada. Verifique a pasta de dados.";}
  finally{busy=false;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=true;CompanySearch.IsEnabled=true;CompaniesGrid.IsEnabled=true;RunButton.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;CancelButton.IsEnabled=false;if(cancellation is not null)completedAt=DateTimeOffset.Now;cancellation?.Dispose();cancellation=null;}
 }
 private IReadOnlyList<NfeCompany> companyRows=Array.Empty<NfeCompany>();
 private readonly HashSet<Guid> selectedCompanies=new();private bool replacingCompanies;private bool showSelectedOnly;
 private CancellationTokenSource? companySearchCancellation;
 private async void CompanySearch_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e)=>await ApplyCompanySearchAsync();
 private async Task ApplyCompanySearchAsync(bool debounce=true)
 {
  if(CompaniesGrid is null)return;companySearchCancellation?.Cancel();companySearchCancellation?.Dispose();companySearchCancellation=new();var token=companySearchCancellation.Token;var term=CompanySearch.Text.Trim();var all=showSelectedOnly?companyRows.Where(x=>selectedCompanies.Contains(x.Id)).ToArray():companyRows;
  try
  {
   if(debounce&&term.Length>0)await Task.Delay(180,token);
   var rows=term.Length==0?all.ToArray():await Task.Run(()=>all.Select(x=>{token.ThrowIfCancellationRequested();return(Row:x,Rank:SearchMatcher.Rank(term,new[]{x.Name,x.TradeName},new[]{x.Cnpj}));}).Where(x=>x.Rank.HasValue).OrderBy(x=>x.Rank).ThenBy(x=>x.Row.Name).Select(x=>x.Row).ToArray(),token);
   if(token.IsCancellationRequested)return;replacingCompanies=true;
   try{CompaniesGrid.ItemsSource=rows;foreach(var row in CompaniesGrid.Items.Cast<NfeCompany>().Where(x=>selectedCompanies.Contains(x.Id)))CompaniesGrid.SelectedItems.Add(row);}
   finally{replacingCompanies=false;}UpdateSelectionSummary();
  }
  catch(OperationCanceledException){ }
 }
 private async Task Refresh()
 {
  var selected=selectedCompanies.ToHashSet();
  if(companyRows.Count==0)selected=(UserPreferences.Load(runtime.DataDirectory).QueryCompanies??Array.Empty<Guid>()).ToHashSet();
  var source=CurrentSource;var rows=await Use(s=>s.CompaniesAsync(source:source));
  if(TypeBox.SelectedIndex==2&&fixedSource is null)rows=rows.Concat(await Use(s=>s.CompaniesAsync(source:NfeSources.SantaCatarinaNfce))).DistinctBy(x=>x.Id).OrderBy(x=>x.Name).ToArray();
  if(rows.SequenceEqual(companyRows))return;
  companyRows=rows;selectedCompanies.Clear();selectedCompanies.UnionWith(selected.Intersect(rows.Select(x=>x.Id)));await ApplyCompanySearchAsync();
  Message.Text="Selecione empresas e período; clique em Consultar e baixar XMLs.";
 }
 private void UpdateSelectionSummary(){UpdateSelectionButton();if(SelectionText is not null)SelectionText.Text=$"{selectedCompanies.Count} selecionada(s) · {CompaniesGrid.Items.Count} empresa(s) no filtro"+(selectedCompanies.Count>CompaniesGrid.SelectedItems.Count?" · Inclui seleção fora do filtro":"");}
 private void Companies_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(replacingCompanies)return;foreach(var row in e.RemovedItems.OfType<NfeCompany>())selectedCompanies.Remove(row.Id);foreach(var row in e.AddedItems.OfType<NfeCompany>())selectedCompanies.Add(row.Id);Period?.Invalidate();UpdateSelectionSummary();if(showSelectedOnly&&e.RemovedItems.Count>0)Dispatcher.BeginInvoke(new Action(async()=>await ApplyCompanySearchAsync(false)));}
 private void All_Click(object sender,RoutedEventArgs e){if(!busy)ToggleCompanySelection();}
 private void Cancel_Click(object sender,RoutedEventArgs e){cancellation?.Cancel();CancelButton.IsEnabled=false;Message.Text="Cancelamento solicitado. Aguardando ponto seguro de gravação…";}
 private DocumentModel[] SelectedModels=>fixedSource is not null?new[]{NfeSources.Model(fixedSource)}:TypeBox.SelectedIndex switch{1=>new[]{DocumentModel.NFCe},2=>new[]{DocumentModel.NFe,DocumentModel.NFCe},_=>new[]{DocumentModel.NFe}};
 private string CurrentSource=>fixedSource??(TypeBox.SelectedIndex==1?NfeSources.SantaCatarinaNfce:Source.SelectedIndex==0?NfeSources.SantaCatarina:NfeSources.National);
 private async void Type_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
 {
  if(Source is null||!IsLoaded)return;
  if(TypeBox.SelectedIndex>0){Source.SelectedIndex=0;Source.IsEnabled=false;SourceNotice.Text="NFC-e usa o serviço SC já implementado: empresas SC, produção e certificado do contador. A disponibilidade é limitada pela fonte; o período apenas filtra os documentos locais.";}
  else{Source.IsEnabled=true;SourceNotice.Text="O certificado compatível é selecionado automaticamente. Nacional usa e-CNPJ da empresa; SC usa o certificado do contador.";}
  Period.Invalidate();await Execute(Refresh);
 }
 private async void Source_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e)
 {
  if(ActorBox is null)return;Period?.Invalidate();bool sc=Source.SelectedIndex==0;ActorBox.IsEnabled=sc;ModeBox.IsEnabled=sc;EnvironmentBox.IsEnabled=!sc;
  if(!sc){ActorBox.SelectedIndex=0;ModeBox.SelectedIndex=0;}else EnvironmentBox.SelectedIndex=0;
  if(IsLoaded)await Execute(Refresh);
 }
 private async void Run_Click(object sender,RoutedEventArgs e)=>await RunQueryAsync();
 private bool preparing;
 private async Task RunQueryAsync()
 {
  if(busy||preparing)return;
  if(!Period.ValidatePeriod()){Message.Text="Confira as datas inicial e final.";return;}
  var companies=selectedCompanies.ToArray();
  if(companies.Length==0){Message.Text="Selecione ao menos uma empresa.";return;}
  if(!int.TryParse(PagesBox.Text,out int pages)||pages is <0 or >100){Message.Text="Use 0 para continuação automática ou informe de 1 a 100 lotes por empresa.";return;}
  string? key=string.IsNullOrWhiteSpace(KeyBox.Text)?null:KeyBox.Text;
  if(TypeBox.SelectedIndex==2&&key is not null){Message.Text="Para consultar por chave, selecione apenas o tipo correspondente.";return;}
  var request=new NfeSyncRequest(companies,CurrentSource,FiscalEnvironment.Production,ActorBox.SelectedIndex switch{1=>ActorFilter.Issuer,2=>ActorFilter.Recipient,3=>ActorFilter.Both,_=>ActorFilter.Either},ModeBox.SelectedIndex==0?PayloadMode.FullXml:PayloadMode.KeysOnly,key,pages);
  request=request with{From=Period.SelectedPeriod.From is DateTime f?DateOnly.FromDateTime(f):null,To=Period.SelectedPeriod.To is DateTime t?DateOnly.FromDateTime(t):null};
  preparing=true;RunButton.IsEnabled=false;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=CompanySearch.IsEnabled=CompaniesGrid.IsEnabled=false;
  try
  {
   var sources=TypeBox.SelectedIndex==2&&fixedSource is null?new[]{request.ProviderCode,NfeSources.SantaCatarinaNfce}:new[]{request.ProviderCode};
   int choice=await SyncPlanDialog.ChooseAsync(runtime,companies,sources,request.Environment,request.Actor,request.PayloadMode,Period.SelectedPeriod.From,Period.SelectedPeriod.To);
   if(choice!=2){if(choice==1)await Period.RefreshAsync();return;}
  }
  catch(Exception e)when(e is not OutOfMemoryException){Message.Text="Não foi possível preparar o escopo da consulta. Nenhuma requisição foi iniciada. Tente novamente.";return;}
  finally{preparing=false;RunButton.IsEnabled=true;PeriodFilterHost.IsEnabled=QueryOptions.IsEnabled=CompanySearch.IsEnabled=CompaniesGrid.IsEnabled=true;}
  runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=true;
  cancellation=new();CancelButton.IsEnabled=true;
  liveRows.Clear();xmlCounts.Clear();periodCounts.Clear();ResultTabs.SelectedIndex=1;PeriodCounts.Text="Aguardando resposta da fonte. Recebidos para sincronização e emissões do período serão contados separadamente.";startedAt=DateTimeOffset.Now;lastResponse=null;completedAt=null;selectedCompanyCount=companies.Length;ResultsGrid.ItemsSource=liveRows;Message.Text="Sincronização de pendências confirmada. Aguardando a fila e a resposta da fonte; o total de lotes é desconhecido.";
  var progress=BackgroundTaskCoordinator.For(runtime).Observe(cancellation!,new Progress<NfeProgress>(ReportProgress));
  bool combined=TypeBox.SelectedIndex==2&&fixedSource is null;
  await Execute(async()=>
  {
   var resources=request.Companies.SelectMany(id=>new[]{request.ProviderCode}.Concat(combined?new[]{NfeSources.SantaCatarinaNfce}:Array.Empty<string>()).Select(source=>$"{id:N}:{source}:{request.Environment}")).ToArray();
   var result=await BackgroundTaskCoordinator.For(runtime).RunAsync(Title,resources,cancellation!,async token=>
   {
    var first=await Use(s=>s.SynchronizeAsync(request,progress,token));
    if(!combined||first.Cancelled)return first;
    var second=await Use(s=>s.SynchronizeAsync(request with{ProviderCode=NfeSources.SantaCatarinaNfce},progress,token));
    return first with{Rows=first.Rows.Concat(second.Rows).ToArray(),Cancelled=second.Cancelled};
   });
   foreach(var row in result.Rows){var previous=liveRows.FirstOrDefault(x=>x.CompanyId==row.CompanyId&&x.Provider==row.Provider);if(previous is not null)liveRows[liveRows.IndexOf(previous)]=row;else liveRows.Add(row);}
   if(result.Cancelled)for(int index=0;index<liveRows.Count;index++)if(liveRows[index].Code is "NFE_REQUESTING" or "NFE_PROCESSING" or "NFE_PAGE_COMMITTED")liveRows[index]=liveRows[index] with{Code="NFE_CANCELLED"};
   await Period.RefreshAsync();
   var problem=result.Rows.FirstOrDefault(x=>x.Code is not ("NFE_SYNCHRONIZED" or "NFE_PAGE_COMMITTED" or "LOCAL_XML_PRESENT"));
   ResultTabs.SelectedIndex=result.Cancelled||problem is not null?1:0;
   UpdateDiagnosticBanner();
   Message.Text=result.Cancelled?"Consulta cancelada. Os documentos já recebidos foram preservados.":problem is not null?$"{problem.Company}: {problem.Result} ({problem.Code}). Confira a aba Resultado da consulta. As notas já baixadas continuam em Notas por período.":"Busca concluída. Notas por período e Exportar XMLs respeitam as datas selecionadas; outros meses recebidos na distribuição ficam fora desse filtro.";
  });
 }
 private void ResultsGrid_SelectionChanged(object sender,System.Windows.Controls.SelectionChangedEventArgs e)=>UpdateDiagnosticBanner();
 private void UpdateDiagnosticBanner()
 {
  if(DiagnosticHeadline is null||DiagnosticDetail is null)return;
  var row=ResultsGrid.SelectedItem as NfeSyncRow??liveRows.LastOrDefault();
  if(row is null)
  {
   DiagnosticHeadline.Text="DIAGNÓSTICO E ORIENTAÇÃO DA CONSULTA";
   DiagnosticDetail.Text="Selecione o resultado de uma empresa na tabela acima (ou dê duplo clique) para ler o diagnóstico detalhado e o horário de liberação.";
   return;
  }
  string next=row.NextAllowedAtLocal.HasValue?$" · Próxima tentativa: {row.NextAllowedAtLocal:dd/MM/yyyy HH:mm:ss}":"";
  
  DiagnosticHeadline.Text=$"{row.Company} · {row.Documents} XML(s) recebido(s) · {row.Summaries} resumo(s)/pendência(s){next}";
  DiagnosticDetail.Text=$"{row.Result} — Clique em Ver diagnóstico (ou dê duplo clique na linha) para abrir o painel completo e copiar para o suporte.";
 }
 private void Diagnostic_Click(object sender,RoutedEventArgs e)
 {
  if(busy)return;
  if(ResultsGrid.SelectedItem is not NfeSyncRow row){Message.Text="Selecione o resultado de uma empresa na tabela para ver o diagnóstico.";return;}
  WorkspaceNavigator.Open(new QueryDiagnosticView(row.Company,row.Provider,row.Code,row.CStat,row.Nsu,row.Pages,row.Documents,row.Summaries,row.NextAllowedAtLocal,row.Result));
 }

 private void UpdateSelectionButton(){if(SelectAllButton is not null)SelectAllButton.Content=CompaniesGrid.Items.Count>0&&CompaniesGrid.SelectedItems.Count==CompaniesGrid.Items.Count?"Desmarcar todas":"Selecionar todas";}
 private void ToggleCompanySelection(){if(CompaniesGrid.Items.Count>0&&CompaniesGrid.SelectedItems.Count==CompaniesGrid.Items.Count)CompaniesGrid.UnselectAll();else CompaniesGrid.SelectAll();UpdateSelectionButton();} private async void ShowSelected_Click(object sender,RoutedEventArgs e)
 {
  if(busy)return;showSelectedOnly=!showSelectedOnly;
  ShowSelectedButton.Content=showSelectedOnly?"Mostrar todos":"Mostrar selecionados";
  await ApplyCompanySearchAsync(false);
 }}
