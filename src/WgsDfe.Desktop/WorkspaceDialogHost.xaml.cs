using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
namespace WgsDfe.Desktop;

public partial class WorkspaceDialogHost : UserControl
{
 private readonly Stack<(WorkspaceView View,IInputElement? Focus,bool Maximized)> stack=new();
 private IInputElement? returnFocus;
 private bool maximized,closing;
 public WorkspaceView? ActiveView=>Body.Content as WorkspaceView;
 public bool IsOpen=>ActiveView is not null;
 public bool IsMaximized=>maximized;
 public bool IsConfirmingDiscard=>DiscardPrompt.Visibility==Visibility.Visible;
 public double SidebarInset {get;set;}=256;
 public event EventHandler? OpenStateChanged;
 public event Action<WorkspaceView>? Suspended;
 public WorkspaceDialogHost(){InitializeComponent();SizeChanged+=(_,_)=>Resize();}
 public void Open(WorkspaceView view)
 {
  if(closing)return;
  var focus=Keyboard.FocusedElement;
  if(ActiveView is { } previous)stack.Push((previous,returnFocus,maximized));
  returnFocus=focus;Body.Content=view;DialogTitle.Text=view.Title;BackgroundButton.Visibility=view is ExportWindow?Visibility.Visible:Visibility.Collapsed;maximized=false;
  Visibility=Visibility.Visible;DiscardPrompt.Visibility=Visibility.Collapsed;maximized=ActualWidth-SidebarInset<900||ActualHeight-82<600;Resize();
  OpenStateChanged?.Invoke(this,EventArgs.Empty);
  Motion.Enter(Frame);Motion.Fade(Scrim,0,1,200);
  Dispatcher.BeginInvoke(new Action(()=>{if(ReferenceEquals(ActiveView,view))view.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));}));
 }
 public bool RequestClose()
 {
  if(closing||ActiveView is not { } view)return false;
  if(!view.CanLeave())return false;
  if(view.HasUnsavedChanges&&view.DialogResult!=true)
  {DiscardPrompt.Visibility=Visibility.Visible;Frame.IsEnabled=false;ContinueEditing.Focus();return false;}
  FinishClose();return true;
 }
 private void FinishClose()
 {
  if(ActiveView is not { } view)return;
  closing=true;var restore=returnFocus;
  void Complete()
  {
   Body.Content=null;view.DisposeView();
   if(stack.TryPop(out var entry)){Body.Content=entry.View;returnFocus=entry.Focus;maximized=entry.Maximized;DialogTitle.Text=entry.View.Title;BackgroundButton.Visibility=entry.View is ExportWindow?Visibility.Visible:Visibility.Collapsed;Resize();}
   else Visibility=Visibility.Collapsed;
   closing=false;Frame.IsEnabled=true;Frame.Opacity=1;DiscardPrompt.Visibility=Visibility.Collapsed;
   OpenStateChanged?.Invoke(this,EventArgs.Empty);
   Dispatcher.BeginInvoke(new Action(()=>{if(restore is UIElement element&&element.IsVisible&&element.IsEnabled)Keyboard.Focus(restore);else ActiveView?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));}));
  }
  if(stack.Count==0)Motion.Fade(Scrim,1,0,180);
  Motion.Exit(Frame,Complete);
 }
 public void Resize()
 {
  DialogArea.Margin=new Thickness(SidebarInset,52,0,30);
  double width=Math.Max(0,ActualWidth-SidebarInset),height=Math.Max(0,ActualHeight-82);

  Frame.Width=Math.Max(0,maximized?width-20:Math.Min(width-20,ActiveView?.PreferredDialogSize?.Width??Math.Max(650,width*.86)));
  Frame.Height=Math.Max(0,maximized?height-16:Math.Min(height-16,ActiveView?.PreferredDialogSize?.Height??Math.Max(450,height*.86)));
  MaximizeButton.Content=maximized?"\uE923":"\uE739";
 }
 private void Maximize_Click(object sender,RoutedEventArgs e){maximized=!maximized;Resize();Motion.Enter(Frame);}
 private void Close_Click(object sender,RoutedEventArgs e)=>RequestClose();
 private void Background_Click(object sender,RoutedEventArgs e)
 {
  if(ActiveView is not ExportWindow view||stack.Count>0)return;
  Body.Content=null;Visibility=Visibility.Collapsed;Suspended?.Invoke(view);OpenStateChanged?.Invoke(this,EventArgs.Empty);
 }
 private void Continue_Click(object sender,RoutedEventArgs e){DiscardPrompt.Visibility=Visibility.Collapsed;Frame.IsEnabled=true;ActiveView?.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));}
 private void Discard_Click(object sender,RoutedEventArgs e)=>FinishClose();
 private void Overlay_KeyDown(object sender,KeyEventArgs e)
 {if(e.Key==Key.Escape){HandleEscape();e.Handled=true;}}
 internal void HandleEscape()
 {if(IsConfirmingDiscard)Continue_Click(this,new RoutedEventArgs());else RequestClose();}
 internal void DisposeAll(){ActiveView?.DisposeView();Body.Content=null;while(stack.TryPop(out var entry))entry.View.DisposeView();}
}
