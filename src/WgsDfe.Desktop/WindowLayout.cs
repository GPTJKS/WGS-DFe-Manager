using System.Windows;
using System.Windows.Controls;
namespace WgsDfe.Desktop;
internal static class WindowLayout
{
 public static void Attach(Window window){window.FontFamily=Typography.Ui;window.FontSize=12.5;window.Language=System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR");window.UseLayoutRounding=true;window.SnapsToDevicePixels=true;}
 public static void Attach(WorkspaceView view)
 {
  view.FontFamily=Typography.Ui;view.FontSize=12.5;view.UseLayoutRounding=true;view.SnapsToDevicePixels=true;
  view.HorizontalContentAlignment=HorizontalAlignment.Stretch;view.VerticalContentAlignment=VerticalAlignment.Stretch;
  void AdaptResolution()
  {
   bool compactHeight=view.ActualHeight is >0 and <=720;
   if(view.FindName("HeaderViewport") is ScrollViewer header)header.MaxHeight=view.ActualHeight>900?330:view.ActualHeight>740?270:195;
   if(view.FindName("CompanyColumn") is ColumnDefinition col&&view.ActualWidth>0)col.Width=new GridLength(view.ActualWidth>=1450?305:view.ActualWidth>=1000?248:215);
   if(view.FindName("RootGrid") is Grid rootGrid)rootGrid.Margin=compactHeight?new Thickness(12,6,12,6):new Thickness(16,10,16,10);
   if(view.FindName("TopBanner") is Border topBanner){topBanner.Padding=compactHeight?new Thickness(10,4,10,4):new Thickness(12,6,12,6);topBanner.Margin=compactHeight?new Thickness(0,0,0,4):new Thickness(0,0,0,6);}
   if(view.FindName("FilterCard") is Border filterCard){filterCard.Padding=compactHeight?new Thickness(10,5,10,5):new Thickness(12,6,12,6);filterCard.Margin=compactHeight?new Thickness(0,0,0,4):new Thickness(0,0,0,6);}
   if(view.FindName("DiagnosticPreviewCard") is Border diagCard)diagCard.Padding=compactHeight?new Thickness(10,6,10,6):new Thickness(12,8,12,8);
  }
  view.Loaded+=(_,_)=>AdaptResolution();
  view.SizeChanged+=(_,_)=>AdaptResolution();
  if(view is not (HistoricalWindow or AutomationWindow or CertificateWindow or CertificateLinksWindow))return;
  if(view.Content is not Grid body)return;
  // The modal owns the chrome. Only the body scrolls; cancellation/result actions stay pinned.
  view.Content=null;var outer=new Grid{Margin=body.Margin};body.Margin=new Thickness(0);
  outer.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});outer.RowDefinitions.Add(new(){Height=GridLength.Auto});
  int last=body.RowDefinitions.Count-1;var footer=new StackPanel{Margin=new Thickness(0,8,0,0)};
  foreach(var child in body.Children.Cast<UIElement>().Where(x=>Grid.GetRow(x)==last).ToArray()){body.Children.Remove(child);footer.Children.Add(child);}
  if(last>=0)body.RowDefinitions.RemoveAt(last);
  var viewport=new ScrollViewer{Content=body,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
  outer.Children.Add(viewport);Grid.SetRow(footer,1);outer.Children.Add(footer);view.Content=outer;
  void Resize(){body.Width=Math.Max(660,viewport.ActualWidth-14);body.Height=Math.Max(view is AutomationWindow?1050:view is HistoricalWindow?580:480,viewport.ActualHeight-14);}
  viewport.Loaded+=(_,_)=>Resize();viewport.SizeChanged+=(_,_)=>Resize();
 }
}
