using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class PeriodDocumentsPanel:UserControl
{
 private FoundationRuntime? runtime;private Func<ArchiveQuery>? scope;private int page=1,last=1;private bool busy;private ArchiveQuery? activeQuery;
 private readonly SemaphoreSlim refreshGate=new(1,1);
 private bool settingPeriod;
 public PeriodDocumentsPanel(){InitializeComponent();FromDate.SelectedDate=new DateTime(DateTime.Today.Year,DateTime.Today.Month,1);ToDate.SelectedDate=DateTime.Today;Previous.IsEnabled=Next.IsEnabled=false;FromDate.SelectedDateChanged+=(_,_)=>DateChanged();ToDate.SelectedDateChanged+=(_,_)=>DateChanged();UpdateChipSelection();}
 private void DateChanged(){UpdateChipSelection();Invalidate();if(!settingPeriod)PeriodChanged?.Invoke(this,EventArgs.Empty);}
 private void UpdateChipSelection()
 {
  if(ChipToday is null||ChipWeek is null||ChipMonth is null||ChipAll is null)return;
  var today=DateTime.Today;var from=FromDate.SelectedDate?.Date;var to=ToDate.SelectedDate?.Date;
  bool isAll=from is null&&to is null;
  bool isToday=from==today&&to==today;
  bool isWeek=from==today.AddDays(-6)&&to==today;
  bool isMonth=from==new DateTime(today.Year,today.Month,1)&&to==today;
  ChipToday.Tag=isToday?"Active":null;
  ChipWeek.Tag=isWeek?"Active":null;
  ChipMonth.Tag=isMonth?"Active":null;
  ChipAll.Tag=isAll?"Active":null;
 }
 public void Configure(FoundationRuntime value,Func<ArchiveQuery> getScope){runtime=value;scope=getScope;}
 public (DateTime? From,DateTime? To) SelectedPeriod {get=>(FromDate.SelectedDate,ToDate.SelectedDate);set{settingPeriod=true;try{FromDate.SelectedDate=value.From;ToDate.SelectedDate=value.To;UpdateChipSelection();}finally{settingPeriod=false;}PeriodChanged?.Invoke(this,EventArgs.Empty);}}
 public bool ValidatePeriod()
 {
  if(!DateInput.Validate(FromDate,ToDate,out var error)){Message.Text=error;return false;}
  if(FromDate.SelectedDate>ToDate.SelectedDate){Message.Text="A data inicial não pode ser posterior à data final.";return false;}Message.Text="";return true;
 }
 public event EventHandler? PeriodChanged;
 public bool TryGetSharedPeriod(out (DateTime? From,DateTime? To) period)
 {
  period=SelectedPeriod;
  if(period.From.HasValue!=period.To.HasValue||period.From>period.To)return false;
  return WgsDfe.Application.DatePeriod.TryParse(FromDate.Text,out var from)&&WgsDfe.Application.DatePeriod.TryParse(ToDate.Text,out var to)&&from?.ToDateTime(TimeOnly.MinValue)==period.From&&to?.ToDateTime(TimeOnly.MinValue)==period.To;
 }
 public void Invalidate(){Documents.ItemsSource=null;Count.Text="Filtros alterados; aplique o período.";activeQuery=null;page=1;Previous.IsEnabled=Next.IsEnabled=false;}
 public async Task RefreshAsync(bool reset=true)
 {
  await refreshGate.WaitAsync();
  try{await RefreshCoreAsync(reset);}finally{refreshGate.Release();}
 }
 private async Task RefreshCoreAsync(bool reset)
 {
  if(busy||runtime is null||scope is null)return;
  if(!ValidatePeriod())return;
  busy=true;IsEnabled=false;
  try
  {
   if(reset){page=1;activeQuery=scope() with{From=FromDate.SelectedDate is DateTime f?DateOnly.FromDateTime(f):null,To=ToDate.SelectedDate is DateTime t?DateOnly.FromDateTime(t):null};}
   if(activeQuery is null)return;if(activeQuery.CompanyIds?.Count==0){Message.Text="Selecione ao menos uma empresa.";return;}var query=activeQuery with{Page=page};
   var result=await Task.Run(async()=>{await using var services=runtime.Services.CreateAsyncScope();return await services.ServiceProvider.GetRequiredService<IArchiveService>().SearchAsync(query);});
   Documents.ItemsSource=result.Items;last=Math.Max(1,(result.Total+49)/50);Previous.IsEnabled=page>1;Next.IsEnabled=page<last;Count.Text=$"{result.Total} nota(s) no período · Página {page} de {last} · Exportação inclui todas as páginas";
   Message.Text=result.Total==0?"Nenhuma nota no acervo para as empresas e o período selecionados. Consulte a SEFAZ para buscar os documentos disponíveis; períodos antigos podem não estar disponíveis na fonte.":"";
  }
  catch(ArchiveException ex){Message.Text=ex.Message;Documents.ItemsSource=null;}
  catch{Message.Text="Não foi possível filtrar o acervo. Confira as empresas e o período.";Documents.ItemsSource=null;}
  finally{busy=false;IsEnabled=true;}
 }
 public FrameworkElement DetachFilters(){((Grid)Content).Children.Remove(PeriodFilters);return PeriodFilters;}
 public ArchiveQuery? ExportQuery => activeQuery;
 private void Export_Click(object sender,RoutedEventArgs e)
 {
  if(runtime is null||scope is null||!ValidatePeriod())return;
  var query=scope() with{From=FromDate.SelectedDate is DateTime f?DateOnly.FromDateTime(f):null,To=ToDate.SelectedDate is DateTime t?DateOnly.FromDateTime(t):null};
  if(query.CompanyIds?.Count==0){Message.Text="Selecione ao menos uma empresa.";return;}
  WorkspaceNavigator.Open(new ExportWindow(runtime,query));
 }
 private async void Search_Click(object sender,RoutedEventArgs e)=>await RefreshAsync();
 private async void QuickPeriod_Click(object sender,RoutedEventArgs e)
 {
  var today=DateTime.Today;var mode=((Button)sender).CommandParameter?.ToString()??((Button)sender).Tag?.ToString();
  SelectedPeriod=(mode switch{"today"=>today,"week"=>today.AddDays(-6),_=>new DateTime(today.Year,today.Month,1)},today);
  await RefreshAsync();
 }
 private async void Clear_Click(object sender,RoutedEventArgs e){FromDate.SelectedDate=ToDate.SelectedDate=null;UpdateChipSelection();await RefreshAsync();}
 private async void Previous_Click(object sender,RoutedEventArgs e){page=Math.Max(1,page-1);await RefreshAsync(false);}
 private async void Next_Click(object sender,RoutedEventArgs e){page=Math.Min(last,page+1);await RefreshAsync(false);}
 private async void Details_Click(object sender,RoutedEventArgs e)
 {
  if(busy||runtime is null||Documents.SelectedItem is not ArchiveDocumentRow row)return;busy=true;IsEnabled=false;
  try
  {
   var detail=await Task.Run(async()=>{await using var services=runtime.Services.CreateAsyncScope();return await services.ServiceProvider.GetRequiredService<IArchiveService>().DetailAsync(row.Id);});
   WorkspaceNavigator.Open(new ArchiveDetailWindow(runtime,$"{row.ModelLabel} Nº {row.Number} (Série {row.Series}) · {row.StatusLabel} · Chave: {row.Key} · Emitente: {row.IssuerName} → Destinatário: {row.RecipientName}",detail.Files,detail.Events));
  }
  catch{Message.Text="Não foi possível abrir o documento selecionado.";}
  finally{busy=false;IsEnabled=true;}
 }
 private void Documents_DoubleClick(object sender,System.Windows.Input.MouseButtonEventArgs e)=>Details_Click(sender,e);
}
