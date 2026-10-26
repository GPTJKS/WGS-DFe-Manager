using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;

/// <summary>Searches list metadata only. Passwords, dates and fiscal value inputs are never touched.</summary>
internal static class ListSearch
{
 public static void Attach(DataGrid grid)
 {
  var wrapper=new Grid();wrapper.RowDefinitions.Add(new(){Height=GridLength.Auto});wrapper.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
  var box=new TextBox{Margin=new Thickness(0,0,0,8),MaxLength=200,ToolTip="Nomes aceitam pequenos erros. CNPJ, CPF, chave e NSU têm correspondência literal."};Ui.SetSearch(box,true);Ui.SetPlaceholder(box,"Pesquisar nesta lista…");wrapper.Children.Add(box);
  if(grid.Parent is Panel parent){int index=parent.Children.IndexOf(grid);Grid.SetRow(wrapper,Grid.GetRow(grid));Grid.SetColumn(wrapper,Grid.GetColumn(grid));Grid.SetRowSpan(wrapper,Grid.GetRowSpan(grid));Grid.SetColumnSpan(wrapper,Grid.GetColumnSpan(grid));DockPanel.SetDock(wrapper,DockPanel.GetDock(grid));parent.Children.RemoveAt(index);parent.Children.Insert(index,wrapper);}
  else return;
  Grid.SetRow(grid,1);Grid.SetColumn(grid,0);Grid.SetRowSpan(grid,1);Grid.SetColumnSpan(grid,1);wrapper.Children.Add(grid);
  CancellationTokenSource? pending=null;bool listening=false;INotifyCollectionChanged? observed=null;
  NotifyCollectionChangedEventHandler? collectionChanged=null;
  var descriptor=DependencyPropertyDescriptor.FromProperty(ItemsControl.ItemsSourceProperty,typeof(DataGrid));
  async Task Refresh(bool immediate=false)
  {
   pending?.Cancel();pending?.Dispose();pending=new();var token=pending.Token;
   try
   {
    string query=box.Text;if(!immediate&&query.Length>0)await Task.Delay(180,token);
    if(grid.ItemsSource is not IEnumerable items)return;
    if(!ReferenceEquals(observed,items)){if(observed is not null)observed.CollectionChanged-=collectionChanged;observed=items as INotifyCollectionChanged;if(observed is not null)observed.CollectionChanged+=collectionChanged;}
    if(query.Length==0){var unfiltered=CollectionViewSource.GetDefaultView(grid.ItemsSource);using(unfiltered.DeferRefresh()){unfiltered.Filter=null;if(unfiltered is ListCollectionView unsorted)unsorted.CustomSort=null;}return;}
    var rows=items.Cast<object>().ToArray();
    var scores=await Task.Run(()=>
    {
     var ranks=new Dictionary<object,int>();foreach(var row in rows){token.ThrowIfCancellationRequested();var rank=Rank(row,query);if(rank.HasValue)ranks[row]=rank.Value;}return ranks;
    },token);
    if(token.IsCancellationRequested)return;var view=CollectionViewSource.GetDefaultView(grid.ItemsSource);
    using(view.DeferRefresh()){view.Filter=row=>scores.ContainsKey(row);if(view is ListCollectionView list)list.CustomSort=new RankedComparer(scores,rows);}
   }
   catch(OperationCanceledException){ }
  }
  EventHandler changed=(_,_)=>_ = Refresh(true);
  collectionChanged=(_,_)=>_ = Refresh(true);
  box.TextChanged+=(_,_)=>_ = Refresh();
  grid.Loaded+=(_,_)=>{if(!listening){descriptor.AddValueChanged(grid,changed);listening=true;}_ = Refresh(true);};
  grid.Unloaded+=(_,_)=>{pending?.Cancel();if(listening){descriptor.RemoveValueChanged(grid,changed);listening=false;}if(observed is not null){observed.CollectionChanged-=collectionChanged;observed=null;}};
 }
 internal static int? Rank(object row,string query)
 {
  var names=new List<string?>();var ids=new List<string?>();var numbers=new List<string?>();
  foreach(var property in row.GetType().GetProperties(BindingFlags.Public|BindingFlags.Instance).Where(x=>x.PropertyType==typeof(string)&&x.GetIndexParameters().Length==0))
  {
   if(property.Name is "Name" or "Label" or "LegalName" or "TradeName" or "Company" or "IssuerName" or "RecipientName" or "City" or "Output" or "Path")names.Add((string?)property.GetValue(row));
   else if(property.Name is "Cnpj" or "TaxId" or "IssuerTaxId" or "RecipientTaxId" or "Holder" or "AccessKey" or "Key" or "Nsu" or "Code" or "Provider" or "Source" or "EventTypeCode")ids.Add((string?)property.GetValue(row));
   else if(property.Name is "DocumentNumber" or "Number")numbers.Add((string?)property.GetValue(row));
  }
  // Fiscal document rows (with a document number) use precise numeric matching: 1103 finds Nº 000001103 only.
  return numbers.Count>0?SearchMatcher.RankFiscal(query,names,ids,numbers):SearchMatcher.Rank(query,names,ids);
 }
 private sealed class RankedComparer(Dictionary<object,int> scores,object[] rows):IComparer
 {
  private readonly Dictionary<object,int> order=rows.Select((row,index)=>(row,index)).GroupBy(x=>x.row).ToDictionary(x=>x.Key,x=>x.First().index);
  public int Compare(object? x,object? y){if(x is null||y is null)return 0;int rank=scores.GetValueOrDefault(x,int.MaxValue).CompareTo(scores.GetValueOrDefault(y,int.MaxValue));return rank!=0?rank:order.GetValueOrDefault(x).CompareTo(order.GetValueOrDefault(y));}
 }
}
