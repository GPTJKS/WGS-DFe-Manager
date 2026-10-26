using System.Collections.Specialized;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
namespace WgsDfe.Desktop;

/// <summary>Non-interactive empty feedback shared by virtualized tables.</summary>
public static class GridFeedback
{
 public static readonly DependencyProperty EnabledProperty=DependencyProperty.RegisterAttached("Enabled",typeof(bool),typeof(GridFeedback),new PropertyMetadata(false,Changed));
 public static bool GetEnabled(DependencyObject target)=>(bool)target.GetValue(EnabledProperty);
 public static void SetEnabled(DependencyObject target,bool value)=>target.SetValue(EnabledProperty,value);
 private static void Changed(DependencyObject target,DependencyPropertyChangedEventArgs args)
 {
  if(target is not DataGrid grid||args.NewValue is not true)return;
  EmptyAdorner? overlay=null;AdornerLayer? owner=null;
  void Update()
  {
   var layer=AdornerLayer.GetAdornerLayer(grid);
   if(layer is null)return;
   if(grid.IsVisible&&grid.Items.Count==0)
   {
    if(overlay is null){overlay=new EmptyAdorner(grid);owner=layer;layer.Add(overlay);}
    overlay.InvalidateVisual();
   }
   else if(overlay is not null){owner?.Remove(overlay);overlay=null;owner=null;}
  }
  grid.Loaded+=(_,_)=>Update();
  grid.ItemContainerGenerator.ItemsChanged+=(_,_)=>Update();
  grid.IsVisibleChanged+=(_,_)=>Update();
  grid.SizeChanged+=(_,_)=>Update();
  grid.Unloaded+=(_,_)=>{if(overlay is not null){owner?.Remove(overlay);overlay=null;owner=null;}};
 }
 private sealed class EmptyAdorner(DataGrid grid):Adorner(grid)
 {
  protected override Geometry GetLayoutClip(Size layoutSlotSize)=>new RectangleGeometry(new Rect(new Point(),grid.RenderSize));
  protected override void OnRender(DrawingContext drawing)
  {
   IsHitTestVisible=false;
   if(grid.ActualWidth<140||grid.ActualHeight<80)return;
   drawing.PushClip(new RectangleGeometry(new Rect(new Point(),grid.RenderSize)));
   var text=new FormattedText("Nenhum registro para exibir",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Typography.Ui,System.Windows.FontStyles.Normal,System.Windows.FontWeights.Normal,System.Windows.FontStretches.Normal),12,(Brush)grid.FindResource("MutedBrush"),VisualTreeHelper.GetDpi(this).PixelsPerDip);
   double top=Math.Max(42,(grid.ActualHeight-text.Height)/2);
   if(grid.ActualHeight>=150)
   {
    var icon=new FormattedText("\uE8A5",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface("Segoe MDL2 Assets"),22,(Brush)grid.FindResource("Accent"),VisualTreeHelper.GetDpi(this).PixelsPerDip);
    drawing.DrawRoundedRectangle((Brush)grid.FindResource("SoftBrush"),null,new Rect(grid.ActualWidth/2-22,top-52,44,38),12,12);drawing.DrawText(icon,new Point(grid.ActualWidth/2-icon.Width/2,top-43));
    var caption=new FormattedText("Ajuste os filtros para encontrar registros.",CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,new Typeface(Typography.Ui,System.Windows.FontStyles.Normal,System.Windows.FontWeights.Normal,System.Windows.FontStretches.Normal),11,(Brush)grid.FindResource("MutedBrush"),VisualTreeHelper.GetDpi(this).PixelsPerDip);
    if(caption.Width<grid.ActualWidth-20)drawing.DrawText(caption,new Point((grid.ActualWidth-caption.Width)/2,top+24));
   }
   drawing.DrawText(text,new Point(Math.Max(12,(grid.ActualWidth-text.Width)/2),top));
   drawing.Pop();
  }
 }
}
