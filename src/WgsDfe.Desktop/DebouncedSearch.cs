using System.Windows.Controls;
namespace WgsDfe.Desktop;
internal static class DebouncedSearch
{
 public static void Attach(TextBox box,Func<CancellationToken,Task> search)
 {
  CancellationTokenSource? pending=null;
  box.TextChanged+=async(_,_)=>
  {
   if(!box.IsLoaded)return;
   pending?.Cancel();pending?.Dispose();pending=new();var token=pending.Token;
   try{await Task.Delay(180,token);await search(token);}catch(OperationCanceledException){}
  };
  box.Unloaded+=(_,_)=>pending?.Cancel();
 }
}
