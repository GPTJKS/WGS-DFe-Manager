using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
namespace WgsDfe.Desktop;
internal static class SelectorSearch
{
 public static void Attach(ComboBox combo)
 {
  bool installed=false;
  combo.DropDownOpened+=(_,_)=>
  {
   if(installed||combo.Template.FindName("PART_Popup",combo) is not Popup popup||popup.Child is not Border border||border.Child is not UIElement original)return;
   installed=true;border.Child=null;
   var panel=new DockPanel();var box=new TextBox{MaxLength=200,Margin=new Thickness(0,0,0,6),ToolTip="Nomes aceitam pequenos erros; identificadores são literais. A seleção só muda ao escolher um resultado."};Ui.SetSearch(box,true);Ui.SetPlaceholder(box,"Pesquisar opções…");DockPanel.SetDock(box,Dock.Top);panel.Children.Add(box);
   var body=new Grid();var results=new ListBox{DisplayMemberPath=combo.DisplayMemberPath,ItemTemplate=combo.ItemTemplate,MaxHeight=240,Visibility=Visibility.Collapsed};body.Children.Add(original);body.Children.Add(results);panel.Children.Add(body);border.Child=panel;
   DebouncedSearch.Attach(box,async token=>
   {
    string query=box.Text;var items=combo.Items.Cast<object>().ToArray();
    var matches=await Task.Run(()=>items.Select(x=>{token.ThrowIfCancellationRequested();return(Item:x,Rank:ListSearch.Rank(x,query));}).Where(x=>x.Rank.HasValue).OrderBy(x=>x.Rank).Select(x=>x.Item).ToArray(),token);
    token.ThrowIfCancellationRequested();results.ItemsSource=matches;original.Visibility=query.Length==0?Visibility.Visible:Visibility.Collapsed;results.Visibility=query.Length==0?Visibility.Collapsed:Visibility.Visible;
   });
   void Choose(){if(results.SelectedItem is { } selected){combo.SelectedItem=selected;combo.IsDropDownOpen=false;combo.Focus();}}
   results.PreviewMouseLeftButtonUp+=(_,e)=>{if((e.OriginalSource as DependencyObject) is { } hit&&ItemsControl.ContainerFromElement(results,hit) is ListBoxItem item){results.SelectedItem=item.DataContext;Choose();}};
   box.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Down&&results.Items.Count>0){results.SelectedIndex=0;results.Focus();e.Handled=true;}else if(e.Key==Key.Escape){combo.IsDropDownOpen=false;e.Handled=true;}};
   results.PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Enter){Choose();e.Handled=true;}else if(e.Key==Key.Escape){combo.IsDropDownOpen=false;e.Handled=true;}};
  };
 }
}
