using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Companies;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Companies;
namespace WgsDfe.Desktop;
public partial class CompanyImportWindow : WorkspaceView
{
 private readonly ImportPlan plan;private readonly FoundationRuntime runtime;private IReadOnlyList<ImportRow> rows;
 private CancellationTokenSource? cancellation;private bool running,finished;
 public string ResultSummary {get;private set;}="Importação não iniciada.";
 public CompanyImportWindow(ImportPlan plan,FoundationRuntime runtime)
 {
  InitializeComponent();WindowLayout.Attach(this);this.plan=plan;this.runtime=runtime;rows=plan.Rows;
  SummaryText.Text=$"Arquivo: {plan.SourceName}{(plan.SheetName is null?"":$" · Aba: {plan.SheetName}")}\n{rows.Count} linhas · {plan.Ready} prontas · {plan.Duplicates} duplicadas · {plan.Invalid} inválidas";
  ImportButton.IsEnabled=plan.Ready>0;ProgressBar.Maximum=Math.Max(1,rows.Count);Render();
  Closing+=(_,e)=>{if(running){cancellation?.Cancel();e.Cancel=true;MessageText.Text="Cancelando após preservar os itens já concluídos…";}};
 }
 private void Render()=>RowsGrid.ItemsSource=rows.Select(r=>new{r.RowNumber,Cnpj=r.Data?.Cnpj??r.RawCnpj,LegalName=r.Data?.LegalName??"",Status=CompanyImportReport.Label(r.Status),r.Reason}).ToArray();
 private async void Start_Click(object sender,RoutedEventArgs e)
 {
  if(running||finished)return;running=true;ImportButton.IsEnabled=false;ReportButton.IsEnabled=false;CloseButton.Content="Cancelar importação";cancellation=new();
  var progress=new Progress<ImportProgress>(p=>{ProgressBar.Value=p.Processed;MessageText.Text=$"Processadas: {p.Processed} de {p.Total} · Importadas: {p.Imported}";});
  try
  {
   var result=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ICompanyImportService>().ImportAsync(plan,progress,cancellation.Token);});
   rows=result.Rows;ResultSummary=$"{(result.Cancelled?"Importação cancelada":"Importação concluída")}: {result.Imported} importadas, {result.Duplicates} duplicadas, {result.Invalid} inválidas, {rows.Count(r=>r.Status==ImportRowStatus.Cancelled)} não processadas.";
   MessageText.Text=ResultSummary;Render();finished=true;
  }
  catch(OperationCanceledException){MessageText.Text=ResultSummary="Importação cancelada antes de iniciar.";}
  catch(CompanyException ex){MessageText.Text=ex.Message;finished=true;}
  catch{MessageText.Text=ResultSummary="A importação foi interrompida. Os cadastros concluídos foram preservados. Analise novamente o arquivo; CNPJs já importados serão identificados.";finished=true;ReportButton.Visibility=Visibility.Collapsed;}
  finally{running=false;cancellation.Dispose();cancellation=null;CloseButton.Content="Fechar";ReportButton.IsEnabled=true;ImportButton.IsEnabled=!finished&&plan.Ready>0;}
 }
 private void Close_Click(object sender,RoutedEventArgs e){if(running)cancellation?.Cancel();else Close();}
 private void Report_Click(object sender,RoutedEventArgs e)
 {
  var file=new Microsoft.Win32.SaveFileDialog{Filter="Relatório CSV (*.csv)|*.csv",FileName="Importacao_Empresas.csv"};if(file.ShowDialog(Owner)!=true)return;
  try{CompanyImportReport.SaveCsv(file.FileName,rows);MessageText.Text="Relatório salvo.";}catch{MessageText.Text="Não foi possível salvar. Escolha uma pasta com permissão de gravação.";}
 }
}
