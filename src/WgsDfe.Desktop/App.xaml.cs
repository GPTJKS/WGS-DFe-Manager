using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class App : System.Windows.Application
{
 private FoundationRuntime? runtime;
 protected override async void OnStartup(StartupEventArgs e)
 {
  System.Globalization.CultureInfo.DefaultThreadCurrentCulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
  System.Globalization.CultureInfo.DefaultThreadCurrentUICulture=System.Globalization.CultureInfo.GetCultureInfo("pt-BR");
  DateInput.Register();base.OnStartup(e);
  try
  {
   var options=FoundationOptions.Load(System.IO.Path.Combine(AppContext.BaseDirectory,"appsettings.json"));
   if(e.Args.Length==2&&e.Args[0]=="--restore")await Task.Run(()=>BackupService.RestoreOfflineAsync(options.ResolveDataDirectory(),options.Application.OrganizationId,e.Args[1]));
   options.LockFileName="instance-17.0.3.lock";
   runtime=new FoundationRuntime(options);
   var status=await Task.Run(()=>runtime.InitializeAsync());
   new MainWindow(status,runtime).Show();
  }
  catch
  {
   try {runtime?.Services.GetRequiredService<ITechnicalLog>().Write(TechnicalLevel.Error,TechnicalEvent.StartupFailed,runtime.CorrelationId);} catch { /* Falha do disco não pode ocultar mensagem. */ }
   MessageBox.Show("Não foi possível iniciar o WGS DF-e Manager 0.17.5. Verifique a configuração, a permissão da pasta de dados e se o programa já está aberto. Nenhum documento foi apagado.","WGS DF-e Manager 0.17.5",MessageBoxButton.OK,MessageBoxImage.Error);
   if(runtime is not null) {await runtime.DisposeAsync(); runtime=null;}
   Shutdown(1);
  }
 }
 protected override void OnExit(ExitEventArgs e)
 {
  if(runtime is not null) runtime.DisposeAsync().AsTask().GetAwaiter().GetResult();
  base.OnExit(e);
 }
}
