using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Certificates;
using WgsDfe.Application.Companies;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class CertificateLinksWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private CertificateRow certificate;private int page=1,last=1;private bool busy;
 public CertificateLinksWindow(FoundationRuntime runtime,CertificateRow certificate){InitializeComponent();WindowLayout.Attach(this);this.runtime=runtime;this.certificate=certificate;DebouncedSearch.Attach(Search,async token=>{while(busy)await Task.Delay(20,token);token.ThrowIfCancellationRequested();page=1;await Execute(()=>LoadSearch(token));});Ui.SetSearch(Search,true);Ui.SetPlaceholder(Search,"Pesquisar empresas…");TitleText.Text=certificate.Label+" · "+certificate.Purpose;FirstLoaded+=async(_,_)=>await Execute(Load);Closing+=(_,e)=>{if(busy)e.Cancel=true;};}
 private async Task Execute(Func<Task> action)
 {
  if(busy)return;busy=true;SearchPanel.IsEnabled=Controls.IsEnabled=Companies.IsEnabled=Links.IsEnabled=UnlinkButton.IsEnabled=false;
  try{await action();}catch(CertificateException e){Message.Text=e.Message;}catch(CompanyException e){Message.Text=e.Message;}catch{Message.Text="Não foi possível atualizar as associações.";}
  finally{busy=false;SearchPanel.IsEnabled=Controls.IsEnabled=Companies.IsEnabled=Links.IsEnabled=UnlinkButton.IsEnabled=true;}
 }
 private Task<T> Service<T>(Func<ICertificateService,Task<T>> action)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<ICertificateService>());});
 private Task Load()=>LoadSearch(default);
 private async Task LoadSearch(CancellationToken token)
 {
  string search=Search.Text;var result=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ICompanyService>().SearchAsync(new(search,null,page),token);});
  token.ThrowIfCancellationRequested();last=Math.Max(1,(result.Total+49)/50);Companies.ItemsSource=result.Items;PageText.Text=$"{page}/{last}";Links.ItemsSource=await Service(s=>s.LinksAsync(certificate.Id));certificate=(await Service(s=>s.ListAsync())).Single(x=>x.Id==certificate.Id);
 }
 private async void Search_Click(object sender,RoutedEventArgs e){page=1;await Execute(Load);}
 private async void Previous_Click(object sender,RoutedEventArgs e){page=Math.Max(1,page-1);await Execute(Load);}
 private async void Next_Click(object sender,RoutedEventArgs e){page=Math.Min(last,page+1);await Execute(Load);}
 private async void Associate_Click(object sender,RoutedEventArgs e)
 {
  var ids=Companies.SelectedItems.Cast<CompanyRow>().Select(x=>x.Id).ToArray();if(ids.Length==0){Message.Text="Selecione uma ou mais empresas.";return;}string source=(string)Source.SelectedValue;
  await Execute(async()=>{await Service(async s=>{await s.AssociateAsync(certificate.Id,certificate.Revision,ids,source,Guid.NewGuid());return true;});await Load();Message.Text="Associações locais salvas. A autorização fiscal ainda será verificada.";});
 }
 private async void Unlink_Click(object sender,RoutedEventArgs e)
 {
  if(Links.SelectedItem is not CertificateLink link){Message.Text="Selecione uma associação atual.";return;}
  if(MessageBox.Show(Owner,$"Remover a associação com {link.CompanyName}?","Remover associação",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
  await Execute(async()=>{await Service(async s=>{await s.UnlinkAsync(link.Id,link.Revision,Guid.NewGuid());return true;});await Load();Message.Text="Associação removida.";});
 }
}
