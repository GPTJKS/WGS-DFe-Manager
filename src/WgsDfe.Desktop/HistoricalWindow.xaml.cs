using System.Windows;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Historical;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class HistoricalWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private bool busy;private int page=1;private CancellationTokenSource? cancellation;
 private Guid? Batch=>(BatchesGrid.SelectedItem as HistoricalBatchRow)?.Id;
 private Guid? Company=>CompanyBox.SelectedItem is HistoricalCompany c&&c.Id!=Guid.Empty?c.Id:null;
 public HistoricalWindow(FoundationRuntime runtime)
 {
  InitializeComponent();WindowLayout.Attach(this);this.runtime=runtime;FirstLoaded+=async(_,_)=>await Execute(Refresh);
  Closing+=(_,e)=>{if(busy){e.Cancel=true;Message.Text="Cancele e aguarde a gravação do ponto seguro antes de fechar.";}};
  Closed+=(_,_)=>runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=false;
 }
 private Task<T> Use<T>(Func<IHistoricalRecoveryService,Task<T>> work)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await work(scope.ServiceProvider.GetRequiredService<IHistoricalRecoveryService>());});
 private async Task Execute(Func<Task> work)
 {
  if(busy)return;busy=true;ImportControls.IsEnabled=false;Actions.IsEnabled=false;BatchesGrid.IsEnabled=false;Pager.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;cancellation=new();CancelButton.IsEnabled=true;
  try{await work();}catch(HistoricalException e){Message.Text=e.Message+" ("+e.Code+")";}catch(OperationCanceledException){Message.Text="Cancelado. Itens já confirmados e pedidos recebidos foram preservados.";}catch{Message.Text="Operação não concluída. Os pontos já gravados foram preservados. Confira o lote e a pasta de dados.";}
  finally{busy=false;ImportControls.IsEnabled=true;Actions.IsEnabled=true;BatchesGrid.IsEnabled=true;Pager.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;CancelButton.IsEnabled=false;cancellation.Dispose();cancellation=null;}
 }
 private async Task Refresh()
 {
  var selected=Batch;var company=Company;var companies=await Use(s=>s.CompaniesAsync(cancellation!.Token));CompanyBox.ItemsSource=new[]{new HistoricalCompany(Guid.Empty,"Automática: emitente da chave","")}.Concat(companies).ToArray();CompanyBox.SelectedItem=CompanyBox.Items.Cast<HistoricalCompany>().FirstOrDefault(x=>x.Id==company);if(CompanyBox.SelectedIndex<0)CompanyBox.SelectedIndex=0;
  var providers=await Use(s=>Task.FromResult(s.Providers()));ProviderBox.ItemsSource=providers;if(providers.Count>0)ProviderBox.SelectedIndex=0;
  HistoricalBox.IsEnabled=providers.Count>0;PaidBox.IsEnabled=providers.Any(x=>x.IsPaid);PaidLimitBox.IsEnabled=PaidBox.IsEnabled;
  ProviderNotice.Text=providers.Count==0?"Nenhum provedor histórico instalado. A conferência local e as fontes oficiais estão disponíveis; a conexão com uma API histórica depende de integração e credenciais próprias.":"Chamadas potencialmente pagas são reservadas antes do envio. O limite inclui envio, acompanhamento e download e acumula nas retomadas.";
  var batches=await Use(s=>s.BatchesAsync(cancellation!.Token));BatchesGrid.ItemsSource=batches;BatchesGrid.SelectedItem=batches.FirstOrDefault(x=>x.Id==selected)??batches.FirstOrDefault();await ShowRows();
 }
 private async Task ShowRows(){if(Batch is not Guid id){RowsGrid.ItemsSource=null;GroupsGrid.ItemsSource=null;return;}var result=await Use(s=>s.RowsAsync(id,page,ct:cancellation!.Token));RowsGrid.ItemsSource=result.Rows;PageText.Text=$"Página {page} · {result.Total} linha(s)";GroupsGrid.ItemsSource=await Use(s=>s.GroupsAsync(id,cancellation!.Token));}
 private async void Import_Click(object sender,RoutedEventArgs e)
 {
  var dialog=new OpenFileDialog{Filter="Chaves|*.xlsx;*.csv;*.txt",Multiselect=false};if(dialog.ShowDialog(Owner)!=true)return;
  var request=new HistoricalImport(dialog.FileName,FiscalEnvironment.Production,Company);
  await Execute(async()=>{var id=await Use(s=>s.ImportAsync(request,cancellation!.Token));page=1;await Refresh();BatchesGrid.SelectedItem=BatchesGrid.Items.Cast<HistoricalBatchRow>().First(x=>x.Id==id);await ShowRows();Message.Text="Chaves importadas e classificadas. Confira os resultados antes de iniciar as consultas.";});
 }
 private async void Template_Click(object sender,RoutedEventArgs e)
 {
  var dialog=new SaveFileDialog{Title="Salvar modelo de chaves para recuperação histórica",FileName="Modelo_Importacao_Chaves.xlsx",Filter="Planilha Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|Texto (*.txt)|*.txt",AddExtension=true};if(dialog.ShowDialog(Owner)!=true)return;
  await Execute(async()=>
  {
   var extension=dialog.FilterIndex switch{2=>"csv",3=>"txt",_=>"xlsx"};
   var source=System.IO.Path.Combine(AppContext.BaseDirectory,"Modelos","Modelo_Importacao_Chaves."+extension);
   await Task.Run(()=>System.IO.File.Copy(source,dialog.FileName,true));
   Message.Text="Modelo salvo. Preencha a coluna CHAVE como texto a partir da linha 2 (uma chave de 44 posições por linha, sem fórmulas) e clique em Importar XLSX / CSV / TXT.";
  });
 }
 private async void Run_Click(object sender,RoutedEventArgs e)
 {
  if(Batch is not Guid id){Message.Text="Selecione um lote.";return;}if(!int.TryParse(PaidLimitBox.Text,out int max)||max is <0 or >100000){Message.Text="Informe limite entre 0 e 100.000 chamadas.";return;}
  if(BatchesGrid.SelectedItem is HistoricalBatchRow {Environment:not FiscalEnvironment.Production}){Message.Text="Lote de homologação preservado para consulta local. Importe as chaves em um novo lote de Produção para executar a recuperação.";return;}
  var options=new HistoricalOptions(OfficialBox.IsChecked==true,HistoricalBox.IsChecked==true,(ProviderBox.SelectedItem as HistoricalProviderChoice)?.Code,PaidBox.IsChecked==true,max);
  runtime.Services.GetRequiredService<ProviderOptions>().EnableExternalRequests=NetworkBox.IsChecked==true;
  var progress=new Progress<HistoricalRecoveryProgress>(p=>Message.Text=$"{p.Processed}/{p.Total} · Linha {p.Line} · {HistoricalMessages.Describe(p.Code)}");
  await Execute(async()=>{await Use(async s=>{await s.RunAsync(id,options,progress,cancellation!.Token);return true;});bool cancelled=cancellation!.IsCancellationRequested;await Refresh();Message.Text=cancelled?"Cancelado; pontos confirmados preservados.":"Conferência concluída. Consulte os motivos por linha e salve o relatório. Retome pedidos pendentes após o prazo indicado.";});
 }
 private async void Assign_Click(object sender,RoutedEventArgs e){if(Batch is not Guid id||Company is not Guid company){Message.Text="Selecione um lote e uma empresa.";return;}await Execute(async()=>{await Use(async s=>{await s.AssignUnassignedAsync(id,company,cancellation!.Token);return true;});await ShowRows();});}
 private async void Report_Click(object sender,RoutedEventArgs e){if(Batch is not Guid id)return;var dialog=new SaveFileDialog{Filter="Relatório CSV|*.csv",FileName="Recuperacao_historica.csv"};if(dialog.ShowDialog(Owner)!=true)return;await Execute(async()=>{await Use(async s=>{await s.WriteReportAsync(id,dialog.FileName,cancellation!.Token);return true;});Message.Text="Relatório salvo.";});}
 private async void Refresh_Click(object sender,RoutedEventArgs e)=>await Execute(Refresh);
 private async void Batch_Changed(object sender,System.Windows.Controls.SelectionChangedEventArgs e){if(!busy){page=1;await Execute(ShowRows);}}
 private async void Previous_Click(object sender,RoutedEventArgs e){if(page>1){page--;await Execute(ShowRows);}}
 private async void Next_Click(object sender,RoutedEventArgs e){if(RowsGrid.Items.Count==50){page++;await Execute(ShowRows);}}
 private void Cancel_Click(object sender,RoutedEventArgs e){cancellation?.Cancel();CancelButton.IsEnabled=false;Message.Text="Cancelamento solicitado. Aguarde o ponto seguro.";}
}
