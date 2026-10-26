using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WgsDfe.Infrastructure;
namespace WgsDfe.Desktop;
public partial class MainWindow
{
 private double interfaceZoom=1;
 private ScrollViewer zoomViewport=null!;
 private FrameworkElement zoomSurface=null!;
 private void InitializeInput()
 {
  InterfaceInput.Register();
  interfaceZoom=UserPreferences.Load(runtime.DataDirectory).InterfaceZoom;
  if(!double.IsFinite(interfaceZoom))interfaceZoom=1;
  interfaceZoom=Math.Clamp(interfaceZoom,.75,1.5);
  zoomSurface=(FrameworkElement)Content;Content=null;
  zoomViewport=new ScrollViewer{Content=zoomSurface,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Content=zoomViewport;
  zoomViewport.SizeChanged+=(_,_)=>ResizeZoom();Loaded+=(_,_)=>ResizeZoom();
  AddHandler(PreviewMouseWheelEvent,new MouseWheelEventHandler((_,e)=>{if((Keyboard.Modifiers&ModifierKeys.Control)==0)return;SetZoom(interfaceZoom+(e.Delta>0?.1:-.1));e.Handled=true;}),true);
  AddHandler(Keyboard.PreviewKeyDownEvent,new KeyEventHandler(Shortcut),true);
 }
 private void ResizeZoom()
 {
  zoomSurface.LayoutTransform=new ScaleTransform(interfaceZoom,interfaceZoom);
  // Keep a usable logical canvas; outer scrollbars preserve access on small windows.
  zoomSurface.Width=Math.Max(960,Math.Max(0,zoomViewport.ActualWidth-18)/interfaceZoom);
  zoomSurface.Height=Math.Max(640,Math.Max(0,zoomViewport.ActualHeight-18)/interfaceZoom);
  if(!navigationManual)ApplyNavigation(zoomSurface.Width<1100);
  DialogHost.Resize();
 }
 internal void SetZoom(double value)
 {
  interfaceZoom=Math.Clamp(Math.Round(value,2),.75,1.5);ResizeZoom();
  try{UserPreferences.Update(runtime.DataDirectory,p=>p with{InterfaceZoom=interfaceZoom});}
  catch(Exception ex)when(ex is System.IO.IOException or UnauthorizedAccessException){MessageText.Text="Zoom aplicado; não foi possível salvar a preferência na pasta de dados.";}
 }
 private void Shortcut(object sender,KeyEventArgs e)
 {
  var key=e.Key==Key.System?e.SystemKey:e.Key;var modifiers=Keyboard.Modifiers;
  if(HandleShortcut(key,modifiers))e.Handled=true;
 }
 internal bool HandleShortcut(Key key,ModifierKeys modifiers)
 {
  if(key==Key.Escape&&modifiers==ModifierKeys.None)
  {
   var combo=InterfaceInput.Descendants(zoomSurface).OfType<ComboBox>().FirstOrDefault(x=>x.IsDropDownOpen);
   if(combo is not null){combo.IsDropDownOpen=false;return true;}
   var menu=InterfaceInput.Descendants(zoomSurface).OfType<FrameworkElement>().Select(x=>x.ContextMenu).FirstOrDefault(x=>x?.IsOpen==true);
   if(menu is not null){menu.IsOpen=false;return true;}
   if(DialogHost.IsOpen){DialogHost.HandleEscape();return true;}
  }
  if(modifiers==ModifierKeys.Control&&(key==Key.D0||key==Key.NumPad0)){SetZoom(1);return true;}
  else if(modifiers==ModifierKeys.Control&&key==Key.F)
  {
   DependencyObject area=ActiveView??(DependencyObject)CompanyPage;
   var search=InterfaceInput.Descendants(area).OfType<TextBox>().FirstOrDefault(x=>x.IsVisible&&x.IsEnabled&&!x.IsReadOnly&&(Ui.GetSearch(x)||x.Name.Contains("Search",StringComparison.OrdinalIgnoreCase)));
   if(search is not null){search.BringIntoView();search.Focus();search.SelectAll();return true;}
  }
  else if(modifiers==ModifierKeys.None&&key==Key.F1){if(DialogHost.IsOpen)DialogHost.Open(new InformationView(false));else Help_Click(this,new RoutedEventArgs());return true;}
  else if(modifiers==ModifierKeys.Alt&&key==Key.Left){Back();return true;}
  else if(modifiers==ModifierKeys.None&&key==Key.Escape&&!DialogHost.IsOpen)
  {
   if(ActiveView is ArchiveWindow archive&&archive.CloseInspector())return true;
   Back();return true;
  }
  return false;
 }
}
