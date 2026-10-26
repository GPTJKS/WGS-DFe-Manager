using System.Windows.Controls;
using WgsDfe.Application.Cte;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
public partial class CteWindow
{
 private IReadOnlyList<CteCompany> companyRows=Array.Empty<CteCompany>();
 private readonly HashSet<Guid> selectedCompanies=new();
 private bool replacingCompanies;private bool showSelectedOnly;
 private CancellationTokenSource? companySearchCancellation;
 private async void CompanySearch_Changed(object sender,TextChangedEventArgs e)=>await ApplyCompanySearchAsync();
 private async Task ApplyCompanySearchAsync(bool debounce=true)
 {
  if(CompaniesGrid is null)return;
  companySearchCancellation?.Cancel();companySearchCancellation?.Dispose();companySearchCancellation=new();
  var token=companySearchCancellation.Token;var term=CompanySearch.Text;var all=showSelectedOnly?companyRows.Where(x=>selectedCompanies.Contains(x.Id)).ToArray():companyRows;
  try
  {
   if(debounce&&term.Length>0)await Task.Delay(180,token);
   var rows=term.Length==0?all.ToArray():await Task.Run(()=>all.Select(x=>{token.ThrowIfCancellationRequested();return(Row:x,Rank:SearchMatcher.Rank(term,[x.Name],[x.Cnpj]));}).Where(x=>x.Rank.HasValue).OrderBy(x=>x.Rank).ThenBy(x=>x.Row.Name).Select(x=>x.Row).ToArray(),token);
   if(token.IsCancellationRequested)return;replacingCompanies=true;
   try{CompaniesGrid.ItemsSource=rows;foreach(var row in rows.Where(x=>selectedCompanies.Contains(x.Id)))CompaniesGrid.SelectedItems.Add(row);}
   finally{replacingCompanies=false;}
   UpdateSelectionButton();
  }
  catch(OperationCanceledException){ }
 }
 private void Companies_Changed(object sender,SelectionChangedEventArgs e)
 {
  if(replacingCompanies)return;
  foreach(var row in e.RemovedItems.OfType<CteCompany>())selectedCompanies.Remove(row.Id);
  foreach(var row in e.AddedItems.OfType<CteCompany>())selectedCompanies.Add(row.Id);
  Period?.Invalidate();UpdateSelectionButton();
  if(showSelectedOnly&&e.RemovedItems.Count>0)Dispatcher.BeginInvoke(new Action(async()=>await ApplyCompanySearchAsync(false)));
 }
 private async void ShowSelected_Click(object sender,System.Windows.RoutedEventArgs e)
 {
  if(busy)return;showSelectedOnly=!showSelectedOnly;
  ShowSelectedButton.Content=showSelectedOnly?"Mostrar todos":"Mostrar selecionados";
  await ApplyCompanySearchAsync(false);
 }}
