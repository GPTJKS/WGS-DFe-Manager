using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Desktop;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Database;
internal static class ExportStructureScenario
{
 public static async Task Run(MainWindow main,FoundationRuntime runtime,string output,Action<string> snapshot,Action<bool,string> check,Action<string> click)
 {
  const string nsText="http://www.portalfiscal.inf.br/nfe";XNamespace ns=nsText;var paths=new List<string>();var keys=new List<string>();Guid company;
  for(int number=701;number<=702;number++)
  {
   var xml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"fixture.xml"));var info=xml.Root!.Element(ns+"infNFe")!;string old=info.Attribute("Id")!.Value[3..];string body=old[..2]+"2610"+old.Substring(6,14)+"65"+old.Substring(22,3)+number.ToString("D9")+old.Substring(34,9);int sum=0;for(int i=0;i<43;i++)sum+=(body[i]-48)*(2+(42-i)%8);int remainder=sum%11;string key=body+(remainder is 0 or 1?0:11-remainder);keys.Add(key);
   info.SetAttributeValue("Id","NFe"+key);var ide=info.Element(ns+"ide")!;ide.Element(ns+"mod")!.Value="65";ide.Element(ns+"nNF")!.Value=number.ToString();ide.Element(ns+"cDV")!.Value=key[^1].ToString();ide.Element(ns+"tpAmb")!.Value="1";ide.Element(ns+"dhEmi")!.Value=$"2026-10-0{number-700}T10:00:00-03:00";
   var wrapped=new XDocument(new XElement(ns+"nfeProc",new XAttribute("versao","4.00"),new XElement(xml.Root),new XElement(ns+"protNFe",new XAttribute("versao","4.00"),new XElement(ns+"infProt",new XElement(ns+"tpAmb","1"),new XElement(ns+"chNFe",key),new XElement(ns+"cStat","100"),new XElement(ns+"nProt","142260000000001"),new XElement(ns+"dhRecbto","2026-10-02T10:00:01-03:00")))));
   string path=Path.Combine(runtime.DataDirectory+"-exports","estrutura-"+number+".xml");wrapped.Save(path);paths.Add(path);
  }
  string eventXml=$"<procEventoNFe xmlns='{nsText}' versao='1.00'><evento versao='1.00'><infEvento Id='ID110111{keys[1]}01'><tpAmb>1</tpAmb><chNFe>{keys[1]}</chNFe><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><dhEvento>2026-10-03T12:13:14-03:00</dhEvento></infEvento></evento><retEvento versao='1.00'><infEvento><tpAmb>1</tpAmb><chNFe>{keys[1]}</chNFe><tpEvento>110111</tpEvento><nSeqEvento>1</nSeqEvento><cStat>135</cStat><nProt>142260000000001</nProt><dhRegEvento>2026-10-03T12:13:15-03:00</dhRegEvento></infEvento></retEvento></procEventoNFe>";
  string eventPath=Path.Combine(runtime.DataDirectory+"-exports","estrutura-evento.xml");await File.WriteAllTextAsync(eventPath,eventXml);paths.Add(eventPath);Guid[] documents;
  using(var scope=runtime.Services.CreateScope())
  {
   var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();var firm=await db.Companies.SingleAsync(x=>x.Cnpj=="12345678000195");firm.LegalName="Sorvetes São João";company=firm.Id;await db.SaveChangesAsync();
   var imported=await scope.ServiceProvider.GetRequiredService<IArchiveService>().ImportAsync(new(paths));check(imported.Imported==3,"structure scenario imports two synthetic notes and a cancellation event");documents=await db.FiscalDocuments.Where(x=>keys.Contains(x.AccessKey)).Select(x=>x.Id).ToArrayAsync();
  }
  string destination=Path.Combine(output,"xml");Directory.CreateDirectory(destination);
  main.Width=1920;main.Height=1080;main.OpenView(new ExportWindow(runtime,new ArchiveQuery(CompanyId:company,Model:DocumentModel.NFCe,Environment:FiscalEnvironment.Production,From:new(2026,10,1),To:new(2026,10,2)),documents:documents));await Wait(()=>main.ActiveView is ExportWindow e&&!e.IsWorking);main.UpdateLayout();await Task.Delay(100);await Wait(()=>!main.ActiveView!.IsWorking);
  var export=(ExportWindow)main.ActiveView!;((TextBox)export.FindName("DestinationBox")).Text=destination;click("Exportar para o destino");await Wait(()=>!export.IsWorking&&((TextBox)export.FindName("OutputPath")).Text.Length>0);
  var scroll=(ScrollViewer)export.FindName("ExportBody");scroll.ScrollToBottom();main.UpdateLayout();snapshot("exportacao-nova-concluida");
  check(!Walk(export).OfType<CheckBox>().Any(x=>x.Content?.ToString()=="Relatórios e manifesto"),"removed report and manifest option has no orphan UI control");
  check(!Walk(export).OfType<Button>().Any(x=>x.Content?.ToString()?.Contains("relatório",StringComparison.OrdinalIgnoreCase)==true),"export has no report action");
  var files=Directory.GetFiles(destination,"*",SearchOption.AllDirectories);check(files.Length==3&&files.All(x=>Path.GetExtension(x)==".xml"),"recursive destination scan contains exactly three XMLs and no auxiliary files");
  var relative=files.Select(x=>Path.GetRelativePath(destination,x).Replace('\\','/')).OrderBy(x=>x).ToArray();
  check(relative.All(x=>x.Split('/').Length==8&&x.StartsWith("Sorvetes São João/")&&x.Contains("/2026/10 - Outubro/01-10-2026 a 02-10-2026/NFC-e/Produção/")),"every real exported XML follows the complete company status date type environment hierarchy");
  check(relative.Any(x=>x.Contains("/Autorizados/"))&&relative.Any(x=>x.Contains("/Cancelados/")),"authorized and cancelled XMLs occupy separate status branches");
  var before=files.ToDictionary(x=>x,x=>File.GetLastWriteTimeUtc(x));click("Exportar para o destino");await Wait(()=>!export.IsWorking);check(before.All(x=>File.GetLastWriteTimeUtc(x.Key)==x.Value),"real UI reexport preserves identical XMLs without rewriting");check(((TextBlock)export.FindName("Message")).Text.Contains("Idênticos ignorados: 3"),"UI summary counts three identical skipped XMLs");snapshot("exportacao-nova-reexportacao");
  File.WriteAllLines(Path.Combine(output,"arvore-exportada.txt"),relative);File.WriteAllText(Path.Combine(output,"varredura-destino.json"),JsonSerializer.Serialize(new{files=relative,auxiliaryFiles=files.Count(x=>Path.GetExtension(x)!=".xml"),technicalDirectories=Directory.GetDirectories(destination,"*",SearchOption.AllDirectories).Count(x=>Path.GetFileName(x).StartsWith("WGS_DFe_"))},new JsonSerializerOptions{WriteIndented=true}));
  main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  var evidence=new WorkspaceView{Title="Árvore real do destino sintético"};var layout=new DockPanel{Margin=new Thickness(20)};var title=new TextBlock{Text="Diretórios e XMLs lidos do destino exportado",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),FontSize=18,FontWeight=FontWeights.SemiBold};DockPanel.SetDock(title,Dock.Top);layout.Children.Add(title);var tree=new TreeView{FontSize=14};layout.Children.Add(tree);evidence.Content=layout;
  foreach(string folder in Directory.GetDirectories(destination).Order())tree.Items.Add(Node(folder));main.OpenView(evidence);main.UpdateLayout();await Task.Delay(100);snapshot("arvore-real-exportacao-depois");CaptureTree(tree,output);VisualComparison.CompareExportEvidence(output);main.Back();await Wait(()=>!main.Dialogs.IsOpen);
  TreeViewItem Node(string path){var node=new TreeViewItem{Header=Path.GetFileName(path),IsExpanded=true};foreach(string folder in Directory.GetDirectories(path).Order())node.Items.Add(Node(folder));foreach(string file in Directory.GetFiles(path).Order())node.Items.Add(new TreeViewItem{Header=Path.GetFileName(file),ToolTip=file});return node;}
 }
 private static void CaptureTree(TreeView tree,string output)
 {
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)tree.ActualWidth,(int)tree.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(tree);
  var crop=new System.Windows.Media.Imaging.CroppedBitmap(bitmap,new Int32Rect(0,0,Math.Min(1100,bitmap.PixelWidth),Math.Min(500,bitmap.PixelHeight)));
  var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(crop));using var file=File.Create(Path.Combine(output,"arvore-destino-detalhe.png"));encoder.Save(file);
 }
 private static IEnumerable<DependencyObject> Walk(DependencyObject root){for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var next in Walk(child))yield return next;}}
 private static async Task Wait(Func<bool> ready){for(int i=0;i<1500;i++){if(ready())return;await Task.Delay(20);}throw new TimeoutException("Export structure UI condition");}
}
