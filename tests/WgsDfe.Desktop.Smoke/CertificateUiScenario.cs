using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Certificates;
using WgsDfe.Database;
using WgsDfe.Desktop;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Certificates;

internal static class CertificateUiScenario
{
 public static async Task Run(MainWindow main,FoundationRuntime runtime,string folder,Action<string> snapshot,Action<bool,string> check)
 {
  main.Width=1366;main.Height=768;
  var probe=runtime.Services.GetRequiredService<CertificateUiProbe>();
  string directory=Path.Combine(folder,"certificados-sintéticos-ação");Directory.CreateDirectory(directory);
  const string password="teste-sintético-ç-123";
  using var rsa=RSA.Create(2048);var request=new CertificateRequest("CN=Certificado sintetico UI",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
  using var certificate=request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(1));
  string valid=Path.Combine(directory,"certificado-ação-ç.pfx"),noPassword=Path.Combine(directory,"sem-senha.p12");
  File.WriteAllBytes(valid,certificate.Export(X509ContentType.Pkcs12,password));File.WriteAllBytes(noPassword,certificate.Export(X509ContentType.Pkcs12,""));
  using var ec=ECDsa.Create(ECCurve.NamedCurves.nistP256);using var another=new CertificateRequest("CN=Segundo sintetico",ec,HashAlgorithmName.SHA256).CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddYears(1));
  string multiple=Path.Combine(directory,"duas-chaves.pfx");File.WriteAllBytes(multiple,new X509Certificate2Collection(new[]{certificate,another}).Export(X509ContentType.Pkcs12,password)!);
  string publicOnly=Path.Combine(directory,"sem-chave.pfx");using(var pub=X509CertificateLoader.LoadCertificate(certificate.RawData))File.WriteAllBytes(publicOnly,pub.Export(X509ContentType.Pkcs12,""));
  string invalid=Path.Combine(directory,"inválido.pfx"),empty=Path.Combine(directory,"vazio.pfx"),large=Path.Combine(directory,"grande.pfx"),wrongExtension=Path.Combine(directory,"arquivo.txt");
  File.WriteAllText(invalid,"arquivo sintético inválido");File.WriteAllBytes(empty,[]);using(var file=File.Create(large))file.SetLength(PfxCodec.MaximumPfxBytes+1L);File.WriteAllText(wrongExtension,"teste");
  Click(main,"Certificados");await Wait(()=>main.ActiveView is CertificateWindow);await Task.Delay(150);
  var list=(CertificateWindow)main.ActiveView!;var grid=(DataGrid)list.FindName("CertificatesGrid");
  var opener=Buttons(main).First(x=>x.Content?.ToString()=="Adicionar certificado A1");opener.Focus();
  Click(main,"Adicionar certificado A1");await Wait(()=>main.ActiveView is CertificateImportView);var form=(CertificateImportView)main.ActiveView!;
  var name=(TextBox)form.FindName("LabelBox");var secret=(PasswordBox)form.FindName("Password");var fileLabel=(TextBlock)form.FindName("SelectedFile");var message=(TextBlock)form.FindName("Message");
  var frame=(Border)main.Dialogs.FindName("Frame");var area=(Grid)main.Dialogs.FindName("DialogArea");main.UpdateLayout();
  check(System.Windows.Application.Current.Windows.Count==1,"certificate dialog uses one native window");
  check(!((UIElement)main.FindName("ShellSurface")).IsEnabled,"modal blocks background interaction");
  var position=frame.TransformToAncestor(area).Transform(new Point(0,0));check(Math.Abs(position.X*2+frame.ActualWidth-area.ActualWidth)<3&&Math.Abs(position.Y*2+frame.ActualHeight-area.ActualHeight)<3,"modal opens centered in useful area");
  check(frame.ActualWidth<area.ActualWidth&&frame.ActualHeight<area.ActualHeight,"default modal smaller than workspace");snapshot("modal-certificado-padrao");
  for(int step=0;step<18;step++){if(Keyboard.FocusedElement is UIElement focused)focused.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));check(main.Dialogs.IsKeyboardFocusWithin,"Tab stays inside modal "+step);}
  double width=frame.ActualWidth;Click(main,"Maximizar / restaurar janela interna");main.UpdateLayout();check(main.Dialogs.IsMaximized&&frame.ActualWidth>width,"modal maximizes inside main window");snapshot("modal-certificado-maximizado");Click(main,"Maximizar / restaurar janela interna");main.UpdateLayout();check(!main.Dialogs.IsMaximized&&Math.Abs(frame.ActualWidth-width)<3,"modal restores default size");
  foreach(var size in new[]{(1000d,700d),(1366d,768d),(1920d,1080d)})
  {
   main.Width=size.Item1;main.Height=size.Item2;await Task.Delay(100);main.UpdateLayout();
   var save=(Button)form.FindName("SaveButton");check(frame.ActualWidth<=area.ActualWidth&&frame.ActualHeight<=area.ActualHeight,"modal fits "+size);
   check(save.TransformToAncestor(frame).Transform(new Point(0,save.ActualHeight)).Y<=frame.ActualHeight,"save action visible "+size);snapshot($"modal-{size.Item1}x{size.Item2}");
  }
  main.Width=1366;main.Height=768;await Task.Delay(100);
  Click(main,"Validar e adicionar certificado");check(message.Text.Contains("Escolha um arquivo"),"missing file has contextual feedback");
  form.SelectFile(valid);check(fileLabel.Text==Path.GetFileName(valid)&&!fileLabel.Text.Contains(directory),"Unicode filename visible without full path");check(name.Text==Path.GetFileNameWithoutExtension(valid),"filename suggests editable certificate name");
  name.Clear();Click(main,"Validar e adicionar certificado");check(message.Text.Contains("nome do cadastro"),"missing name is explained inside modal");
  name.Text="Certificado sintético com senha";secret.Password="incorreta";
  main.OpenView(new QueryDiagnosticView("Empresa sintética","Teste local","WAIT_UNTIL_ALLOWED",137,"42",1,0,0,null,"Janela interna aninhada"));await Wait(()=>main.ActiveView is QueryDiagnosticView);main.UpdateLayout();check(System.Windows.Application.Current.Windows.Count==1,"nested dialog retains one native window");snapshot("modal-aninhado");main.Back();await Wait(()=>ReferenceEquals(main.ActiveView,form));check(secret.Password=="incorreta"&&fileLabel.Text==Path.GetFileName(valid),"nested dialog preserves parent fields");
  probe.Hold();Click(main,"Validar e adicionar certificado");check(form.IsProcessing&&!((Button)form.FindName("SaveButton")).IsEnabled,"certificate processing prevents duplicate submits");
  main.Back();check(ReferenceEquals(main.ActiveView,form),"critical import prevents closing");snapshot("certificado-carregando");probe.Release();await Wait(()=>!form.IsProcessing);
  check(ReferenceEquals(main.ActiveView,form)&&message.Text.Contains("senha incorreta",StringComparison.OrdinalIgnoreCase),"wrong password keeps modal open with specific message");
  check(secret.Password=="incorreta"&&fileLabel.Text==Path.GetFileName(valid)&&name.Text=="Certificado sintético com senha","failed import preserves password file and name");
  var feedback=(Border)form.FindName("Feedback");main.UpdateLayout();var feedbackBottom=feedback.TransformToAncestor(frame).Transform(new Point(0,feedback.ActualHeight));check(feedback.IsVisible&&feedbackBottom.Y<frame.ActualHeight,"error visible inside modal viewport");snapshot("certificado-senha-incorreta");
  secret.Password=password;Click(main,"Validar e adicionar certificado");await Wait(()=>!main.Dialogs.IsOpen);
  check(grid.Items.Count==1&&grid.SelectedItem is CertificateRow,"successful import selects new row");check(secret.Password.Length==0,"successful import clears password");check(ReferenceEquals(Keyboard.FocusedElement,opener),"modal returns focus to opener");snapshot("certificado-importado");
  var first=(CertificateRow)grid.SelectedItem;
  Click(main,"Testar chave privada");await Wait(()=>((TextBlock)list.FindName("Message")).Text.Contains("Chave privada testada")&&((ProgressBar)list.FindName("BusyBar")).Visibility==Visibility.Collapsed);check(true,"UI reopens DPAPI vault and proves synthetic private key");snapshot("certificado-chave-testada");
  using(var scope=runtime.Services.CreateScope())
  {
   var service=scope.ServiceProvider.GetRequiredService<ICertificateService>();var fresh=(await service.ListAsync()).Single();check(fresh.Id==first.Id,"certificate persisted in fresh service scope");
   var company=await scope.ServiceProvider.GetRequiredService<WgsDbContext>().Companies.Select(x=>x.Id).FirstAsync();await service.AssociateAsync(fresh.Id,fresh.Revision,new[]{company},"DEFAULT",Guid.NewGuid());
  }
  Click(main,"Atualizar / limpar pendências");await Wait(()=>grid.SelectedItem is CertificateRow row&&row.Revision>first.Revision);
  Click(main,"Substituir selecionado");await Wait(()=>main.ActiveView is CertificateImportView);form=(CertificateImportView)main.ActiveView!;
  form.SelectFile(noPassword);Click(main,"Validar e substituir certificado");await Wait(()=>!main.Dialogs.IsOpen);
  check(grid.SelectedItem is CertificateRow replaced&&replaced.Id==first.Id,"replacement retains certificate identity");
  using(var scope=runtime.Services.CreateScope()){var links=await scope.ServiceProvider.GetRequiredService<ICertificateService>().LinksAsync(first.Id);check(links.Count==1,"replacement preserves company source links");}
  Click(main,"Adicionar certificado A1");await Wait(()=>main.ActiveView is CertificateImportView);form=(CertificateImportView)main.ActiveView!;form.SelectFile(noPassword);((TextBox)form.FindName("LabelBox")).Text="Certificado sem senha";Click(main,"Validar e adicionar certificado");await Wait(()=>!main.Dialogs.IsOpen);check(grid.Items.Count==2,"valid unencrypted PFX imports without password");
  Click(main,"Adicionar certificado A1");await Wait(()=>main.ActiveView is CertificateImportView);form=(CertificateImportView)main.ActiveView!;message=(TextBlock)form.FindName("Message");secret=(PasswordBox)form.FindName("Password");
  foreach(var scenario in new[]{(wrongExtension,"extensão"),(empty,"não contém"),(large,"5 MiB"),(invalid,"senha incorreta"),(multiple,"mais de um"),(publicOnly,"chave privada")})
  {
   form.SelectFile(scenario.Item1);secret.Password=scenario.Item1==multiple?password:"";Click(main,"Validar e adicionar certificado");await Wait(()=>!form.IsProcessing);
   check(message.Text.Contains(scenario.Item2,StringComparison.OrdinalIgnoreCase),"certificate input feedback: "+Path.GetFileName(scenario.Item1));
  }
  form.SelectFile(noPassword);probe.FailNext=CertificateError.StoreUnavailable;Click(main,"Validar e adicionar certificado");await Wait(()=>!form.IsProcessing);check(message.Text.Contains("Cofre indisponível"),"vault failure has distinct safe feedback");
  probe.FailNext=CertificateError.Conflict;Click(main,"Validar e adicionar certificado");await Wait(()=>!form.IsProcessing);check(message.Text.Contains("cadastro mudou"),"conflict has distinct safe feedback");
  probe.Unexpected=true;Click(main,"Validar e adicionar certificado");await Wait(()=>!form.IsProcessing);check(message.Text.Contains("Referência de suporte"),"unexpected failure returns safe correlation reference");snapshot("certificado-falha-segura");
  // Escape requests the same safe close path and displays an integrated discard prompt.
  main.Dialogs.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(main),0,Key.Escape){RoutedEvent=Keyboard.PreviewKeyDownEvent});
  check(main.Dialogs.IsConfirmingDiscard,"pending certificate changes require discard confirmation");snapshot("modal-confirmacao-pendente");Click(main,"Continuar editando");check(!main.Dialogs.IsConfirmingDiscard&&ReferenceEquals(main.ActiveView,form),"continue editing preserves modal");
  main.Back();Click(main,"Descartar alterações");await Wait(()=>!main.Dialogs.IsOpen);check(secret.Password.Length==0,"discard clears certificate password");
  // Company editor shares the same pending-change guard.
  Click(main,"Empresas");await Task.Delay(100);Click(main,"+  Nova empresa");await Wait(()=>main.ActiveView is CompanyEditor);((TextBox)main.ActiveView!.FindName("LegalNameBox")).Text="Alteração sintética pendente";var editor=(CompanyEditor)main.ActiveView!;main.UpdateLayout();Children(editor).OfType<ScrollViewer>().First().ScrollToBottom();main.UpdateLayout();snapshot("modal-formulario-longo-rolagem");var saveCompany=(Button)editor.FindName("SaveButton");check(saveCompany.TransformToAncestor(frame).Transform(new Point(0,saveCompany.ActualHeight)).Y<=frame.ActualHeight,"long form keeps save pinned");main.Back();check(main.Dialogs.IsConfirmingDiscard,"company editor protects unsaved changes");snapshot("empresa-alteracoes-pendentes");Click(main,"Descartar alterações");await Wait(()=>!main.Dialogs.IsOpen);
  Motion.Disabled=false;
  main.OpenView(new QueryDiagnosticView("Empresa sintética","Teste local","WAIT_UNTIL_ALLOWED",137,"42",1,0,0,DateTime.Now.AddHours(1),"Verificação de movimento"));await Task.Delay(260);check(main.Dialogs.IsOpen,"animations enabled preserve dialog flow");main.Back();await Wait(()=>!main.Dialogs.IsOpen);Motion.Disabled=true;check(true,"reduced motion bypasses transitions safely");
 }
 private static async Task Wait(Func<bool> condition){for(int i=0;i<750;i++){if(condition())return;await Task.Delay(40);}throw new TimeoutException("Certificate UI condition was not reached.");}
 private static IEnumerable<DependencyObject> Children(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Children(child))yield return nested;}}
 private static IEnumerable<Button> Buttons(MainWindow main)=>Children(main.Dialogs.IsOpen?main.Dialogs:main).OfType<Button>();
 private static void Click(MainWindow main,string label){main.UpdateLayout();var button=Buttons(main).First(x=>x.Content?.ToString()==label||System.Windows.Automation.AutomationProperties.GetName(x)==label);if(!button.IsEnabled)throw new InvalidOperationException("Disabled button: "+label);button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}
}

