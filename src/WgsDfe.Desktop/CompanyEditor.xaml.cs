using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using WgsDfe.Application.Certificates;
using WgsDfe.Infrastructure.Certificates;
using WgsDfe.Infrastructure.Companies;
using System.Windows;
using WgsDfe.Application.Companies;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
public partial class CompanyEditor : WorkspaceView
{
 private readonly Func<CompanyInput,ReadOnlyMemory<byte>,ReadOnlyMemory<char>,Task> save;
 private readonly CompanyLookup lookup;private bool saving;private byte[]? pendingPfx;private string fileName="";
 private readonly string originalCertificateStatus;
 public CompanyEditor(CompanyRow? row,CompanyLookup lookup,string certificateStatus,Func<CompanyInput,ReadOnlyMemory<byte>,ReadOnlyMemory<char>,Task> save)
 {
  InitializeComponent();WindowLayout.Attach(this);this.save=save;this.lookup=lookup;originalCertificateStatus=certificateStatus;CertificateStatus.Text=certificateStatus;StateBox.ItemsSource=CompanyValidation.States;
  var data=row?.Data??new CompanyInput("",null,"",null,"SC",null,true,EnabledDocuments.All,null,null);
  LegalNameBox.Text=data.LegalName;TradeNameBox.Text=data.TradeName;CnpjBox.Text=data.Cnpj;RegistrationBox.Text=data.StateRegistration;StateBox.SelectedItem=data.State;CityBox.Text=data.City;ActiveBox.IsChecked=data.IsActive;StorageBox.Text=data.StorageRelativePath;NotesBox.Text=data.Notes;
  NfeBox.IsChecked=data.EnabledDocuments.HasFlag(EnabledDocuments.NFe);NfceBox.IsChecked=data.EnabledDocuments.HasFlag(EnabledDocuments.NFCe);CteBox.IsChecked=data.EnabledDocuments.HasFlag(EnabledDocuments.CTe);CteOsBox.IsChecked=data.EnabledDocuments.HasFlag(EnabledDocuments.CTeOS);NfcomBox.IsChecked=data.EnabledDocuments.HasFlag(EnabledDocuments.NFCom);
  MetadataText.Text=$"Última sincronização: {(row?.LastSynchronizationAtUtc is DateTime date?date.ToLocalTime().ToString("dd/MM/yyyy HH:mm"):"Ainda não realizada")}";
  Form.AddHandler(System.Windows.Controls.TextBox.TextChangedEvent,new System.Windows.Controls.TextChangedEventHandler((_,_)=>HasUnsavedChanges=true));Form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.CheckedEvent,new RoutedEventHandler((_,_)=>HasUnsavedChanges=true));Form.AddHandler(System.Windows.Controls.Primitives.ToggleButton.UncheckedEvent,new RoutedEventHandler((_,_)=>HasUnsavedChanges=true));StateBox.SelectionChanged+=(_,_)=>HasUnsavedChanges=true;
  Closing+=(_,e)=>{if(saving)e.Cancel=true;};
  Closed+=(_,_)=>{ClearPending();CertificatePassword.Clear();};
 }
 private void Cancel_Click(object sender,RoutedEventArgs e)=>Close();
 private async void Save_Click(object sender,RoutedEventArgs e)
 {
  if(saving)return;ErrorText.Text="";
  var documents=EnabledDocuments.None;
  if(NfeBox.IsChecked==true)documents|=EnabledDocuments.NFe;if(NfceBox.IsChecked==true)documents|=EnabledDocuments.NFCe;if(CteBox.IsChecked==true)documents|=EnabledDocuments.CTe;if(CteOsBox.IsChecked==true)documents|=EnabledDocuments.CTeOS;if(NfcomBox.IsChecked==true)documents|=EnabledDocuments.NFCom;
  var input=new CompanyInput(LegalNameBox.Text,TradeNameBox.Text,CnpjBox.Text,RegistrationBox.Text,StateBox.SelectedItem as string??"",CityBox.Text,ActiveBox.IsChecked==true,documents,StorageBox.Text,NotesBox.Text);
  saving=true;Form.IsEnabled=false;SaveButton.IsEnabled=false;CancelButton.IsEnabled=false;
  var success=false;
  char[] password=ReadPassword();
  try{await save(input,pendingPfx??ReadOnlyMemory<byte>.Empty,password);success=true;}
  catch(CertificateException ex){ErrorText.Text=ex.Message;}
  catch(CompanyException ex){ErrorText.Text=ex.Message;}
  catch{ErrorText.Text="Não foi possível salvar o cadastro. Verifique o acesso à pasta de dados e tente novamente.";}
  finally{CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));saving=false;Form.IsEnabled=true;SaveButton.IsEnabled=true;CancelButton.IsEnabled=true;}
  if(success)DialogResult=true;
 }
 private char[] ReadPassword()
 {
  using var secure=CertificatePassword.SecurePassword;var pointer=Marshal.SecureStringToGlobalAllocUnicode(secure);
  try{var chars=new char[secure.Length];Marshal.Copy(pointer,chars,0,chars.Length);return chars;}finally{Marshal.ZeroFreeGlobalAllocUnicode(pointer);}
 }
 private void SetBusy(bool value){saving=value;Form.IsEnabled=SaveButton.IsEnabled=CancelButton.IsEnabled=!value;}
 private void ClearPending(){if(pendingPfx is not null)CryptographicOperations.ZeroMemory(pendingPfx);pendingPfx=null;fileName="";}
 private void Cnpj_Changed(object sender,System.Windows.Controls.TextChangedEventArgs e)
 {
  if(pendingPfx is not null&&CertificateStatus is not null)CertificateStatus.Text="CNPJ alterado. O certificado será conferido novamente ao salvar.";
 }
 private void Password_Changed(object sender,RoutedEventArgs e)
 {
  if(pendingPfx is not null){HasUnsavedChanges=true;CertificateStatus.Text=fileName+" — valide a senha e o CNPJ antes de salvar.";}
 }
 private async void Lookup_Click(object sender,RoutedEventArgs e)
 {
  if(saving)return;ErrorText.Text="";
  if(!Cnpj.TryNormalize(CnpjBox.Text,out var id)){ErrorText.Text="Informe um CNPJ válido.";return;}
  if((!string.IsNullOrWhiteSpace(LegalNameBox.Text)||!string.IsNullOrWhiteSpace(TradeNameBox.Text)||!string.IsNullOrWhiteSpace(CityBox.Text))&&MessageBox.Show(Owner,"Preencher razão social, nome fantasia, UF e município com os dados da consulta? Os demais campos serão preservados.","Consultar CNPJ",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  SetBusy(true);LookupStatus.Text="Consultando dados públicos…";
  try{var result=await lookup.FindAsync(id);LegalNameBox.Text=result.LegalName;TradeNameBox.Text=result.TradeName??"";StateBox.SelectedItem=result.State;CityBox.Text=result.City??"";CnpjBox.Text=Cnpj.Format(result.Cnpj);LookupStatus.Text=$"Dados preenchidos via {result.Source}{(result.Cached?" (consulta recente reutilizada)":"")}. Confira antes de salvar. Inscrição estadual: preenchimento manual.";}
  catch(CompanyException ex){ErrorText.Text=ex.Message;LookupStatus.Text="Consulta não concluída. Seus campos foram preservados.";}
  catch{ErrorText.Text="Não foi possível consultar. Você pode preencher manualmente.";LookupStatus.Text="Consulta não concluída.";}
  finally{SetBusy(false);}
 }
 private async void SelectCertificate_Click(object sender,RoutedEventArgs e)
 {
  if(saving)return;ErrorText.Text="";
  if(!Cnpj.TryNormalize(CnpjBox.Text,out _)){ErrorText.Text="Preencha um CNPJ válido antes de selecionar o certificado.";return;}
  var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Certificado A1 (*.pfx;*.p12)|*.pfx;*.p12",Multiselect=false};if(dialog.ShowDialog(Owner)!=true)return;
  SetBusy(true);byte[]? bytes=null;
  try
  {
   using var stream=new FileStream(dialog.FileName,FileMode.Open,FileAccess.Read,FileShare.Read);
   if(stream.Length is <1 or >PfxCodec.MaximumPfxBytes)throw new CertificateException(CertificateError.InvalidInput);
   bytes=new byte[(int)stream.Length];await stream.ReadExactlyAsync(bytes);ClearPending();pendingPfx=bytes;bytes=null;fileName=Path.GetFileName(dialog.FileName);HasUnsavedChanges=true;CertificatePassword.Clear();CertificateStatus.Text=fileName+" — informe a senha e clique em Validar certificado. Será importado somente ao salvar.";
  }
  catch(CertificateException ex){ErrorText.Text=ex.Message;}
  catch{ErrorText.Text="Não foi possível ler o certificado. Verifique o arquivo e tente novamente.";}
  finally{if(bytes is not null)CryptographicOperations.ZeroMemory(bytes);SetBusy(false);}
 }
 private void ClearCertificate_Click(object sender,RoutedEventArgs e){if(saving)return;ClearPending();CertificatePassword.Clear();CertificateStatus.Text=originalCertificateStatus;ErrorText.Text="";}
 private async void ValidateCertificate_Click(object sender,RoutedEventArgs e)
 {
  if(saving)return;ErrorText.Text="";var bytes=pendingPfx;if(bytes is null){ErrorText.Text="Selecione um arquivo .pfx ou .p12.";return;}
  var id=CnpjBox.Text;var password=ReadPassword();SetBusy(true);
  try
  {
   var text=await Task.Run(()=>{using var certificate=PfxCodec.Load(bytes,password);var holder=CompanyRegistrationService.ValidateCertificate(certificate,id);return $"e-CNPJ conferido: {Cnpj.Format(holder)} · Válido até {certificate.NotAfter:dd/MM/yyyy}. Pronto para salvar.";});
   CertificateStatus.Text=text;
  }
  catch(CompanyException ex){ErrorText.Text=ex.Message;CertificateStatus.Text="Certificado não validado. Confira o arquivo e a senha.";}
  catch(CertificateException ex){ErrorText.Text=ex.Message;CertificateStatus.Text="Certificado não validado. Confira o arquivo e a senha.";}
  catch{ErrorText.Text="Não foi possível validar o certificado.";}
  finally{CryptographicOperations.ZeroMemory(MemoryMarshal.AsBytes(password.AsSpan()));SetBusy(false);}
 }

}
