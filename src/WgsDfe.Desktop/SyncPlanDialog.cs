using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Providers;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;

internal sealed class SyncPlanDialog:WorkspaceView
{
 public static async Task<int> ChooseAsync(FoundationRuntime runtime,Guid[] companies,string[] sources,FiscalEnvironment environment,ActorFilter actor,PayloadMode payload,DateTime? from,DateTime? to)
 {
  var plan=await Task.Run(async()=>
  {
   await using var scope=runtime.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();
   var capabilities=scope.ServiceProvider.GetRequiredService<IProviderRegistry>().Providers.Where(x=>sources.Contains(x.Capabilities.Code)).Select(x=>x.Capabilities).ToArray();
   var names=await db.Companies.AsNoTracking().Where(x=>companies.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.LegalName);
   var states=await db.DocumentSyncStates.AsNoTracking().Where(x=>companies.Contains(x.CompanyId)&&sources.Contains(x.ProviderCode)&&x.Environment==environment&&x.Actor==actor&&x.PayloadMode==payload).ToListAsync();
   return (Remote:capabilities.Length==sources.Length&&capabilities.All(x=>x.SupportsDateRange),Rows:companies.SelectMany(id=>sources.Select(source=>{var state=states.FirstOrDefault(x=>x.CompanyId==id&&x.ProviderCode==source);return $"{names.GetValueOrDefault(id,"Empresa")} · {source} · NSU salvo: {state?.LastNsu??"0"}"+(state?.NextAllowedAtUtc is DateTime next&&next>DateTime.UtcNow?$" · Próxima liberação: {next.ToLocalTime():dd/MM HH:mm:ss}":"");})).ToArray());
  });
  var dialog=new SyncPlanDialog(plan.Remote,plan.Rows,from,to);var completion=new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
  dialog.Closed+=(_,_)=>completion.TrySetResult(dialog.DialogResult==true?dialog.choice:0);WorkspaceNavigator.Open(dialog);return await completion.Task;
 }
 private int choice;
 private SyncPlanDialog(bool remote,string[] rows,DateTime? from,DateTime? to)
 {
  Title="Conferir escopo da consulta";PreferredDialogSize=new Size(820,580);
  var root=new Grid{Margin=new Thickness(24)};root.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});
  var body=new StackPanel();body.Children.Add(new TextBlock{Text=from.HasValue||to.HasValue?$"Documentos por emissão: {from:dd/MM/yyyy} a {to:dd/MM/yyyy}":"Todo o período · filtro sem datas selecionado",Style=(Style)FindResource("SectionTitle")});
  body.Children.Add(new Border{Style=(Style)FindResource("Notice"),Child=new TextBlock{Text=remote?"A fonte aceita período remoto. Serão enviadas as datas selecionadas.":"Esta fonte distribui por NSU e não aceita período remoto. Para obter as notas do intervalo, é necessário sincronizar as pendências a partir da posição salva. Isso pode receber XMLs de outros dias; eles serão preservados, mas a lista e a exportação por período continuam restritas às datas acima.",TextWrapping=TextWrapping.Wrap}});
  body.Children.Add(new TextBlock{Text="Posição de retomada por empresa e fonte",FontWeight=FontWeights.SemiBold,Margin=new Thickness(0,8,0,8)});
  foreach(string row in rows)body.Children.Add(new TextBlock{Text=row,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
  if(!remote)body.Children.Add(new TextBlock{Text="NSU 0 indica sincronização inicial possivelmente extensa. Não há total conhecido nem garantia de ordenação por data de emissão: não é seguro saltar NSUs ou parar somente por encontrar uma data posterior. Recebidos, dentro e fora do período serão informados separadamente.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)});
  root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  var actions=new WrapPanel{Margin=new Thickness(0,16,0,0)};
  var cancel=new Button{Content="Cancelar consulta"};cancel.Click+=(_,_)=>DialogResult=false;actions.Children.Add(cancel);
  var local=new Button{Content="Somente filtrar acervo"};local.Click+=(_,_)=>{choice=1;DialogResult=true;};actions.Children.Add(local);
  var run=new Button{Content=remote?"Consultar período na fonte":"Sincronizar pendências e filtrar período",Style=(Style)FindResource("PrimaryButton")};run.Click+=(_,_)=>{choice=2;DialogResult=true;};actions.Children.Add(run);
  Grid.SetRow(actions,1);root.Children.Add(actions);Content=root;
 }
}
