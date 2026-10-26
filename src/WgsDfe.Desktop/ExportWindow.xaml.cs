using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Export;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class ExportWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private readonly ArchiveQuery initial;private readonly Guid[]? documentIds,eventIds;
 private bool busy;private CancellationTokenSource? cancellation;private ExportResult? result;
 public override bool CanRunInBackground=>true;
 public override bool IsWorking=>busy;
 public override string OperationStatus=>Message.Text;
 public override void CancelOperation()=>Cancel_Click(this,new RoutedEventArgs());
 public ExportWindow(FoundationRuntime runtime,ArchiveQuery? filter=null,Guid[]? documents=null,Guid[]? events=null)
 {
  InitializeComponent();WindowLayout.Attach(this);this.runtime=runtime;initial=filter??new();documentIds=documents;eventIds=events;var preferences=UserPreferences.Load(runtime.DataDirectory);DestinationBox.Text=preferences.ExportDirectory;FormatBox.SelectedIndex=1;
  From.SelectedDate=initial.From?.ToDateTime(TimeOnly.MinValue);To.SelectedDate=initial.To?.ToDateTime(TimeOnly.MinValue);EnvironmentBox.SelectedIndex=1;
  if(initial.Models is not null||initial.Model.HasValue)
  {var models=initial.Models??new[]{initial.Model!.Value};NfeBox.IsChecked=models.Contains(DocumentModel.NFe);NfceBox.IsChecked=models.Contains(DocumentModel.NFCe);CteBox.IsChecked=models.Contains(DocumentModel.CTe);CteOsBox.IsChecked=models.Contains(DocumentModel.CTeOS);NfcomBox.IsChecked=models.Contains(DocumentModel.NFCom);}
  if(initial.CompanyId==Guid.Empty)UnassignedBox.IsChecked=true;
  if(filter is not null)ScopeText.Text=$"Empresas selecionadas: {initial.CompanyIds?.Count.ToString()??(initial.CompanyId.HasValue?"1":"todas")} · Período: {initial.From?.ToString("dd/MM/yyyy")??"início"} a {initial.To?.ToString("dd/MM/yyyy")??"hoje/todos"}. Filtros e seleção de documentos preservados.";
  FirstLoaded+=async(_,_)=>await Execute(async()=>{await using var scope=runtime.Services.CreateAsyncScope();var companies=await scope.ServiceProvider.GetRequiredService<IArchiveService>().CompaniesAsync(cancellation!.Token);CompaniesGrid.ItemsSource=companies.Where(x=>x.Id.HasValue&&x.Id!=Guid.Empty&&(!initial.CompanyId.HasValue||x.Id==initial.CompanyId)&&(initial.CompanyIds is null||initial.CompanyIds.Contains(x.Id.Value))).ToArray();if(initial.CompanyIds is not null){AllCompanies.IsChecked=false;AllCompanies.IsEnabled=false;foreach(var row in CompaniesGrid.Items.Cast<ArchiveCompany>().Where(x=>initial.CompanyIds.Contains(x.Id!.Value)))CompaniesGrid.SelectedItems.Add(row);UnassignedBox.IsChecked=false;UnassignedBox.IsEnabled=false;}if(initial.CompanyId.HasValue){AllCompanies.IsChecked=false;foreach(var row in CompaniesGrid.Items.Cast<ArchiveCompany>())CompaniesGrid.SelectedItems.Add(row);AllCompanies.IsEnabled=false;CompaniesGrid.IsEnabled=false;UnassignedBox.IsEnabled=false;}else Scope_Changed(this,new RoutedEventArgs());});
  Closing+=(_,e)=>{if(busy){e.Cancel=true;Message.Text="Cancele e aguarde a operação antes de fechar.";}};
 }
 private Task<T> Use<T>(Func<IExportService,Task<T>> work)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await work(scope.ServiceProvider.GetRequiredService<IExportService>());});
 private async Task Execute(Func<Task> action)
 {
  if(busy)return;busy=true;Filters.IsEnabled=false;Options.IsEnabled=false;DestinationCard.IsEnabled=false;AllCompanies.IsEnabled=false;CompaniesGrid.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;CancelButton.IsEnabled=true;cancellation=new();
  try{await action();}catch(BackgroundTaskConflictException ex){Message.Text=ex.Message;}catch(ExportException e){Message.Text=e.Message;}catch(OperationCanceledException){Message.Text="Cancelado; XMLs concluídos e preexistentes preservados.";}catch{Message.Text="Operação não concluída. Confira a seleção, a pasta de destino, as permissões e o espaço disponível.";}
  finally{busy=false;if(AllCompanies.IsChecked==true)CompaniesGrid.SelectAll();Filters.IsEnabled=true;Options.IsEnabled=true;DestinationCard.IsEnabled=true;AllCompanies.IsEnabled=initial.CompanyIds is null&&!initial.CompanyId.HasValue;CompaniesGrid.IsEnabled=!initial.CompanyId.HasValue&&AllCompanies.IsChecked!=true;BusyBar.Visibility=Visibility.Collapsed;CancelButton.IsEnabled=false;cancellation.Dispose();cancellation=null;}
 }
 private ExportRequest Request()
 {
  if(!DateInput.Validate(From,To,out var error))throw new ExportException("EXPORT_INVALID_DATE");
  EnabledDocuments models=EnabledDocuments.None;if(NfeBox.IsChecked==true)models|=EnabledDocuments.NFe;if(CteBox.IsChecked==true)models|=EnabledDocuments.CTe;if(CteOsBox.IsChecked==true)models|=EnabledDocuments.CTeOS;if(NfceBox.IsChecked==true)models|=EnabledDocuments.NFCe;if(NfcomBox.IsChecked==true)models|=EnabledDocuments.NFCom;
  var filter=initial with{From=From.SelectedDate.HasValue?DateOnly.FromDateTime(From.SelectedDate.Value):null,To=To.SelectedDate.HasValue?DateOnly.FromDateTime(To.SelectedDate.Value):null,Environment=FiscalEnvironment.Production};
  return new(filter,DestinationBox.Text,FormatBox.SelectedIndex==0?ExportFormat.Zip:ExportFormat.Folder,initial.CompanyId.HasValue||AllCompanies.IsChecked==true?null:CompaniesGrid.SelectedItems.Cast<ArchiveCompany>().Select(x=>x.Id!.Value).ToArray(),models,EventsBox.IsChecked==true,OrphansBox.IsChecked==true,VersionsBox.IsChecked==true,ConflictsBox.IsChecked==true,initial.CompanyId==Guid.Empty||!initial.CompanyId.HasValue&&AllCompanies.IsChecked==true&&UnassignedBox.IsChecked==true,SkipBox.IsChecked==true,documentIds,eventIds);
 }
 private async void Preview_Click(object sender,RoutedEventArgs e){await Execute(async()=>{var request=Request();var plan=await Use(s=>s.PreviewAsync(request,cancellation!.Token));PreviewText.Text=$"{plan.Documents} nota(s) · {plan.Events} evento(s) · {plan.Files} arquivo(s) · Estimativa: {plan.EstimatedBytes/1024d/1024d:N2} MiB · {plan.SharedCopies} cópia(s) por participação em empresas. A integridade física será conferida na exportação.";});}
 private async void Export_Click(object sender,RoutedEventArgs e)=>await ExportAsync();
 private async Task ExportAsync()
 {
  var progress=new Progress<ExportProgress>(p=>Message.Text=$"{ExportMessages.Describe(p.Code)} · {p.Processed}/{p.Total} · Preparados: {p.Written} · Não exportados: {p.Skipped}");
  await Execute(async()=>{var request=Request();result=null;ResultGrid.ItemsSource=null;OutputPath.Clear();Message.Text="Preparando exportação; aguardando acesso ao acervo e ao destino.";await SavePreferencesAsync();var owner=cancellation!;var observed=BackgroundTaskCoordinator.For(runtime).Observe(owner,progress);result=await BackgroundTaskCoordinator.For(runtime).RunAsync(Title,new[]{"EXPORT"},owner,token=>Use(s=>s.ExportAsync(request,observed,token)));ResultGrid.ItemsSource=result.Rows;OutputPath.Text=result.Output??"";Message.Text=$"{result.Message} · Empresa(s): {string.Join(", ",result.Rows.Select(x=>x.Details.Company).Distinct().Take(3))} · Total de empresas: {result.Rows.Select(x=>x.Details.Folder).Distinct().Count()} · Documentos: {result.Rows.Where(x=>x.Kind=="Documento").Select(x=>x.Key).Distinct().Count()} · Período: {request.Filter.From?.ToString("dd/MM/yyyy")??"início"} a {request.Filter.To?.ToString("dd/MM/yyyy")??"fim"} · XMLs gravados: {result.Written} · Idênticos ignorados: {result.Rows.Count(x=>x.Code=="ALREADY_EXISTS")} · Substituídos: 0 · Conflitos: {result.Rows.Count(x=>x.Code=="EXPORT_DESTINATION_CONFLICT")} · Indisponíveis: {result.Rows.Count(x=>x.Code=="FileMissing"||x.Code=="StorageUnavailable")} · Falhas: {result.Rows.Count(x=>x.Code is not ("EXPORTED" or "ALREADY_EXISTS" or "EXPORT_DESTINATION_CONFLICT" or "NOT_PROCESSED"))} · Não processados: {result.Rows.Count(x=>x.Code=="NOT_PROCESSED")}.";});
 }
 private void OpenFolder_Click(object sender,RoutedEventArgs e)
 {try{if(result?.Output is string output)System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Directory.Exists(output)?output:System.IO.Path.GetDirectoryName(output)!){UseShellExecute=true});else Message.Text="Conclua uma exportação primeiro.";}catch{Message.Text="Não foi possível abrir a pasta.";}}
 private void CopySummary_Click(object sender,RoutedEventArgs e){try{Clipboard.SetText(Message.Text);}catch{Message.Text="Não foi possível copiar o resumo. Tente novamente.";}}
 private async void Destination_Click(object sender,RoutedEventArgs e){var dialog=new Microsoft.Win32.OpenFolderDialog{Title="Escolher destino dos XMLs",Multiselect=false};if(dialog.ShowDialog(Owner)==true){DestinationBox.Text=dialog.FolderName;await SavePreferencesAsync();}}
 private async Task SavePreferencesAsync(){var destination=DestinationBox.Text;bool zip=FormatBox.SelectedIndex==0;try{await Task.Run(()=>{UserPreferences.Update(runtime.DataDirectory,p=>p with{ExportDirectory=destination,Zip=zip});});}catch{Message.Text="Preferência não salva. Verifique a pasta de dados.";}}
 private void Scope_Changed(object sender,RoutedEventArgs e){if(CompaniesGrid is not null&&!busy){CompaniesGrid.IsEnabled=!initial.CompanyId.HasValue&&AllCompanies.IsChecked!=true;UnassignedBox.IsEnabled=!initial.CompanyId.HasValue&&AllCompanies.IsChecked==true;}}
 private void Cancel_Click(object sender,RoutedEventArgs e){cancellation?.Cancel();CancelButton.IsEnabled=false;Message.Text="Cancelamento solicitado. Aguarde o ponto seguro.";}
}
