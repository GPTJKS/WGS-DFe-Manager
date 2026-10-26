using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;
namespace WgsDfe.Desktop;
public sealed class SettingsView:WorkspaceView
{
 private readonly FoundationRuntime runtime;
 private readonly TextBlock message=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,12)};
 private readonly TextBlock storage=new(){TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,12)};
 private readonly ComboBox retention=new(){ItemsSource=new[]{"Manter cópias locais","30 dias após exportação","60 dias após exportação","90 dias após exportação"},Width=260,HorizontalAlignment=HorizontalAlignment.Left};
 private readonly TextBox orgNameBox=new(){Width=320,MaxLength=120,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,4,12,4)};
 private readonly TextBox orgCnpjBox=new(){Width=190,MaxLength=25,Padding=new Thickness(8,5,8,5),Margin=new Thickness(0,4,12,4)};
 private bool busy;
 public SettingsView(FoundationRuntime runtime)
 {
  this.runtime=runtime;Title="Configurações";
  var preferences=UserPreferences.Load(runtime.DataDirectory);
  orgNameBox.Text=string.IsNullOrWhiteSpace(preferences.LicensedOrganization)?"WGS Assessoria Contábil":preferences.LicensedOrganization;
  orgCnpjBox.Text=preferences.LicensedCnpj??"";

  var panel=new StackPanel{Margin=new Thickness(24)};
  var eyebrow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,2)};
  eyebrow.Children.Add(new System.Windows.Shapes.Ellipse{Width=8,Height=8,Fill=(System.Windows.Media.Brush)FindResource("GoldBrush"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,4)});
  eyebrow.Children.Add(new TextBlock{Text="PARÂMETROS DO SISTEMA · GOVERNANÇA CORPORATIVA E RETENÇÃO",Style=(Style)FindResource("Eyebrow")});
  panel.Children.Add(eyebrow);
  panel.Children.Add(new TextBlock{Text="Configurações & Ambiente Corporativo",Style=(Style)FindResource("PageTitle"),Margin=new Thickness(0,0,0,4)});
  panel.Children.Add(new TextBlock{Text="Personalização da organização licenciada, armazenamento, segurança criptográfica e rotinas operacionais.",Style=(Style)FindResource("Caption"),Margin=new Thickness(0,0,0,12)});
  var common=new WrapPanel();common.Children.Add(Action("Certificados",()=>WorkspaceNavigator.Open(new CertificateWindow(runtime))));common.Children.Add(Action("Pasta padrão de exportação",ChooseFolder));common.Children.Add(Action("Automações opcionais",()=>WorkspaceNavigator.Open(new AutomationWindow(runtime))));panel.Children.Add(common);

  var corporate=new StackPanel();
  corporate.Children.Add(Note("Personalize a identificação da organização ou escritório licenciado exibida no cabeçalho, no painel executivo e nos relatórios operacionais. Ideal para implantação em múltiplos clientes ou departamentos fiscais."));
  var corpInputs=new WrapPanel{VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,4,0,4)};
  var nameStack=new StackPanel();nameStack.Children.Add(new TextBlock{Text="Nome da Organização / Escritório Licenciado",Style=(Style)FindResource("Eyebrow")});nameStack.Children.Add(orgNameBox);corpInputs.Children.Add(nameStack);
  var cnpjStack=new StackPanel();cnpjStack.Children.Add(new TextBlock{Text="CNPJ / Identificação (opcional)",Style=(Style)FindResource("Eyebrow")});cnpjStack.Children.Add(orgCnpjBox);corpInputs.Children.Add(cnpjStack);
  var saveCorpBtn=Action("Salvar identificação corporativa",SaveCorporateIdentity);saveCorpBtn.Style=(Style)FindResource("PrimaryButton");saveCorpBtn.Margin=new Thickness(0,14,0,4);corpInputs.Children.Add(saveCorpBtn);
  corporate.Children.Add(corpInputs);
  Section("Identificação corporativa e licenciamento",corporate,true);

  var backup=new StackPanel();backup.Children.Add(Note("O backup contém banco SQLite consistente, empresas, vínculos, NSU e metadados. Os XMLs e o cofre de certificados devem ser preservados separadamente. Em outro perfil do Windows, reimporte os certificados. A restauração exige o aplicativo fechado e cria uma cópia de recuperação do banco anterior."));
  var buttons=new WrapPanel();buttons.Children.Add(Action("Criar backup",async()=>await Backup()));buttons.Children.Add(Action("Preparar restauração",PrepareRestore));backup.Children.Add(buttons);Section("Backup e restauração",backup,true);
  var space=new StackPanel();var location=new Border{Style=(Style)FindResource("Notice")};var locationBody=new StackPanel();locationBody.Children.Add(new TextBlock{Text="PASTA DE DADOS E ESPAÇO OCUPADO",Style=(Style)FindResource("Eyebrow")});locationBody.Children.Add(storage);location.Child=locationBody;space.Children.Add(location);
  var policy=new DockPanel();var policyText=new StackPanel{Margin=new Thickness(0,0,20,0)};policyText.Children.Add(new TextBlock{Text="Política de retenção de XMLs",FontWeight=FontWeights.SemiBold});policyText.Children.Add(new TextBlock{Text="Controle das cópias mantidas neste computador.",Style=(Style)FindResource("Caption")});DockPanel.SetDock(retention,Dock.Right);policy.Children.Add(retention);policy.Children.Add(policyText);space.Children.Add(new Border{Style=(Style)FindResource("Notice"),Child=policy});retention.SelectedIndex=preferences.RetentionDays switch{30=>1,60=>2,90=>3,_=>0};
  space.Children.Add(Note("A limpeza é manual e opcional. Apenas XMLs autorizados, sem eventos e sem versões alternativas, são elegíveis. A cópia exportada precisa continuar acessível e íntegra; ela será usada para abrir ou reexportar o XML. Cancelamentos, eventos, divergências, documentos não confirmados e metadados permanecem preservados."));
  var storageActions=new WrapPanel();storageActions.Children.Add(Action("Avaliar e limpar cópias locais",async()=>await Clean()));storageActions.Children.Add(Action("Atualizar espaço ocupado",async()=>await UpdateStorage()));space.Children.Add(storageActions);Section("Armazenamento e retenção",space,true);
  var advanced=new WrapPanel();advanced.Children.Add(Action("Inspecionar XML",()=>WorkspaceNavigator.Open(new DfeInspectorWindow(runtime.Services.GetRequiredService<WgsDfe.Application.Parsing.IDfeParser>()))));advanced.Children.Add(Action("Abrir pasta de dados",()=>OpenFolder(runtime.DataDirectory)));Section("Ferramentas avançadas",advanced,false);
  var help=new StackPanel();help.Children.Add(Note("Cadastre as empresas e seus certificados, consulte os lotes disponíveis e exporte os XMLs por período. Os diagnósticos explicam os prazos e orientações de cada fonte."));help.Children.Add(Action("Abrir ajuda",()=>WorkspaceNavigator.Open(new InformationView(false))));Section("Ajuda e diretrizes do sistema",help,false);panel.Children.Add(message);Content=new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Visible};
  void Section(string name,UIElement body,bool open)
  {
   var (glyph,description)=name switch{
    "Identificação corporativa e licenciamento"=>("\uE80F","Personalização do escritório ou empresa titular para uso corporativo."),
    "Backup e restauração"=>("\uE753","Gestão de integridade, NSU local e preservação dos metadados fiscais."),
    "Armazenamento e retenção"=>("\uE8B7","Pasta de dados, cópias locais e política de espaço ocupado."),
    "Ferramentas avançadas"=>("\uE713","Diagnóstico operacional e inspeção de documentos fiscais."),
    _=>("\uE897","Orientações para cadastro, consultas e exportação do acervo.")};
   var header=new DockPanel{Margin=new Thickness(0,2,14,2)};var icon=new TextBlock{Text=glyph,FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),FontSize=17,Foreground=(System.Windows.Media.Brush)FindResource("GoldBrush"),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
   header.Children.Add(new Border{Child=icon,Background=(System.Windows.Media.Brush)FindResource("PrimaryFixedBrush"),CornerRadius=new CornerRadius(11),Width=38,Height=38,Margin=new Thickness(0,0,12,0)});
   var labels=new StackPanel{VerticalAlignment=VerticalAlignment.Center};labels.Children.Add(new TextBlock{Text=name,FontWeight=FontWeights.SemiBold,FontSize=15,Foreground=(System.Windows.Media.Brush)FindResource("InkBrush")});labels.Children.Add(new TextBlock{Text=description,Style=(Style)FindResource("Caption"),Margin=new Thickness(0,2,0,0)});header.Children.Add(labels);
   panel.Children.Add(new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(20,12,20,16),Margin=new Thickness(0,12,0,0),Child=new Expander{Header=header,Content=body,IsExpanded=open}});
  }
  Loaded+=async(_,_)=>await UpdateStorage();Closing+=(_,e)=>{if(busy){e.Cancel=true;message.Text="Aguarde a conclusão da manutenção.";}};
 }
 private void SaveCorporateIdentity()
 {
  string name=string.IsNullOrWhiteSpace(orgNameBox.Text)?"WGS Assessoria Contábil":orgNameBox.Text.Trim();
  string cnpj=orgCnpjBox.Text.Trim();
  try
  {
   UserPreferences.Update(runtime.DataDirectory,p=>p with{LicensedOrganization=name,LicensedCnpj=cnpj});
   if(System.Windows.Application.Current?.MainWindow is MainWindow main)main.ApplyOrganizationIdentity(name);
   message.Text=$"Identificação corporativa atualizada para '{name}'.";
  }
  catch{message.Text="Não foi possível salvar a identificação corporativa.";}
 }
 private Border Note(string text)
 {
  var content=new DockPanel();content.Children.Add(new TextBlock{Text="\uE946",FontFamily=new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),Foreground=(System.Windows.Media.Brush)FindResource("Accent"),FontSize=16,Margin=new Thickness(0,3,12,0)});
  content.Children.Add(new TextBlock{Text=text,TextWrapping=TextWrapping.Wrap,Foreground=(System.Windows.Media.Brush)FindResource("MutedBrush"),LineHeight=20});return new(){Style=(Style)FindResource("Notice"),Child=content};
 }
 private Button Action(string label,Action action){var button=new Button{Content=label,HorizontalAlignment=HorizontalAlignment.Left};if(label is "Criar backup" or "Avaliar e limpar cópias locais")button.Style=(Style)FindResource("PrimaryButton");button.Click+=(_,_)=>{if(!busy)action();};return button;}
 private async Task Run(Func<Task> work)
 {
  if(busy)return;busy=true;message.Text="Processando…";
  try{await work();}catch(IOException){message.Text="Não foi possível acessar a pasta ou o arquivo. Confira permissões, espaço e se há outra instância aberta.";}catch(UnauthorizedAccessException){message.Text="Sem permissão de acesso. Escolha uma pasta gravável.";}catch{message.Text="A manutenção não foi concluída. Os controles fiscais foram preservados. Confira os arquivos e tente novamente.";}
  finally{busy=false;}
 }
 private void ChooseFolder()
 {
  var picker=new Microsoft.Win32.OpenFolderDialog{Title="Pasta padrão de exportação"};if(picker.ShowDialog(Owner)!=true)return;
  try{UserPreferences.Update(runtime.DataDirectory,p=>p with{ExportDirectory=picker.FolderName});message.Text="Pasta padrão salva.";}catch{message.Text="Não foi possível salvar a preferência.";}
 }
 private async Task Backup()
 {
  var picker=new Microsoft.Win32.SaveFileDialog{Filter="Backup WGS (*.wgsbackup)|*.wgsbackup",FileName=$"WGS_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.wgsbackup"};if(picker.ShowDialog(Owner)!=true)return;
  await Run(async()=>{var org=runtime.Services.GetRequiredService<IOrganizationContext>().OrganizationId;await Task.Run(()=>BackupService.CreateAsync(runtime.DataDirectory,org,picker.FileName));message.Text="Backup consistente criado. Preserve também seus XMLs e certificados originais.";});
 }
 private void PrepareRestore()
 {
  var picker=new Microsoft.Win32.OpenFileDialog{Filter="Backup WGS (*.wgsbackup)|*.wgsbackup"};if(picker.ShowDialog(Owner)!=true)return;
  message.Text="Feche o aplicativo e execute o comando abaixo. O backup será validado antes da substituição; será mantido um banco de recuperação.\n\""+Environment.ProcessPath+"\" --restore \""+picker.FileName+"\"";
 }
 private async Task Clean()
 {
  int days=retention.SelectedIndex switch{1=>30,2=>60,3=>90,_=>0};if(days==0){message.Text="Escolha 30, 60 ou 90 dias para avaliar a limpeza.";return;}
  if(MessageBox.Show(Owner,$"Remover apenas as cópias locais elegíveis exportadas há pelo menos {days} dias?\n\nConfirmo que mantenho os XMLs exportados sob minha guarda. Arquivos exportados não serão apagados. Banco, NSU, retomada, eventos, versões e documentos protegidos serão preservados.","Limpeza de cópias locais",MessageBoxButton.YesNo,MessageBoxImage.Warning,MessageBoxResult.No)!=MessageBoxResult.Yes)return;
  await Run(async()=>{UserPreferences.Update(runtime.DataDirectory,p=>p with{RetentionDays=days});var result=await Task.Run(async()=>{await using var scope=runtime.Services.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<ArchiveRetention>().CleanAsync(days);});message.Text=$"Removidos: {result.Removed} XMLs locais · {result.Bytes/1024d/1024d:N2} MiB liberados · Não elegíveis/indisponíveis entre os avaliados: {result.Protected}.";await UpdateStorage();});
 }
 private async Task UpdateStorage()
 {
  try{var size=await Task.Run(()=>Directory.EnumerateFiles(runtime.DataDirectory,"*",new EnumerationOptions{RecurseSubdirectories=true,AttributesToSkip=FileAttributes.ReparsePoint,IgnoreInaccessible=true}).Sum(f=>new FileInfo(f).Length));storage.Text=$"Pasta de dados: {runtime.DataDirectory}\nEspaço ocupado: {size/1024d/1024d:N2} MiB";}catch{storage.Text="Espaço ocupado indisponível.";}
 }
 private void OpenFolder(string path){try{Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}catch{message.Text="Não foi possível abrir a pasta.";}}
}
