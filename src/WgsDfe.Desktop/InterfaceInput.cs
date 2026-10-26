using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace WgsDfe.Desktop;

internal static class InterfaceInput
{
 private static bool registered;
 public static void Register()
 {
  if(registered)return;registered=true;
  EventManager.RegisterClassHandler(typeof(ScrollViewer),UIElement.PreviewMouseWheelEvent,new MouseWheelEventHandler(Wheel));
 }
 private static DependencyObject? Parent(DependencyObject node)=>node is Visual or System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(node):LogicalTreeHelper.GetParent(node);
 private static void Wheel(object sender,MouseWheelEventArgs e)
 {
  if(e.Handled||(Keyboard.Modifiers&ModifierKeys.Control)!=0)return;
  // Preview tunnels from outside in: the first viewer dispatches to the deepest one
  // that can still scroll, allowing the wheel to continue at either boundary.
  var viewers=new List<ScrollViewer>();
  for(var node=e.OriginalSource as DependencyObject;node is not null;node=Parent(node))if(node is ScrollViewer viewer)viewers.Add(viewer);
  foreach(var viewer in viewers)
  {
   if(!viewer.IsEnabled||viewer.VerticalScrollBarVisibility==ScrollBarVisibility.Disabled)continue;
   if(e.Delta>0?viewer.VerticalOffset<=0:viewer.VerticalOffset>=viewer.ScrollableHeight)continue;
   if(SystemParameters.WheelScrollLines<0){if(e.Delta>0)viewer.PageUp();else viewer.PageDown();}
   else for(int i=0;i<Math.Max(1,SystemParameters.WheelScrollLines)*Math.Max(1,Math.Abs(e.Delta)/120);i++){if(e.Delta>0)viewer.LineUp();else viewer.LineDown();}
   e.Handled=true;return;
  }
 }
 internal static IEnumerable<DependencyObject> Descendants(DependencyObject root)
 {
  for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);yield return child;foreach(var nested in Descendants(child))yield return nested;}
 }
}
