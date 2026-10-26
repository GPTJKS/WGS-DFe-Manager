using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
namespace WgsDfe.Desktop;

/// <summary>Internal screen with explicit safe navigation and final cleanup.</summary>
public class WorkspaceView : UserControl
{
 public event RoutedEventHandler FirstLoaded
 {
  add{bool initialized=false;Loaded+=(sender,args)=>{if(initialized)return;initialized=true;value(sender,args);};}
  remove { }
 }
 public string Title {get;set;}="";
 public Size? PreferredDialogSize {get;set;}
 public bool HasUnsavedChanges {get;protected set;}
 public virtual bool CanRunInBackground=>false;
 public virtual bool IsWorking=>false;
 public virtual bool HasOperationHistory=>false;
 public virtual string OperationStatus=>Title;
 public virtual void CancelOperation(){ }
 internal bool CanDeactivate()=>CanRunInBackground||CanLeave();
 public Window Owner => Window.GetWindow(this) ?? System.Windows.Application.Current.MainWindow;
 private bool? result;
 public bool? DialogResult {get=>result;set{result=value;Close();}}
 public event CancelEventHandler? Closing;
 public event EventHandler? Closed;
 internal bool CanLeave(){var args=new CancelEventArgs();Closing?.Invoke(this,args);return !args.Cancel;}
 internal void DisposeView()=>Closed?.Invoke(this,EventArgs.Empty);
 public void Close(){if(Owner is MainWindow main)main.Back();}
}
internal static class WorkspaceNavigator
{
 public static void Open(WorkspaceView view)
 {if(System.Windows.Application.Current.MainWindow is MainWindow main)main.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Normal,new Action(()=>main.OpenView(view)));}
}
