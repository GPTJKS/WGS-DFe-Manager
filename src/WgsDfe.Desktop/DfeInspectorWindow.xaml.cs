using System.Globalization;
using System.IO;
using System.Windows;
using WgsDfe.Application.Parsing;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
public partial class DfeInspectorWindow:WorkspaceView
{
 private readonly IDfeParser parser;private bool busy;
 public DfeInspectorWindow(IDfeParser parser){InitializeComponent();PreferredDialogSize=new Size(1160,740);WindowLayout.Attach(this);this.parser=parser;Closing+=(_,e)=>{if(busy)e.Cancel=true;};}
 private static string KeyDescription(DfeAccessKey k)=>$"Chave: {k.Value}\nModelo: {k.ModelName} ({(int)k.Model})\nUF: {k.State} ({k.StateCode})\nReferência da chave: {k.Month:D2}/{k.Year}\n{(k.IsCpfIssuer?"CPF":"CNPJ")} emitente: {k.IssuerTaxId}\nSérie: {k.Series}\nNúmero: {k.Number}\nTipo de emissão: {k.EmissionType}\nSite autorizador: {k.AuthorizerSite?.ToString(CultureInfo.InvariantCulture)??"Não se aplica"}\nCódigo numérico: {k.NumericCode}\nDV: {k.CheckDigit}";
 private static string EnvironmentName(FiscalEnvironment? e)=>e switch{FiscalEnvironment.Production=>"Produção",FiscalEnvironment.Homologation=>"Homologação",_=>"Não informado no resumo"};
 private static string StatusName(DocumentStatus s)=>s switch{DocumentStatus.Authorized=>"Autorizada (declarada no XML)",DocumentStatus.Cancelled=>"Cancelada (declarada no XML)",DocumentStatus.Denied=>"Denegada (declarada no XML)",DocumentStatus.Other=>"Outra resposta",_=>"Não confirmada / não se aplica"};
 private void Key_Click(object sender,RoutedEventArgs e)
 {
  Message.Text="";Result.Text="";
  try{var key=DfeAccessKey.Parse(KeyBox.Text);Result.Text=KeyDescription(key);InspectorTabs.SelectedIndex=1;Message.Text="Estrutura e dígitos verificadores consistentes. A chave não informa o ambiente nem comprova existência/autorização.";}
  catch(AccessKeyException ex){Message.Text=ex.Message;}
 }
 private async void Xml_Click(object sender,RoutedEventArgs e)
 {
  var file=new Microsoft.Win32.OpenFileDialog{Filter="Documento XML (*.xml)|*.xml",Multiselect=false};if(file.ShowDialog(Owner)!=true)return;
  busy=true;Actions.IsEnabled=KeyBox.IsEnabled=false;BusyBar.Visibility=Visibility.Visible;Message.Text="";Result.Text="";
  try
  {
   var r=await Task.Run(()=>parser.ParseFileAsync(file.FileName));KeyBox.Text=r.Key.Value;
   var rawXml=await Task.Run(()=>File.ReadAllText(file.FileName));
   DanfeViewer.LoadXml(rawXml);
   InspectorTabs.SelectedIndex=0;
   Result.Text=$"Tipo: {r.Description}\nLeiaute: {r.LayoutVersion}\n\n{KeyDescription(r.Key)}\n\nAmbiente: {EnvironmentName(r.Environment)}\nEmitente: {r.IssuerName??"Não informado"}\nDestinatário: {r.RecipientName??"Não informado"}\nCPF/CNPJ destinatário: {r.RecipientTaxId??"Não informado"}\nEmissão: {r.IssueDate?.ToString("O",CultureInfo.InvariantCulture)??"Não informada"}\nTotal declarado: {r.Total?.ToString("N2",CultureInfo.GetCultureInfo("pt-BR"))??"Não se aplica"}\nSituação declarada: {StatusName(r.Status)}\nCódigo de resposta: {r.ResponseCode?.ToString(CultureInfo.InvariantCulture)??"Não informado"}\nProtocolo: {r.ProtocolNumber??"Não informado"}\nRecebimento: {r.ReceiptDate?.ToString("O",CultureInfo.InvariantCulture)??"Não informado"}\nEvento: {r.EventType??"Não se aplica"}\nSequência: {r.EventSequence?.ToString(CultureInfo.InvariantCulture)??"Não se aplica"}\nData do evento: {r.EventDate?.ToString("O",CultureInfo.InvariantCulture)??"Não se aplica"}\n\nSHA-256 do arquivo original:\n{r.Sha256}";
   Message.Text=r.Kind==DfePayloadKind.FullDocument?"XML lido e espelho gráfico da nota fiscal carregado abaixo.":"Este XML é um resumo ou evento fiscal; confira a visualização abaixo.";
  }
  catch(DfeParseException ex){Message.Text=ex.Message;}catch{Message.Text="Não foi possível inspecionar o arquivo.";}
  finally{busy=false;Actions.IsEnabled=KeyBox.IsEnabled=true;BusyBar.Visibility=Visibility.Collapsed;}
 }
 private void Clear_Click(object sender,RoutedEventArgs e){KeyBox.Clear();Result.Clear();DanfeViewer.ClearView("Selecione ou abra um arquivo XML para visualizar o espelho gráfico da Nota Fiscal.");Message.Text="";}
 private void CopyKey_Click(object sender,RoutedEventArgs e)=>FiscalClipboard.CopyKey(KeyBox.Text,message=>Message.Text=message);
}