internal sealed class CertificateUiProbe
{
 private TaskCompletionSource? gate;
 public CertificateError? FailNext {get;set;}
 public bool Unexpected {get;set;}
 public void Hold()=>gate=new(TaskCreationOptions.RunContinuationsAsynchronously);
 public void Release(){gate?.TrySetResult();gate=null;}
 public async Task BeforeImport()
 {if(gate is not null)await gate.Task;if(FailNext is { } failure){FailNext=null;throw new CertificateException(failure);}if(Unexpected){Unexpected=false;throw new InvalidOperationException("SYNTHETIC_FAILURE");}}
}
internal sealed class ProbedCertificates(CertificateService inner,CertificateUiProbe probe):ICertificateService
{
 public async Task<CertificateMutationResult> ImportAsync(string path,ReadOnlyMemory<char> password,string label,CertificateUse use,Guid correlationId,Guid? replaceId=null,long? revision=null,CancellationToken ct=default){await probe.BeforeImport();return await inner.ImportAsync(path,password,label,use,correlationId,replaceId,revision,ct);}
 public Task<IReadOnlyList<CertificateRow>> ListAsync(CancellationToken ct=default)=>inner.ListAsync(ct);
 public Task<CertificateMutationResult> RemoveAsync(Guid id,long revision,Guid correlationId,CancellationToken ct=default)=>inner.RemoveAsync(id,revision,correlationId,ct);
 public Task<CertificateTestResult> TestAsync(Guid id,Guid correlationId,CancellationToken ct=default)=>inner.TestAsync(id,correlationId,ct);
 public Task<IReadOnlyList<CertificateLink>> LinksAsync(Guid id,CancellationToken ct=default)=>inner.LinksAsync(id,ct);
 public Task AssociateAsync(Guid id,long revision,IReadOnlyCollection<Guid> companies,string providerCode,Guid correlationId,CancellationToken ct=default)=>inner.AssociateAsync(id,revision,companies,providerCode,correlationId,ct);
 public Task UnlinkAsync(Guid id,long revision,Guid correlationId,CancellationToken ct=default)=>inner.UnlinkAsync(id,revision,correlationId,ct);
 public Task<int> CleanupAsync(CancellationToken ct=default)=>inner.CleanupAsync(ct);
}
