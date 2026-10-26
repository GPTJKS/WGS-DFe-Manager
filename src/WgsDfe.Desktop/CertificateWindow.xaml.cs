using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Certificates;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class CertificateWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private bool busy;
 public CertificateWindow(FoundationRuntime runtime)
 {InitializeComponent();ListSearch.Attach(CertificatesGrid);WindowLayout.Attach(this);this.runtime=runtime;FirstLoaded+=async(_,_)=>await Execute(Refresh);Closing+=(_,e)=>{if(busy)e.Cancel=true;};}
 private Task<T> Service<T>(Func<ICertificateService,Task<T>> action)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<ICertificateService>());});
 private async Task Execute(Func<Task> action)
 {
  if(busy)return;busy=true;ImportPanel.IsEnabled=Actions.IsEnabled=CertificatesGrid.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;
  try{await action();}catch(CertificateException e){Message.Text=e.Message;}catch(WgsDfe.Application.Nfe.NfeException e){Message.Text=e.Message;}catch{Message.Text="Operação não concluída. Verifique o perfil do Windows, o arquivo e a pasta de dados.";}
  finally{busy=false;ImportPanel.IsEnabled=Actions.IsEnabled=CertificatesGrid.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;}
 }
 private async Task Refresh()
 {
  var selected=(CertificatesGrid.SelectedItem as CertificateRow)?.Id;var rows=await Service(s=>s.ListAsync());CertificatesGrid.ItemsSource=rows;CertificatesGrid.SelectedItem=rows.FirstOrDefault(x=>x.Id==selected);
  Alerts.Text=$"{rows.Count} certificado(s) · {rows.Count(x=>x.ValidityAt(DateTime.UtcNow)==CertificateValidity.Expired)} vencido(s) · {rows.Count(x=>x.ValidityAt(DateTime.UtcNow)==CertificateValidity.Expiring)} vencendo em até 30 dias";
 }
 private CertificateRow? Selected(){if(CertificatesGrid.SelectedItem is CertificateRow row)return row;Message.Text="Selecione um certificado.";return null;}
 private void Selection_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(CertificatesGrid.SelectedItem is not CertificateRow row){Details.Text="";return;}
  Details.Text=$"Titular: {row.Subject}\nValidade: {row.ValidFromUtc.ToLocalTime():dd/MM/yyyy HH:mm} a {row.ValidToUtc.ToLocalTime():dd/MM/yyyy HH:mm}\nImpressão SHA-256: {row.Thumbprint}";
 }
 private void OpenImport(CertificateRow? replace)
 {
  WorkspaceNavigator.Open(new CertificateImportView(runtime,replace,async result=>
  {
   await Refresh();
   if(result.Certificate is { } saved)CertificatesGrid.SelectedItem=CertificatesGrid.Items.Cast<CertificateRow>().FirstOrDefault(x=>x.Id==saved.Id);
   Message.Text=result.CleanupPending?"Certificado salvo. Há limpeza pendente no cofre.":"Certificado validado, protegido e salvo com sucesso.";
  }));
 }
 private void Add_Click(object sender,RoutedEventArgs e)=>OpenImport(null);
 private void Replace_Click(object sender,RoutedEventArgs e){var row=Selected();if(row is not null)OpenImport(row);}
 private async void Test_Click(object sender,RoutedEventArgs e){var row=Selected();if(row is not null)await Execute(async()=>{var result=await Service(s=>s.TestAsync(row.Id,Guid.NewGuid()));Message.Text=result.Message;await Refresh();});}
 private async void Remove_Click(object sender,RoutedEventArgs e)
 {
  var row=Selected();if(row is null)return;if(MessageBox.Show(Owner,$"Remover {row.Label} e suas associações locais?\nO arquivo original e o acervo fiscal serão preservados.","Remover certificado",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
  await Execute(async()=>{var result=await Service(s=>s.RemoveAsync(row.Id,row.Revision,Guid.NewGuid()));await Refresh();Message.Text=result.CleanupPending?"Cadastro removido. Limpeza do cofre pendente; tente atualizar novamente.":"Certificado e associações removidos.";});
 }
 private async void DefaultAccountant_Click(object sender,RoutedEventArgs e)
 {
  var row=Selected();if(row is null)return;
  if(row.Use!=CertificateUse.Accountant){Message.Text="O e-CNPJ da empresa já é usado automaticamente nas consultas nacionais. Para SC, importe um certificado com finalidade Escritório / contador.";return;}
  await Execute(async()=>{await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<WgsDfe.Infrastructure.Certificates.CertificateSelection>().SetAccountantAsync(row.Id);});Message.Text="Certificado do contador definido para as consultas SC. Não é necessário associar empresa por empresa.";});
 }

 private void Links_Click(object sender,RoutedEventArgs e){var row=Selected();if(row is not null&&!busy)WorkspaceNavigator.Open(new CertificateLinksWindow(runtime,row));}
 private async void Refresh_Click(object sender,RoutedEventArgs e)=>await Execute(async()=>{var pending=await Service(s=>s.CleanupAsync());await Refresh();Message.Text=pending>0?"Ainda há limpeza pendente no cofre.":"Lista atualizada.";});
}
