using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Automation;
using WgsDfe.Application.Nfe;
using WgsDfe.Application.Cte;
using WgsDfe.Domain;
using WgsDfe.Database;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Automation;
namespace WgsDfe.Desktop;
public partial class AutomationWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private readonly AutomationRunner runner;private ScheduleRow? editing;private bool busy;
 private readonly DispatcherTimer statusTimer=new(){Interval=TimeSpan.FromSeconds(2)};
 private sealed record Source(string Code,string Label);private sealed record CompanyOption(Guid Id,string Name);
 public AutomationWindow(FoundationRuntime runtime)
 {
  InitializeComponent();SelectorSearch.Attach(CompanyBox);ListSearch.Attach(SchedulesGrid);ListSearch.Attach(RunsGrid);WindowLayout.Attach(this);this.runtime=runtime;runner=runtime.Services.GetRequiredService<AutomationRunner>();
  SourceBox.ItemsSource=new[]{new Source(NfeSources.SantaCatarina,"NF-e SC"),new Source(NfeSources.National,"NF-e Nacional"),new Source(CteSource.Code,"CT-e / OS"),new Source(NfeSources.SantaCatarinaNfce,"NFC-e SC"),new Source(NfeSources.SantaCatarinaNfcom,"NFCom SC")};SourceBox.SelectedIndex=0;
  FirstLoaded+=async(_,_)=>await Execute(async()=>{await using var scope=runtime.Services.CreateAsyncScope();CompanyBox.ItemsSource=await scope.ServiceProvider.GetRequiredService<WgsDbContext>().Companies.AsNoTracking().OrderBy(x=>x.LegalName).Select(x=>new CompanyOption(x.Id,x.LegalName+" · "+x.Cnpj+(x.IsActive?"":" (inativa)"))).ToListAsync();await Refresh();statusTimer.Start();});
  statusTimer.Tick+=(_,_)=>SessionStatus();Closed+=(_,_)=>statusTimer.Stop();Closing+=(_,e)=>{if(busy){e.Cancel=true;Message.Text="Aguarde a operação terminar antes de fechar.";}};
 }
 private Task<T> Use<T>(Func<IAutomationService,Task<T>> work)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await work(scope.ServiceProvider.GetRequiredService<IAutomationService>());});
 private async Task Execute(Func<Task> action)
 {if(busy)return;busy=true;Editor.IsEnabled=false;SchedulesGrid.IsEnabled=false;try{await action();}catch(AutomationException e){Message.Text=e.Message;}catch{Message.Text="Não foi possível concluir. Atualize a lista e confira a pasta de dados.";}finally{busy=false;Editor.IsEnabled=true;SchedulesGrid.IsEnabled=true;SessionStatus();}}
 private void SessionStatus()=>SessionText.Text=runner.IsArmed?"Automação habilitada nesta sessão":"Automação pausada nesta sessão"+(runner.LastError is null?"":" · "+AutomationMessages.Describe(runner.LastError));
 private async Task Refresh(){editing=null;EditText.Text="Novo agendamento";SchedulesGrid.ItemsSource=await Use(s=>s.ListAsync());RunsGrid.ItemsSource=await Use(s=>s.RunsAsync());SessionStatus();}
 private void Source_Changed(object sender,SelectionChangedEventArgs e)
 {if(ActorBox is null)return;bool cte=SourceBox.SelectedValue as string==CteSource.Code;bool national=SourceBox.SelectedValue as string==NfeSources.National;ActorBox.IsEnabled=!cte&&!national;PayloadBox.IsEnabled=!cte&&!national;if(cte||national){ActorBox.SelectedIndex=2;PayloadBox.SelectedIndex=0;}CteBox.IsEnabled=cte;OsBox.IsEnabled=cte;bool sc=NfeSources.IsSantaCatarina(SourceBox.SelectedValue as string??"");EnvironmentBox.IsEnabled=!sc;if(sc)EnvironmentBox.SelectedIndex=0;}
 private void Selection_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(SchedulesGrid.SelectedItem is not ScheduleRow row)return;editing=row;var i=row.Settings;CompanyBox.SelectedValue=i.CompanyId;SourceBox.SelectedValue=i.ProviderCode;EnvironmentBox.SelectedIndex=0;TimeBox.Text=$"{i.Hour:D2}:{i.Minute:D2}";ZoneBox.SelectedIndex=i.TimeZoneId=="UTC"?1:0;ActorBox.SelectedIndex=i.Actor==ActorFilter.Issuer?0:i.Actor==ActorFilter.Recipient?1:2;PayloadBox.SelectedIndex=i.PayloadMode==PayloadMode.FullXml?0:1;CteBox.IsChecked=i.Models.HasFlag(EnabledDocuments.CTe);OsBox.IsChecked=i.Models.HasFlag(EnabledDocuments.CTeOS);PagesBox.Text=i.MaxPages.ToString(CultureInfo.InvariantCulture);EnabledBox.IsChecked=i.Enabled;EditText.Text="Editando horário selecionado";
 }
 private void New_Click(object sender,RoutedEventArgs e){if(busy)return;editing=null;SchedulesGrid.SelectedItem=null;EnabledBox.IsChecked=false;EditText.Text="Novo agendamento (salvar para criar)";}
 private async void Save_Click(object sender,RoutedEventArgs e)=>await Execute(async()=>
 {
  if(CompanyBox.SelectedValue is not Guid company||SourceBox.SelectedValue is not string source||!TimeOnly.TryParseExact(TimeBox.Text,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time)||!int.TryParse(PagesBox.Text,out int pages))throw new AutomationException("SCHEDULE_INVALID");
  bool cte=source==CteSource.Code;var models=(CteBox.IsChecked==true?EnabledDocuments.CTe:0)|(OsBox.IsChecked==true?EnabledDocuments.CTeOS:0);
  var input=new ScheduleInput(company,source,FiscalEnvironment.Production,time.Hour,time.Minute,ZoneBox.SelectedIndex==1?"UTC":"America/Sao_Paulo",cte?ActorFilter.Either:ActorBox.SelectedIndex switch{0=>ActorFilter.Issuer,1=>ActorFilter.Recipient,_=>ActorFilter.Either},cte||PayloadBox.SelectedIndex==0?PayloadMode.FullXml:PayloadMode.KeysOnly,models,pages,EnabledBox.IsChecked==true);
  var id=editing?.Id;var revision=editing?.Revision;await Use(s=>s.SaveAsync(input,id,revision));editing=null;await Refresh();EditText.Text="Novo agendamento";Message.Text="Agendamento salvo. Para outro horário, crie um novo. Habilite a sessão para executar.";
 });
 private async void Disable_Click(object sender,RoutedEventArgs e)=>await Execute(async()=>{if(SchedulesGrid.SelectedItem is not ScheduleRow row)throw new AutomationException("SCHEDULE_MISSING");await Use(s=>s.SaveAsync(row.Settings with{Enabled=false},row.Id,row.Revision));editing=null;await Refresh();Message.Text="Agendamento pausado. O histórico foi preservado.";});
 private async void Delete_Click(object sender,RoutedEventArgs e)=>await Execute(async()=>{if(SchedulesGrid.SelectedItem is not ScheduleRow row)throw new AutomationException("SCHEDULE_MISSING");if(MessageBox.Show(Owner,"Excluir este agendamento sem histórico?","Excluir agendamento",MessageBoxButton.YesNo,MessageBoxImage.Question,MessageBoxResult.No)!=MessageBoxResult.Yes)return;await Use(async s=>{await s.DeleteAsync(row.Id,row.Revision);return true;});editing=null;await Refresh();});
 private async void Arm_Click(object sender,RoutedEventArgs e)=>await Execute(async()=>{if(Consent.IsChecked!=true){Message.Text="Marque a autorização de consultas automáticas para habilitar esta sessão.";return;}await runner.ArmAsync();Message.Text="Automação habilitada. Horários vencidos serão consolidados em uma execução, sem repetir cada dia perdido.";});
 private async void Pause_Click(object sender,RoutedEventArgs e)
 {try{await runner.PauseAsync();Consent.IsChecked=false;if(!busy)await Execute(Refresh);Message.Text="Automação pausada. Recebimentos confirmados preservados; rede oficial desabilitada na sessão.";}catch{Message.Text="Confira o encerramento da operação antes de fechar o aplicativo.";}SessionStatus();}
 private async void Refresh_Click(object sender,RoutedEventArgs e)=>await Execute(Refresh);
}
