using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
namespace WgsDfe.Desktop;
public static class Motion
{
 public const int InteractionMilliseconds=140,TransitionMilliseconds=220,ModalMilliseconds=220;
 public static Action CaptureExit(FrameworkElement target)
 {
  if(!Allowed||target.ActualWidth<1||target.ActualHeight<1||target.Parent is not Grid parent)return ()=>{};
  // Keep a visual snapshot while the cached page leaves the tree. Its work and state stay alive.
  var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(target.ActualWidth),(int)Math.Ceiling(target.ActualHeight),96,96,PixelFormats.Pbgra32);
  bitmap.Render(target);bitmap.Freeze();
  var image=new Image{Source=bitmap,Stretch=Stretch.Fill,IsHitTestVisible=false};
  Grid.SetRow(image,Grid.GetRow(target));Grid.SetColumn(image,Grid.GetColumn(target));Panel.SetZIndex(image,90);parent.Children.Add(image);
  return ()=>
  {
   var move=new TranslateTransform();image.RenderTransform=move;
   var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
   move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,-10,TimeSpan.FromMilliseconds(190)){EasingFunction=ease});
   var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(190)){EasingFunction=ease};fade.Completed+=(_,_)=>parent.Children.Remove(image);image.BeginAnimation(UIElement.OpacityProperty,fade);
  };
 }
 public static void Slide(FrameworkElement target)
 {
  Fade(target,.18,1,TransitionMilliseconds);var move=new TranslateTransform();target.RenderTransform=move;
  if(Allowed)
  {
   move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(8,0,TimeSpan.FromMilliseconds(TransitionMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
   Cascade(target);
  }
 }
 public static void Cascade(FrameworkElement root)
 {
  if(!Allowed)return;
  FrameworkElement? current=root;
  for(int depth=0;depth<4&&current is not null;depth++)
  {
   if(current is QueriesView q&&q.ActiveView is FrameworkElement active){current=active;continue;}
   if(current is ContentControl cc&&cc.Content is FrameworkElement inner){current=inner;continue;}
   if(current is ScrollViewer sv&&sv.Content is FrameworkElement scrolled){current=scrolled;continue;}
   break;
  }
  if(current is not Panel panel)return;
  int index=0;
  foreach(var child in panel.Children.OfType<FrameworkElement>())
  {
   if(child.Visibility!=Visibility.Visible||child is TabControl)continue;
   AnimateBlock(child,index*32);
   if(++index>=5)break;
  }
 }
 private static void AnimateBlock(FrameworkElement element,int delayMs)
 {
  element.RenderTransformOrigin=new Point(0.5,0.5);
  var group=new TransformGroup();var scale=new ScaleTransform(0.988,0.988);var move=new TranslateTransform(0,14);
  group.Children.Add(scale);group.Children.Add(move);element.RenderTransform=group;
  var duration=TimeSpan.FromMilliseconds(260);var delay=TimeSpan.FromMilliseconds(delayMs);
  var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
  move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(14,0,duration){BeginTime=delay,EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(0.988,1,duration){BeginTime=delay,EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.988,1,duration){BeginTime=delay,EasingFunction=ease});
 }
 public static void PulseNav(Button? navButton)
 {
  if(!Allowed||navButton is null)return;
  if(navButton.Template?.FindName("ActiveBar",navButton) is FrameworkElement bar)
  {
   bar.RenderTransformOrigin=new Point(0.5,0.5);
   var scale=new ScaleTransform(1,0.15);bar.RenderTransform=scale;
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.15,1,TimeSpan.FromMilliseconds(240)){EasingFunction=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=0.45}});
  }
  if(navButton.Template?.FindName("Surface",navButton) is FrameworkElement surface)
  {
   surface.RenderTransformOrigin=new Point(0.5,0.5);
   var scale=new ScaleTransform(0.97,0.97);surface.RenderTransform=scale;
   var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(0.97,1,TimeSpan.FromMilliseconds(180)){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.97,1,TimeSpan.FromMilliseconds(180)){EasingFunction=ease});
  }
 }
 public static void SlideChip(FrameworkElement? chip)
 {
  if(!Allowed||chip is null)return;
  Fade(chip,.35,1,200);
  var move=new TranslateTransform(0,5);chip.RenderTransform=move;
  move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(5,0,TimeSpan.FromMilliseconds(200)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
 }
 public static bool Disabled {get;set;}
 internal static bool? SystemAnimationOverrideForTests {get;set;}
 public static bool Allowed=>!Disabled&&(SystemAnimationOverrideForTests??true);
 public static readonly DependencyProperty AnimateProperty=DependencyProperty.RegisterAttached("Animate",typeof(bool),typeof(Motion),new PropertyMetadata(false,Attach));
 public static bool GetAnimate(DependencyObject target)=>(bool)target.GetValue(AnimateProperty);
 public static void SetAnimate(DependencyObject target,bool value)=>target.SetValue(AnimateProperty,value);
 public static void Fade(UIElement target,double from,double to,int milliseconds)
 {
  target.BeginAnimation(UIElement.OpacityProperty,null);target.Opacity=to;
  if(Allowed)target.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(milliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}});
 }
 public static void Enter(FrameworkElement target)
 {
  Fade(target,.15,1,ModalMilliseconds);
  var transforms=new TransformGroup();var scale=new ScaleTransform(1,1);var move=new TranslateTransform();transforms.Children.Add(scale);transforms.Children.Add(move);target.RenderTransform=transforms;
  if(!Allowed)return;
  var ease=new CubicEase{EasingMode=EasingMode.EaseOut};
  move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(12,0,TimeSpan.FromMilliseconds(ModalMilliseconds)){EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(.98,1,TimeSpan.FromMilliseconds(ModalMilliseconds)){EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(.98,1,TimeSpan.FromMilliseconds(ModalMilliseconds)){EasingFunction=ease});
 }
 public static void Exit(FrameworkElement target,Action complete)
 {
  if(!Allowed){complete();return;}
  var animation=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(180)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseIn}};
  animation.Completed+=(_,_)=>complete();target.BeginAnimation(UIElement.OpacityProperty,animation);
  var transforms=new TransformGroup();var scale=new ScaleTransform();var move=new TranslateTransform();transforms.Children.Add(scale);transforms.Children.Add(move);target.RenderTransform=transforms;
  var duration=TimeSpan.FromMilliseconds(180);var ease=new CubicEase{EasingMode=EasingMode.EaseIn};
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(1,.98,duration){EasingFunction=ease});scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.98,duration){EasingFunction=ease});move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(0,10,duration){EasingFunction=ease});
 }
 private static void Attach(DependencyObject target,DependencyPropertyChangedEventArgs args)
 {
  if(args.NewValue is not true||target is not FrameworkElement element)return;
  element.Loaded+=(_,_)=>{if(element is Button button)Ui.Decorate(button);if(element is not DataGridRow)Fade(element,.75,1,160);};
  if(element is ButtonBase)
  {
   element.MouseEnter+=(_,_)=>{AnimateSurface(element,-1.5,1.015,InteractionMilliseconds);AnimateEdge(element,Color.FromRgb(128,159,151));};
   element.MouseLeave+=(_,_)=>{AnimateSurface(element,0,1.0,InteractionMilliseconds);AnimateEdge(element,Color.FromRgb(220,228,242));};
   element.PreviewMouseLeftButtonDown+=(_,_)=>AnimateSurface(element,0.5,0.97,80);
   element.PreviewMouseLeftButtonUp+=(_,_)=>AnimateSurface(element,-1.0,1.0,160,spring:true);
   element.GotKeyboardFocus+=(_,_)=>AnimateEdge(element,Color.FromRgb(177,149,99));
  }
  if(element is TextBlock text)
  {
   var descriptor=System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty,typeof(TextBlock));bool listening=false;
   EventHandler changed=(_,_)=>Fade(text,.35,1,180);
   text.Loaded+=(_,_)=>{if(!listening){descriptor.AddValueChanged(text,changed);listening=true;}};
   text.Unloaded+=(_,_)=>{if(listening){descriptor.RemoveValueChanged(text,changed);listening=false;}};
  }
  if(element is TabItem tab)
  {
   tab.MouseEnter+=(_,_)=>{if(!tab.IsSelected&&tab.Template?.FindName("Tab",tab) is FrameworkElement pill)AnimatePill(pill,-1,1.01,120,false);};
   tab.MouseLeave+=(_,_)=>{if(tab.Template?.FindName("Tab",tab) is FrameworkElement pill)AnimatePill(pill,0,1.0,140,false);};
   tab.AddHandler(Selector.SelectedEvent,new RoutedEventHandler((_,_)=>
   {
    if(tab.Template?.FindName("Tab",tab) is FrameworkElement pill)AnimatePill(pill,0,1.0,200,true);
    if(tab.Content is FrameworkElement content)Slide(content);
   }));
  }
  if(element is CheckBox check){check.Checked+=(_,_)=>Fade(check,.65,1,InteractionMilliseconds);check.Unchecked+=(_,_)=>Fade(check,.65,1,InteractionMilliseconds);}
  if(element is Expander expander)
  {
   expander.Expanded+=(_,_)=>
   {
    AnimateChevron(expander,true);
    if(expander.Template.FindName("Body",expander) is not FrameworkElement body)return;
    body.Visibility=Visibility.Visible;Slide(body);
   };
   expander.Collapsed+=(_,_)=>
   {
    AnimateChevron(expander,false);
    if(expander.Template.FindName("Body",expander) is not FrameworkElement body)return;
    if(!Allowed){body.Visibility=Visibility.Collapsed;return;}
    body.Visibility=Visibility.Visible;var scale=new ScaleTransform(1,1);body.RenderTransform=scale;
    scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(1,.96,TimeSpan.FromMilliseconds(160)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseIn}});
    var fade=new DoubleAnimation(1,0,TimeSpan.FromMilliseconds(160));fade.Completed+=(_,_)=>{if(!expander.IsExpanded)body.Visibility=Visibility.Collapsed;};body.BeginAnimation(UIElement.OpacityProperty,fade);
   };
  }
 }
 private static void AnimateSurface(FrameworkElement element,double y,double scaleValue,int ms,bool spring=false)
 {
  if(!Allowed||element is not Control control||control.Template?.FindName("Surface",control) is not FrameworkElement surface)return;
  bool isNav=control.Template?.FindName("ActiveBar",control) is not null;
  surface.RenderTransformOrigin=new Point(0.5,0.5);
  var group=new TransformGroup();var scale=new ScaleTransform(1,1);var move=new TranslateTransform();
  if(surface.RenderTransform is TransformGroup existing&&existing.Children.Count==2&&existing.Children[0] is ScaleTransform es&&existing.Children[1] is TranslateTransform em){scale=es;move=em;}
  else{group.Children.Add(scale);group.Children.Add(move);surface.RenderTransform=group;}
  var duration=TimeSpan.FromMilliseconds(ms);
  IEasingFunction ease=spring?new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=0.35}:new CubicEase{EasingMode=EasingMode.EaseOut};
  if(isNav)
  {
   double targetX=y<0?3.5:0;
   move.BeginAnimation(TranslateTransform.XProperty,new DoubleAnimation(targetX,duration){EasingFunction=ease});
  }
  else
  {
   move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(y,duration){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(scaleValue,duration){EasingFunction=ease});
   scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(scaleValue,duration){EasingFunction=ease});
  }
 }
 private static void AnimatePill(FrameworkElement pill,double y,double targetScale,int ms,bool pop)
 {
  if(!Allowed)return;
  pill.RenderTransformOrigin=new Point(0.5,0.5);
  var group=new TransformGroup();var scale=new ScaleTransform(pop?0.94:1,pop?0.94:1);var move=new TranslateTransform(0,y);
  group.Children.Add(scale);group.Children.Add(move);pill.RenderTransform=group;
  var duration=TimeSpan.FromMilliseconds(ms);
  IEasingFunction ease=pop?new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=0.4}:new CubicEase{EasingMode=EasingMode.EaseOut};
  move.BeginAnimation(TranslateTransform.YProperty,new DoubleAnimation(y,duration){EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(pop?0.94:scale.ScaleX,targetScale,duration){EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(pop?0.94:scale.ScaleY,targetScale,duration){EasingFunction=ease});
 }
 private static void AnimateChevron(Expander expander,bool expanded)
 {
  if(!Allowed||expander.Template?.FindName("HeaderButton",expander) is not ToggleButton header||header.Template?.FindName("Chevron",header) is not FrameworkElement chevron)return;
  chevron.RenderTransformOrigin=new Point(0.5,0.5);
  var scale=new ScaleTransform(0.8,0.8);chevron.RenderTransform=scale;
  var ease=new BackEase{EasingMode=EasingMode.EaseOut,Amplitude=0.5};
  scale.BeginAnimation(ScaleTransform.ScaleXProperty,new DoubleAnimation(0.75,1,TimeSpan.FromMilliseconds(180)){EasingFunction=ease});
  scale.BeginAnimation(ScaleTransform.ScaleYProperty,new DoubleAnimation(0.75,1,TimeSpan.FromMilliseconds(180)){EasingFunction=ease});
 }
 private static void AnimateEdge(FrameworkElement element,Color target)
 {
  if(element is not Control control)return;
  if(control is Button&&control.Template?.FindName("Surface",control) is Border surface&&control.Template?.FindName("ActiveBar",control) is null)
  {var brush=new SolidColorBrush((surface.BorderBrush as SolidColorBrush)?.Color??target);surface.BorderBrush=brush;if(Allowed)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(brush.Color,target,TimeSpan.FromMilliseconds(InteractionMilliseconds)));else brush.Color=target;}
  else if(control is CheckBox)
  {var brush=new SolidColorBrush((control.BorderBrush as SolidColorBrush)?.Color??target);control.SetCurrentValue(Control.BorderBrushProperty,brush);if(Allowed)brush.BeginAnimation(SolidColorBrush.ColorProperty,new ColorAnimation(brush.Color,target,TimeSpan.FromMilliseconds(InteractionMilliseconds)));else brush.Color=target;}
 }
}
