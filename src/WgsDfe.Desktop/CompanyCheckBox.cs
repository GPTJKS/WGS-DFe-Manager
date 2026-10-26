using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace WgsDfe.Desktop;

/// <summary>One owner for row selection, including keyboard and UI Automation Toggle.</summary>
public sealed class CompanyCheckBox:CheckBox
{
 public CompanyCheckBox(){SetResourceReference(StyleProperty,typeof(CheckBox));Motion.SetAnimate(this,true);}
 protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
 {OnToggle();Focus();e.Handled=true;}
 protected override void OnToggle()
 {
  for(DependencyObject? current=this;current is not null;current=VisualTreeHelper.GetParent(current))
   if(current is DataGridRow row){row.IsSelected=!row.IsSelected;return;}
 }
}
