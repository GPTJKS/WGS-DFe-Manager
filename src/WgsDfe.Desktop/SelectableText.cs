using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace WgsDfe.Desktop;
public sealed class SelectableText:TextBox
{
 public SelectableText()
 {
  IsReadOnly=true;IsReadOnlyCaretVisible=true;BorderThickness=new Thickness(0);
  Background=Brushes.Transparent;Padding=new Thickness(0);Margin=new Thickness(0);
  TextWrapping=TextWrapping.Wrap;MinHeight=0;
  VerticalScrollBarVisibility=ScrollBarVisibility.Disabled;
  HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
 }
}
