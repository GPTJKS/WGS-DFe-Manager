using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using WgsDfe.Application.Archive;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class ArchiveDetailWindow:WorkspaceView
{
 private readonly FoundationRuntime runtime;private bool busy;
 public ArchiveDetailWindow(FoundationRuntime runtime,string heading,IReadOnlyList<ArchiveFileRow> versions,IReadOnlyList<ArchiveEventRow> events)
 {
  InitializeComponent();
  PreferredDialogSize=new Size(1060,650);
  WindowLayout.Attach(this);
  this.runtime=runtime;
  Heading.Text=heading;
  Versions.ItemsSource=versions;
  Events.ItemsSource=events;
  if(versions.Count>0)Versions.SelectedItem=versions.FirstOrDefault(x=>x.IsPrimary)??versions[0];
  Versions.SelectionChanged+=async(_,_)=>{if(IsLoaded&&!busy&&Versions.SelectedItem is ArchiveFileRow)await LoadSelectedVersionAsync();};
  FirstLoaded+=async(_,_)=>{if(Versions.SelectedItem is ArchiveFileRow)await LoadSelectedVersionAsync();};
  Closing+=(_,e)=>{if(busy)e.Cancel=true;};
 }
 private async Task Run(Func<Task> action)
 {if(busy)return;busy=true;VisualButton.IsEnabled=DownloadXmlButton.IsEnabled=ViewButton.IsEnabled=EventButton.IsEnabled=Versions.IsEnabled=Events.IsEnabled=false;try{await action();}catch(ArchiveException e){Message.Text=e.Message;}catch{Message.Text="Não foi possível ler o arquivo do acervo.";}finally{busy=false;VisualButton.IsEnabled=DownloadXmlButton.IsEnabled=ViewButton.IsEnabled=EventButton.IsEnabled=Versions.IsEnabled=Events.IsEnabled=true;}}
 private Task<T> Service<T>(Func<IArchiveService,Task<T>> action)=>Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<IArchiveService>());});
 private async Task LoadSelectedVersionAsync()
 {
  if(Versions.SelectedItem is not ArchiveFileRow file){Message.Text="Selecione uma versão na primeira aba.";return;}
  Xml.Clear();
  await Run(async()=>
  {
   var result=await Service(s=>s.ReadXmlAsync(file.Id));
   Xml.Text=result.Text;
   DanfeViewer.LoadXml(result.Text);
   Message.Text=$"SHA-256 conferido: {result.Hash}. Espelho gráfico e XML carregados; bytes originais preservados."+(result.PreviewTruncated?" Prévia limitada a 200.000 caracteres.":"");
  });
 }
 private async void Visual_Click(object sender,RoutedEventArgs e)
 {
  ViewerTabs.SelectedIndex=0;
  await LoadSelectedVersionAsync();
 }
 private async void View_Click(object sender,RoutedEventArgs e)
 {
  ViewerTabs.SelectedIndex=1;
  await LoadSelectedVersionAsync();
 }
 private async void DownloadXml_Click(object sender,RoutedEventArgs e)=>await DownloadSelectedXmlAsync();
 public async Task<bool> DownloadSelectedXmlAsync(string? destinationPath=null)
 {
  if(Versions.SelectedItem is not ArchiveFileRow file)
  {
   const string unavailable="O XML desta nota não está disponível ou nenhuma versão foi selecionada no acervo.";
   Message.Text=unavailable;
   if(destinationPath is null)MessageBox.Show(unavailable,"Baixar XML",MessageBoxButton.OK,MessageBoxImage.Warning);
   return false;
  }
  ArchiveOriginalXml? original=null;
  await Run(async()=>
  {
   original=await Service(s=>s.ReadOriginalXmlAsync(file.Id));
  });
  if(original is null||original.Bytes.Length==0)
  {
   if(string.IsNullOrWhiteSpace(Message.Text))Message.Text="Não foi possível obter o XML original desta nota.";
   if(destinationPath is null)MessageBox.Show(Message.Text,"Baixar XML",MessageBoxButton.OK,MessageBoxImage.Warning);
   return false;
  }
  string? targetPath=destinationPath;
  if(string.IsNullOrWhiteSpace(targetPath))
  {
   var dialog=new SaveFileDialog
   {
    Title="Baixar XML original da nota fiscal",
    Filter="Arquivo XML (*.xml)|*.xml",
    DefaultExt=".xml",
    FileName=original.FileName
   };
   if(dialog.ShowDialog(Window.GetWindow(this))!=true)return false;
   targetPath=dialog.FileName;
  }
  try
  {
   string? dir=Path.GetDirectoryName(targetPath);
   if(!string.IsNullOrWhiteSpace(dir))Directory.CreateDirectory(dir);
   await File.WriteAllBytesAsync(targetPath,original.Bytes);
   Message.Text=$"XML original baixado com sucesso: {Path.GetFileName(targetPath)} ({original.Bytes.Length:N0} bytes · SHA-256: {original.Hash}).";
   return true;
  }
  catch
  {
   const string saveError="Não foi possível salvar o arquivo XML no destino selecionado.";
   Message.Text=saveError;
   if(destinationPath is null)MessageBox.Show(saveError,"Baixar XML",MessageBoxButton.OK,MessageBoxImage.Error);
   return false;
  }
 }
 private async void Event_Click(object sender,RoutedEventArgs e)
 {
  if(Events.SelectedItem is not ArchiveEventRow ev){Message.Text="Selecione um evento na segunda aba.";return;}
  Xml.Clear();
  await Run(async()=>
  {
   var files=await Service(s=>s.EventFilesAsync(ev.Id));
   Versions.ItemsSource=files;
   if(files.Count>0)Versions.SelectedIndex=0;
   Message.Text="Versões do evento carregadas na primeira aba.";
  });
  if(Versions.SelectedItem is ArchiveFileRow)await LoadSelectedVersionAsync();
 }
}
