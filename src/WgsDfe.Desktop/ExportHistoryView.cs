using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WgsDfe.Application.Export;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public sealed class ExportHistoryView:WorkspaceView
{
 private readonly FoundationRuntime runtime;
 private readonly DataGrid grid=new(){AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false};
 private readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap};private int page=1;private bool busy;
 public ExportHistoryView(FoundationRuntime runtime)
 {
  this.runtime=runtime;Title="Histórico de exportações";var layout=new Grid{Margin=new Thickness(24)};layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
  layout.Children.Add(new TextBlock{Text="Histórico de exportações",Style=(Style)FindResource("PageTitle"),Margin=new Thickness(0,0,0,16)});
  foreach(var (header,binding,width) in new[]{("Registrada (UTC)","CreatedAtUtc","180"),("Empresas","Companies","90"),("Gravados","Written","80"),("Não gravados","Skipped","100"),("Pasta / ZIP","Output","*")})grid.Columns.Add(new DataGridTextColumn{Header=header,Binding=new Binding(binding){StringFormat=binding=="CreatedAtUtc"?"dd/MM/yyyy HH:mm":null},Width=width=="*"?new DataGridLength(1,DataGridLengthUnitType.Star):new DataGridLength(double.Parse(width))});
  Grid.SetRow(grid,1);layout.Children.Add(grid);ListSearch.Attach(grid);var footer=new StackPanel{Margin=new Thickness(0,12,0,0)};var actions=new WrapPanel();
  var previous=new Button{Content="Anterior"};previous.Click+=async(_,_)=>{if(busy||page==1)return;page--;await Load();};actions.Children.Add(previous);
  var next=new Button{Content="Próxima"};next.Click+=async(_,_)=>{if(busy)return;page++;await Load();};actions.Children.Add(next);
  var open=new Button{Content="Abrir pasta"};open.Click+=(_,_)=>{if(grid.SelectedItem is not ExportHistoryEntry row)return;try{Process.Start(new ProcessStartInfo(Directory.Exists(row.Output)?row.Output:Path.GetDirectoryName(row.Output)!){UseShellExecute=true});}catch{message.Text="A pasta não está acessível. Confira se o destino foi movido ou desconectado.";}};actions.Children.Add(open);
  footer.Children.Add(actions);footer.Children.Add(message);Grid.SetRow(footer,2);layout.Children.Add(footer);Content=layout;
  FirstLoaded+=async(_,_)=>await Load();Closing+=(_,e)=>e.Cancel=busy;
 }
 private async Task Load()
 {
  if(busy)return;busy=true;
  try
  {
   int skip=(page-1)*50;var rows=await Task.Run(()=>
   {
    var folder=Path.Combine(runtime.DataDirectory,"ExportHistory");if(!Directory.Exists(folder))return Array.Empty<ExportHistoryEntry>();
    var result=new List<ExportHistoryEntry>();foreach(var file in Directory.EnumerateFiles(folder,"*.json").OrderByDescending(Path.GetFileName).Skip(skip).Take(50))
    {try{var row=JsonSerializer.Deserialize<ExportHistoryEntry>(File.ReadAllText(file));if(row is not null)result.Add(row);}catch(Exception e)when(e is IOException or JsonException){}}
    return result.ToArray();
   });grid.ItemsSource=rows;message.Text=$"Página {page} · {rows.Length} exportação(ões). O histórico registra a publicação; arquivos externos podem ter sido movidos depois.";
  }
  catch{message.Text="Não foi possível ler o histórico.";}
  finally{busy=false;}
 }
}
