using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Companies;
using WgsDfe.Infrastructure;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
public partial class MainWindow : Window
{
 private readonly FoundationRuntime runtime;
 private int page=1,total;private bool busy;
 public MainWindow(FoundationStatus status,FoundationRuntime runtime)
 {
  InitializeComponent();DialogHost.OpenStateChanged+=(_,_)=>ShellSurface.IsEnabled=!DialogHost.IsOpen;InitializeBackgroundTasks();WindowLayout.Attach(this);CompanyViewport.SizeChanged+=(_,_)=>{bool compact=CompanyViewport.ActualHeight is >0 and <=700;CompanyPage.Margin=compact?new Thickness(12,6,12,6):new Thickness(16,10,16,10);CompanyPage.Width=Math.Max(0,CompanyViewport.ActualWidth-(compact?24:32));CompanyPage.Height=Math.Max(0,CompanyViewport.ActualHeight-(compact?12:20));};this.runtime=runtime;
  var prefs=UserPreferences.Load(runtime.DataDirectory);
  string orgName=!string.IsNullOrWhiteSpace(prefs.LicensedOrganization)?prefs.LicensedOrganization:status.OrganizationName;
  ApplyOrganizationIdentity(orgName);
  InitializeInput();
  DebouncedSearch.Attach(SearchBox,async token=>{while(busy)await Task.Delay(20,token);token.ThrowIfCancellationRequested();await ExecuteAsync(()=>LoadSearchPageAsync(token));});
  Loaded+=async(_,_)=>{await RefreshAsync();ShowHome();};
  Closing+=(_,e)=>{if(!CanCloseWorkspace()){e.Cancel=true;return;}if(busy){e.Cancel=true;MessageText.Text="Aguarde a conclusão da operação ou cancele a importação na janela de conferência.";}};
 }
 public void ApplyOrganizationIdentity(string organizationName)
 {
  string name=string.IsNullOrWhiteSpace(organizationName)?"WGS Assessoria Contábil":organizationName.Trim();
  ShellContext.Text=name;
  OfficeText.Text=$"{name} · Cadastro de empresas";
  if(SidebarOrgSubtitle is not null)SidebarOrgSubtitle.Text=name;
  if(FooterOrgText is not null)FooterOrgText.Text=name;
 }
 private Task<T> UseService<T>(Func<ICompanyService,Task<T>> operation)=>Task.Run(async()=>
 {await using var scope=runtime.Services.CreateAsyncScope();return await operation(scope.ServiceProvider.GetRequiredService<ICompanyService>());});
 private async Task ExecuteAsync(Func<Task> action)
 {
  if(busy)return;busy=true;UtilityNavigation.IsEnabled=false;Toolbar.IsEnabled=false;Navigation.IsEnabled=false;CompaniesGrid.IsEnabled=false;Pager.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;
  try{await action();}
  catch(CompanyException e){MessageText.Text=e.Message;}
  catch(OperationCanceledException){MessageText.Text="Operação cancelada.";}
  catch{MessageText.Text="Não foi possível concluir. Verifique o arquivo, o acesso à pasta de dados e tente novamente.";}
  finally{busy=false;UtilityNavigation.IsEnabled=true;Toolbar.IsEnabled=true;Navigation.IsEnabled=true;CompaniesGrid.IsEnabled=true;Pager.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;}
 }
 private Task LoadPageAsync()=>LoadSearchPageAsync(default);
 private async Task LoadSearchPageAsync(CancellationToken token)
 {
  var search=SearchBox.Text;bool? active=ActiveFilter.SelectedIndex switch{1=>true,2=>false,_=>null};
  var result=await UseService(s=>s.SearchAsync(new(search,active,All:true),token));token.ThrowIfCancellationRequested();total=result.Total;
  int activeCount=result.Items.Count(x=>x.Data.IsActive),inactiveCount=result.Items.Count(x=>!x.Data.IsActive);
  CompaniesGrid.ItemsSource=result.Items;CompanyTotal.Text=$"{total}";CompanyActive.Text=$"{activeCount}";CompanyInactive.Text=$"{inactiveCount}";
  CompanyActiveRate.Text=total>0?$"{activeCount*100.0/total:0.#}%":"0%";
  PageText.Text=$"{total} empresa(s) · Lista completa · Role para ver mais";PreviousButton.IsEnabled=NextButton.IsEnabled=false;
 }
 private async Task CertificateAlertsAsync()
 {
  try
  {
   var rows=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<WgsDfe.Application.Certificates.ICertificateService>().ListAsync();});
   int alerts=rows.Count(x=>x.ValidityAt(DateTime.UtcNow)!=WgsDfe.Application.Certificates.CertificateValidity.Valid);
   CertificateSummary.Text=rows.Count==0?"Nenhum certificado cadastrado. Adicione um A1 para preparar as consultas.":$"{rows.Count} certificado(s) cadastrado(s) · {alerts} com validade a revisar.";
   CertificateMetricText.Text=rows.Count==0?"0 A1":alerts>0?$"{alerts} Atenção":$"{rows.Count} em dia";
   SidebarCertText.Text=rows.Count==0?"e-CNPJ · Adicione A1":alerts>0?$"{alerts} certificado(s) a revisar":$"e-CNPJ · {rows.Count} ativo(s)";
   if(alerts>0)MessageText.Text=$"{alerts} certificado(s) vencido(s), vencendo em até 30 dias ou ainda não vigentes. Abra Gerenciar certificados.";
  }
  catch{CertificateSummary.Text="Validade indisponível. Abra Certificados para conferir o cadastro.";CertificateMetricText.Text="Conferir";}
 }
 private void More_Click(object sender,RoutedEventArgs e) { var button=(System.Windows.Controls.Button)sender;button.ContextMenu.PlacementTarget=button;button.ContextMenu.IsOpen=true; }
 private void Cte_Click(object sender,RoutedEventArgs e)=>ShowPage("Cte",()=>new CteWindow(runtime));
 private void Automation_Click(object sender,RoutedEventArgs e)=>ShowPage("Automation",()=>new AutomationWindow(runtime));
 private void Export_Click(object sender,RoutedEventArgs e)=>ShowPage("Export",()=>new ExportWindow(runtime));
 private void Historical_Click(object sender,RoutedEventArgs e)=>ShowPage("Historical",()=>new HistoricalWindow(runtime));
 private void Nfcom_Click(object sender,RoutedEventArgs e)=>ShowPage("Nfcom",()=>new NfeWindow(runtime,DocumentModel.NFCom));
 private void Nfce_Click(object sender,RoutedEventArgs e)=>ShowPage("Nfce",()=>new NfeWindow(runtime,DocumentModel.NFCe));
 private void Nfe_Click(object sender,RoutedEventArgs e)=>ShowPage("consultas",()=>new QueriesView(runtime));
 private void Archive_Click(object sender,RoutedEventArgs e)=>ShowPage("Archive",()=>new ArchiveWindow(runtime));
 private void Inspect_Click(object sender,RoutedEventArgs e)=>ShowPage("Inspect",()=>new DfeInspectorWindow(runtime.Services.GetRequiredService<WgsDfe.Application.Parsing.IDfeParser>()));
 private void Certificates_Click(object sender,RoutedEventArgs e)=>ShowPage("certificados",()=>new CertificateWindow(runtime));
 private Task RefreshAsync()=>ExecuteAsync(async()=>{await LoadPageAsync();await CertificateAlertsAsync();});
 private CompanyRow? Selection()
 {
  if(CompaniesGrid.SelectedItem is CompanyRow row)return row;MessageText.Text="Selecione uma empresa na lista.";return null;
 }
 private async Task OpenEditorAsync(CompanyRow? row)
 {
  var certificateStatus=row is null?"Opcional agora. Adicione o e-CNPJ para preparar as consultas nacionais.":await Task.Run(async()=>
  {
   await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDfe.Database.WgsDbContext>();
   var company=await db.Companies.FindAsync(row.Id);
   if(company is null)return "Empresa não encontrada.";
   try{var certificate=await scope.ServiceProvider.GetRequiredService<WgsDfe.Infrastructure.Certificates.CertificateSelection>().ResolveAsync(company,WgsDfe.Application.Nfe.NfeSources.National);return $"e-CNPJ selecionado automaticamente: {certificate.Label} · válido até {certificate.ValidToUtc:dd/MM/yyyy}. Adicione outro arquivo para substituir o certificado deste cadastro.";}
   catch(WgsDfe.Application.Nfe.NfeException ex){return ex.Message;}

  });
  var editor=new CompanyEditor(row,runtime.Services.GetRequiredService<WgsDfe.Infrastructure.Companies.CompanyLookup>(),certificateStatus,async (input,pfx,password)=>
  {await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<WgsDfe.Infrastructure.Companies.CompanyRegistrationService>().SaveAsync(row,input,pfx,password);});});
  editor.Closed+=async(_,_)=>{await RefreshAsync();if(editor.DialogResult==true)MessageText.Text="Cadastro salvo.";};OpenView(editor);
 }
 private async void Add_Click(object sender,RoutedEventArgs e)=>await ExecuteAsync(()=>OpenEditorAsync(null));
 private void EditRow_Click(object sender,RoutedEventArgs e){if(sender is System.Windows.Controls.Button {DataContext:CompanyRow row}){CompaniesGrid.SelectedItem=row;Edit_Click(sender,e);}}
 private void ActiveRow_Click(object sender,RoutedEventArgs e){if(sender is System.Windows.Controls.Button {DataContext:CompanyRow row}){CompaniesGrid.SelectedItem=row;Active_Click(sender,e);}}
 private async void Edit_Click(object sender,RoutedEventArgs e){var row=Selection();if(row is not null)await ExecuteAsync(()=>OpenEditorAsync(row));}
 private async void Grid_DoubleClick(object sender,MouseButtonEventArgs e){if(!busy&&CompaniesGrid.SelectedItem is CompanyRow row)await ExecuteAsync(()=>OpenEditorAsync(row));}
 private async void Active_Click(object sender,RoutedEventArgs e)
 {
  var row=Selection();if(row is null)return;
  await ExecuteAsync(async()=>{await UseService(async s=>{await s.SetActiveAsync(row.Id,row.Revision,!row.Data.IsActive,Guid.NewGuid());return true;});await LoadPageAsync();MessageText.Text=row.Data.IsActive?"Empresa inativada. O acervo foi preservado.":"Empresa ativada.";});
 }
 private async void Delete_Click(object sender,RoutedEventArgs e)
 {
  var row=Selection();if(row is null)return;
  if(MessageBox.Show(this,$"Excluir o cadastro de {row.LegalName}\nCNPJ: {row.Cnpj}?\n\nO cadastro será removido das listas e suas automações serão desativadas. XMLs, certificados e histórico fiscal serão preservados. Ao cadastrar o mesmo CNPJ novamente, a posição de consulta será recuperada.","Excluir empresa",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
  await ExecuteAsync(async()=>{await UseService(async s=>{await s.DeleteAsync(row.Id,row.Revision,Guid.NewGuid());return true;});await LoadPageAsync();MessageText.Text="Empresa excluída da lista. XMLs e histórico fiscal preservados.";});
 }
 private async void Template_Click(object sender,RoutedEventArgs e)
 {
  var file=new Microsoft.Win32.SaveFileDialog{Title="Salvar modelo de empresas",FileName="Modelo_Importacao_Empresas.xlsx",Filter="Planilha Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv",AddExtension=true};if(file.ShowDialog(this)!=true)return;
  await ExecuteAsync(async()=>{var extension=file.FilterIndex==2?"csv":"xlsx";var source=System.IO.Path.Combine(AppContext.BaseDirectory,"Modelos","Modelo_Importacao_Empresas."+extension);await Task.Run(()=>System.IO.File.Copy(source,file.FileName,true));MessageText.Text="Modelo salvo. Preencha a aba Empresas a partir da linha 2; depois use Mais ações → Importar empresas em lote. CNPJ, RazaoSocial e UF são obrigatórios.";});
 }
 private async void Import_Click(object sender,RoutedEventArgs e)
 {
  var file=new Microsoft.Win32.OpenFileDialog{Filter="Empresas (*.xlsx;*.csv)|*.xlsx;*.csv",Multiselect=false};if(file.ShowDialog(this)!=true)return;
  await ExecuteAsync(async()=>
  {
   var plan=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ICompanyImportService>().AnalyzeAsync(file.FileName);});
   var preview=new CompanyImportWindow(plan,runtime);preview.Closed+=async(_,_)=>{await RefreshAsync();MessageText.Text=preview.ResultSummary;};OpenView(preview);
  });
 }
 private async void Search_Click(object sender,RoutedEventArgs e){page=1;await RefreshAsync();}
 private async void Search_KeyDown(object sender,KeyEventArgs e){if(e.Key==Key.Enter){page=1;await RefreshAsync();}}
 private async void Previous_Click(object sender,RoutedEventArgs e){page=Math.Max(1,page-1);await RefreshAsync();}
 private async void Next_Click(object sender,RoutedEventArgs e){page++;await RefreshAsync();}
}
