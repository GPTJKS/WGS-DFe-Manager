using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application;
using WgsDfe.Application.Certificates;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Certificates;
namespace WgsDfe.Desktop;
public partial class CertificateImportView : WorkspaceView
{
 private readonly FoundationRuntime runtime;
 private readonly CertificateRow? replace;
 private readonly Func<CertificateMutationResult,Task> completed;
 private string selectedPath="";private bool busy;
 public bool IsProcessing=>busy;
 public CertificateImportView(FoundationRuntime runtime,CertificateRow? replace,Func<CertificateMutationResult,Task> completed)
 {
  InitializeComponent();this.runtime=runtime;this.replace=replace;this.completed=completed;WindowLayout.Attach(this);
  if(replace is not null){Title="Substituir certificado A1";LabelBox.Text=replace.Label;UseBox.SelectedIndex=(int)replace.Use;SaveButton.Content="Validar e substituir certificado";ShowMessage("Substituição com vínculos preservados","Confira o titular do novo certificado. As associações existentes serão mantidas.",false);}
  HasUnsavedChanges=false;
  Closing+=(_,e)=>{if(busy){e.Cancel=true;ShowMessage("Validação em andamento","Aguarde a proteção e a gravação do certificado antes de fechar.",false);}};
  Closed+=(_,_)=>{Password.Clear();selectedPath="";};
 }
 private void Changed(object sender,RoutedEventArgs e){if(IsInitialized)HasUnsavedChanges=true;}
 private void Choose_Click(object sender,RoutedEventArgs e)
 {
  var picker=new Microsoft.Win32.OpenFileDialog{Title="Selecionar certificado A1",Filter="Certificado A1 (*.pfx;*.p12)|*.pfx;*.p12",Multiselect=false};
  if(picker.ShowDialog(Owner)==true)SelectFile(picker.FileName);
 }
 // Shared by the native picker and the synthetic UI harness; selecting never imports.
 public void SelectFile(string path)
 {
  if(busy)return;
  selectedPath=path;SelectedFile.Text=Path.GetFileName(path);Password.Clear();
  if(string.IsNullOrWhiteSpace(LabelBox.Text))LabelBox.Text=Path.GetFileNameWithoutExtension(path);
  HasUnsavedChanges=true;Feedback.Visibility=Visibility.Collapsed;
 }
 private void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
 private void ShowMessage(string title,string message,bool error)
 {
  FeedbackTitle.Text=title;Message.Text=message;Feedback.Background=(System.Windows.Media.Brush)FindResource(error?"ErrorSurface":"SoftBrush");
  Feedback.Visibility=Visibility.Visible;Motion.Fade(Feedback,.1,1,180);
 }
 private async void Save_Click(object sender,RoutedEventArgs e)
 {
  if(busy)return;
  if(string.IsNullOrWhiteSpace(selectedPath)){ShowMessage("Selecione um arquivo","Escolha um arquivo .pfx ou .p12 antes de continuar.",true);return;}
  if(string.IsNullOrWhiteSpace(LabelBox.Text)){ShowMessage("Informe o nome","Preencha o nome do cadastro do certificado.",true);LabelBox.Focus();return;}
  if(!new[]{".pfx",".p12"}.Contains(Path.GetExtension(selectedPath).ToLowerInvariant())){ShowMessage("Extensão não permitida","Escolha um arquivo com extensão .pfx ou .p12.",true);return;}
  try{using var file=new FileStream(selectedPath,FileMode.Open,FileAccess.Read,FileShare.Read);if(file.Length==0){ShowMessage("Arquivo vazio","O arquivo selecionado não contém um certificado.",true);return;}if(file.Length>PfxCodec.MaximumPfxBytes){ShowMessage("Arquivo muito grande","O limite para importação é de 5 MiB.",true);return;}}
  catch(FileNotFoundException){ShowMessage("Arquivo não encontrado","Selecione novamente o arquivo do certificado.",true);return;}
  catch(UnauthorizedAccessException){ShowMessage("Sem permissão de leitura","Escolha uma cópia do arquivo em uma pasta acessível neste perfil do Windows.",true);return;}
  catch(IOException){ShowMessage("Arquivo indisponível","O arquivo pode estar em uso. Confira se ele está acessível e tente novamente.",true);return;}
  busy=true;Fields.IsEnabled=false;SaveButton.IsEnabled=CancelButton.IsEnabled=false;Progress.Visibility=Visibility.Visible;
  SaveButton.Content="Validando e protegendo…";ShowMessage("Processando certificado","Validando a chave privada e protegendo o cadastro no cofre local…",false);
  Guid correlation=Guid.NewGuid();char[] chars;
  using(var secure=Password.SecurePassword){var pointer=Marshal.SecureStringToGlobalAllocUnicode(secure);try{chars=new char[secure.Length];Marshal.Copy(pointer,chars,0,chars.Length);}finally{Marshal.ZeroFreeGlobalAllocUnicode(pointer);}}
  try
  {
   var path=selectedPath;var label=LabelBox.Text;var use=(CertificateUse)UseBox.SelectedIndex;
   var result=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ICertificateService>().ImportAsync(path,chars,label,use,correlation,replace?.Id,replace?.Revision);});
   Password.Clear();HasUnsavedChanges=false;ShowMessage("Certificado salvo","Certificado validado e protegido. Atualizando a lista…",false);
   await completed(result);busy=false;DialogResult=true;
  }
  catch(CertificateException ex){ShowMessage(ex.Code==CertificateError.InvalidPfx?"Confira a senha e o arquivo":"Não foi possível importar",ex.Message,true);if(ex.Code==CertificateError.InvalidPfx)_ = Dispatcher.BeginInvoke(new Action(()=>Password.Focus()));}
  catch
  {
   try{runtime.Services.GetRequiredService<ITechnicalLog>().Write(TechnicalLevel.Error,TechnicalEvent.CertificateImportFailed,correlation);}catch{ }
   ShowMessage("Falha na gravação",$"Não foi possível concluir a proteção ou a persistência. Seus campos foram preservados. Referência de suporte: {correlation:N}.",true);
  }
  finally
  {
   CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(chars.AsSpan()));busy=false;Fields.IsEnabled=true;SaveButton.IsEnabled=CancelButton.IsEnabled=true;Progress.Visibility=Visibility.Collapsed;
   SaveButton.Content=replace is null?"Validar e adicionar certificado":"Validar e substituir certificado";
  }
 }
}
