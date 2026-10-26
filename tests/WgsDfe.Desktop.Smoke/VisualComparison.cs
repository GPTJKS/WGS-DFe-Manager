using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using WgsDfe.Application.Archive;
using WgsDfe.Database;
using WgsDfe.Desktop;
using WgsDfe.Infrastructure;

internal static class VisualComparison
{
 private static readonly string[] Areas=["consultas","empresas","documentos","configurações"];
 public static async Task PrepareFixtures(FoundationRuntime runtime)
 {
  using(var scope=runtime.Services.CreateScope())
  {
   var db=scope.ServiceProvider.GetRequiredService<WgsDbContext>();
   foreach(var company in await db.Companies.ToListAsync()){company.City="Município sintético";company.StateRegistration="ISENTO";}
   await db.SaveChangesAsync();
  }
  var sources=new List<string>();
  for(int number=124;number<=126;number++)
  {
   var xml=XDocument.Load(Path.Combine(AppContext.BaseDirectory,"fixture.xml"));XNamespace ns=xml.Root!.Name.Namespace;var info=xml.Root.Element(ns+"infNFe")!;string old=info.Attribute("Id")!.Value[3..];
   string body=old[..25]+number.ToString("D9",CultureInfo.InvariantCulture)+old.Substring(34,9);int sum=0;for(int i=0;i<43;i++)sum+=(body[i]-48)*(2+(42-i)%8);int remainder=sum%11;string key=body+(remainder is 0 or 1?0:11-remainder).ToString(CultureInfo.InvariantCulture);
   info.SetAttributeValue("Id","NFe"+key);info.Element(ns+"ide")!.Element(ns+"nNF")!.Value=number.ToString(CultureInfo.InvariantCulture);info.Element(ns+"ide")!.Element(ns+"cDV")!.Value=key[^1].ToString();
   info.Element(ns+"emit")!.Element(ns+"xNome")!.Value="Fornecedor sintético "+number;info.Element(ns+"ide")!.Element(ns+"tpAmb")!.Value="1";
   info.Element(ns+"emit")!.AddAfterSelf(new XElement(ns+"dest",new XElement(ns+"CNPJ","12345678000276"),new XElement(ns+"xNome","Cliente sintético")));
   info.Element(ns+"total")!.Element(ns+"ICMSTot")!.Element(ns+"vNF")!.Value=(number*17.35m).ToString("0.00",CultureInfo.InvariantCulture);
   string path=Path.Combine(runtime.DataDirectory+"-exports","comparison-synthetic-"+number+".xml");xml.Save(path);sources.Add(path);
  }
  using var services=runtime.Services.CreateScope();var result=await services.ServiceProvider.GetRequiredService<IArchiveService>().ImportAsync(new(sources));
  if(result.Imported!=3)throw new InvalidOperationException("Comparison XML fixtures: "+string.Join(", ",result.Rows.Select(x=>x.Code)));
 }
 public static async Task Capture(MainWindow main,string output,Action<string> snapshot,Action<string> navigate)
 {
  Directory.CreateDirectory(Path.Combine(output,"comparison"));
  var geometry=new List<object>();
  typeof(MainWindow).GetMethod("ApplyNavigation",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.Invoke(main,new object[]{false});
  foreach(string area in Areas)
  {
   string source=Path.GetFullPath(Path.Combine("docs/validation/redesign/comparacao",area+"-stitch.png"));if(!File.Exists(source))continue;
   var reference=Read(source);main.Width=reference.PixelWidth+16;main.Height=reference.PixelHeight+39;
   await Ready(main);navigate(area switch{"consultas"=>"Consultas","empresas"=>"Empresas","documentos"=>"Documentos",_=>"Configurações"});await Task.Delay(100);await Ready(main);
   if(main.ActiveView is QueriesView {ActiveView:NfeWindow query})
   {((ComboBox)query.FindName("EnvironmentBox")).SelectedIndex=0;var period=(PeriodDocumentsPanel)query.FindName("Period");((DatePicker)period.FindName("FromDate")).SelectedDate=new DateTime(2026,9,1);((DatePicker)period.FindName("ToDate")).SelectedDate=new DateTime(2026,9,30);await period.RefreshAsync();((TabControl)query.FindName("ResultTabs")).SelectedIndex=0;if(((DataGrid)period.FindName("Documents")).Items.Count!=3)throw new InvalidOperationException("Comparison consultation must display three real imported documents in period.");}
   if(main.ActiveView is ArchiveWindow archive){((TabControl)archive.FindName("Tabs")).SelectedIndex=0;navigate("Pesquisar");await Ready(main);}
   snapshot(area+"-reference-viewport");
   var root=(FrameworkElement)main.Content;var active=main.ActiveView;
   geometry.Add(new{area,reference=new{width=reference.PixelWidth,height=reference.PixelHeight},actual=new{width=root.ActualWidth,height=root.ActualHeight},sidebar=((ColumnDefinition)main.FindName("SidebarColumn")).ActualWidth,controls=Walk(active??(DependencyObject)main.FindName("CompanyPage")).OfType<FrameworkElement>().Where(x=>x is DataGrid||x is TabControl).Select(x=>new{name=x.Name,x.ActualWidth,x.ActualHeight,position=x.TransformToAncestor(root).Transform(new Point(0,0))}).ToArray()});
   GeneratePair(source,Path.Combine(output,area+"-reference-viewport.png"),Path.Combine(output,"comparison",area));
  }
  File.WriteAllText(Path.Combine(output,"comparison","geometry.json"),JsonSerializer.Serialize(geometry,new JsonSerializerOptions{WriteIndented=true}));
 }
 private static async Task Ready(MainWindow main)
 {
  bool Busy(WorkspaceView? view)
  {if(view is QueriesView queries&&Busy(queries.ActiveView))return true;return view?.GetType().GetField("busy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)?.GetValue(view) is true;}
  for(int step=0;step<1000;step++){if(!Busy(main.ActiveView)&&((StackPanel)main.FindName("Navigation")).IsEnabled)return;await Task.Delay(40);}throw new TimeoutException("Visual comparison did not become ready.");
 }
 public static void CompareExisting(string output)
 {
  Directory.CreateDirectory(Path.Combine(output,"comparison"));
  foreach(string area in Areas)
  {
   var source=Path.GetFullPath(Path.Combine("docs/validation/redesign/comparacao",area+"-stitch.png"));var actual=Path.Combine(output,area+"-1366x768-expandido.png");
   if(File.Exists(source)&&File.Exists(actual))GeneratePair(source,actual,Path.Combine(output,"comparison",area));
  }
 }
 public static void CompareUserEvidence(string output)
 {
  foreach(var pair in new[]{("checkbox.png","checkbox-marcado-estavel.png","antes-depois-checkbox"),("consulta-lote76.png","nfce-em-andamento-retorno.png","antes-depois-consulta")})
  {
   string source=Path.GetFullPath(Path.Combine("docs/validation/stitch-background/antes",pair.Item1));
   if(File.Exists(source))GeneratePair(source,Path.Combine(output,pair.Item2),Path.Combine(output,"comparison",pair.Item3),"ANTES · USO REAL","DEPOIS · WPF SINTÉTICO");
  }
 }
 public static void CompareRoundTwoEvidence(string output)
 {
  Directory.CreateDirectory(Path.Combine(output,"comparison"));
  foreach(var pair in new[]{
   ("0d0df773-14f5-4c91-9c49-511e8e8cdcff","exportacao-expandida-1920x1080-escala1,00-normal-opcoes","exportacao"),
   ("158394a7-71f0-4f78-b3c9-07d47d0c9b03","nfce-em-andamento-retorno","consulta-periodo"),
   ("ece3b478-f1b0-4266-a11f-7411c1489ab8","automacao-refinada","automacao"),
   ("53d614cf-6dd9-4aed-9299-6eba12639bc7","nfce-cancelada-preservada","consulta-cancelada")})
   GeneratePair(Path.GetFullPath(Path.Combine("docs/validation/redesign/antes-rodada2",pair.Item1+".png")),Path.Combine(output,pair.Item2+".png"),Path.Combine(output,"comparison","rodada2-"+pair.Item3),"ANTES · USO REAL","DEPOIS · WPF SINTÉTICO");
 }
 public static void CompareExportEvidence(string output)
 {
  Directory.CreateDirectory(Path.Combine(output,"comparison"));var after=Read(Path.Combine(output,"arvore-destino-detalhe.png"));
  foreach(string id in new[]{"a15780e4-659a-4319-954c-d51ca3a0a7d2","67edfee4-d06f-4a6b-bcd9-19704c47afe2","97b3acd7-e611-4da5-9243-e20fdbc57af7"})
  {
   var before=Read(Path.GetFullPath(Path.Combine("docs/validation/redesign/rodada3/antes",id+".png")));int width=Math.Max(before.PixelWidth,after.PixelWidth);int height=(int)Math.Ceiling(Math.Max(before.PixelHeight*(double)width/before.PixelWidth,after.PixelHeight*(double)width/after.PixelWidth));
   var side=new DrawingVisual();using(var draw=side.RenderOpen()){draw.DrawRectangle(Brushes.White,null,new Rect(0,0,width*2+24,height+32));Label(draw,"ANTES · EXPLORER · USO REAL",12);Label(draw,"DEPOIS · ÁRVORE REAL DO DESTINO SINTÉTICO",width+36);draw.DrawImage(before,new Rect(0,32,width,before.PixelHeight*(double)width/before.PixelWidth));draw.DrawImage(after,new Rect(width+24,32,width,after.PixelHeight*(double)width/after.PixelWidth));}Save(side,width*2+24,height+32,Path.Combine(output,"comparison","exportacao-"+id+"-antes-depois.png"));
  }
 }
 private static BitmapImage Read(string path){var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.UriSource=new Uri(Path.GetFullPath(path));image.EndInit();image.Freeze();return image;}
 private static void GeneratePair(string source,string actual,string destination,string beforeLabel="GOOGLE STITCH",string afterLabel="WPF · IMPLEMENTAÇÃO")
 {
  var reference=Read(source);var current=Read(actual);int width=reference.PixelWidth,height=reference.PixelHeight;
  var side=new DrawingVisual();using(var draw=side.RenderOpen()){draw.DrawRectangle(Brushes.White,null,new Rect(0,0,width*2+24,height+32));Label(draw,beforeLabel,12);Label(draw,afterLabel,width+36);draw.DrawImage(reference,new Rect(0,32,width,height));draw.DrawImage(current,new Rect(width+24,32,width,Math.Min(height,current.PixelHeight*(double)width/current.PixelWidth)));}Save(side,width*2+24,height+32,destination+"-side-by-side.png");
  var overlay=new DrawingVisual();using(var draw=overlay.RenderOpen()){draw.DrawImage(reference,new Rect(0,0,width,height));draw.PushOpacity(.5);draw.DrawImage(current,new Rect(0,0,width,Math.Min(height,current.PixelHeight*(double)width/current.PixelWidth)));draw.Pop();}Save(overlay,width,height,destination+"-overlay50.png");
  File.WriteAllText(destination+"-normalization.txt",$"Reference {reference.PixelWidth}x{reference.PixelHeight}; WPF {current.PixelWidth}x{current.PixelHeight}; comparison canvas {width}x{height}; current image preserves aspect ratio and is top aligned; unavailable lower viewport remains blank. Pixel overlay identifies geometry differences; differing source data is intentional.");
 }
 private static void Label(DrawingContext draw,string label,double left)=>draw.DrawText(new FormattedText(label,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,new Typeface("Segoe UI Semibold"),12,Brushes.DarkSlateGray,1),new Point(left,8));
 private static void Save(Visual visual,int width,int height,string path){var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(path);encoder.Save(file);}
 private static IEnumerable<DependencyObject> Walk(DependencyObject parent){for(int index=0;index<VisualTreeHelper.GetChildrenCount(parent);index++){var child=VisualTreeHelper.GetChild(parent,index);yield return child;foreach(var nested in Walk(child))yield return nested;}}
}
