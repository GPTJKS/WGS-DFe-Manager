using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;
using WgsDfe.Infrastructure.Archive;

namespace WgsDfe.Desktop;

public partial class ArchiveWindow : WorkspaceView
{
 private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
 private readonly FoundationRuntime runtime;
 private bool busy;
 private CancellationTokenSource? cancellation;
 private int page = 1, last = 1;
 private ArchiveImportResult? report;

 public ArchiveWindow(FoundationRuntime runtime)
 {
  InitializeComponent();
  SelectorSearch.Attach(Company);
  WindowLayout.Attach(this);
  this.runtime = runtime;

  FirstLoaded += async (_, _) => await Run(async () =>
  {
   Company.ItemsSource = await Service(s => s.CompaniesAsync());
   Company.SelectedIndex = 0;
   var today = DateTime.Today;
   FinFrom.SelectedDate = new DateTime(today.Year, today.Month, 1);
   FinTo.SelectedDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
   await Load();
  });

  Closing += (_, e) =>
  {
   if (busy)
   {
    e.Cancel = true;
    Message.Text = "Aguarde a operação ou solicite o cancelamento.";
   }
  };
 }

 private Task<T> Service<T>(Func<IArchiveService, Task<T>> action) => Task.Run(async () =>
 {
  await using var scope = runtime.Services.CreateAsyncScope();
  return await action(scope.ServiceProvider.GetRequiredService<IArchiveService>());
 });

 private async Task Run(Func<Task> action, bool cancellable = false)
 {
  if (busy) return;
  busy = true;
  Toolbar.IsEnabled = Filters.IsEnabled = Pager.IsEnabled = Tabs.IsEnabled = false;
  BusyBar.Visibility = Visibility.Visible;
  CancelButton.IsEnabled = cancellable;
  cancellation = new();
  try
  {
   await action();
  }
  catch (ArchiveException e)
  {
   Message.Text = e.Message;
  }
  catch (OperationCanceledException)
  {
   Message.Text = "Operação cancelada. Os itens concluídos foram preservados.";
  }
  catch
  {
   Message.Text = "Não foi possível concluir. Verifique a pasta de dados, o espaço disponível e tente novamente.";
  }
  finally
  {
   busy = false;
   Toolbar.IsEnabled = Filters.IsEnabled = Pager.IsEnabled = Tabs.IsEnabled = true;
   BusyBar.Visibility = Visibility.Collapsed;
   CancelButton.IsEnabled = false;
   cancellation.Dispose();
   cancellation = null;
  }
 }

 private static int? Selected(ComboBox box) => int.TryParse(box.SelectedValue?.ToString(), out int value) ? value : null;

 private static decimal? Amount(TextBox box)
 {
  if (string.IsNullOrWhiteSpace(box.Text)) return null;
  if (!decimal.TryParse(box.Text, NumberStyles.Number, PtBr, out var value))
   throw new ArchiveException(ArchiveError.InvalidQuery);
  return value;
 }

 private ArchiveQuery Query()
 {
  if (!DateInput.Validate(From, To, out _))
   throw new ArchiveException(ArchiveError.InvalidQuery);

  return new(
   Search.Text,
   Company.SelectedItem is ArchiveCompany c ? c.Id : null,
   (DocumentModel?)Selected(Model),
   FiscalEnvironment.Production,
   (DocumentStatus?)Selected(Status),
   Issuer.Text,
   Recipient.Text,
   Number.Text,
   Series.Text,
   From.SelectedDate.HasValue ? DateOnly.FromDateTime(From.SelectedDate.Value) : null,
   To.SelectedDate.HasValue ? DateOnly.FromDateTime(To.SelectedDate.Value) : null,
   Amount(Minimum),
   Amount(Maximum),
   (ActorFilter?)Selected(Role),
   page);
 }

 private Task Load() => LoadSearch(default);

 private async Task LoadSearch(CancellationToken token)
 {
  var q = Query();
  var data = await Service(s => s.SearchAsync(q, token));
  var events = await Service(s => s.SearchEventsAsync(q.Search, q.Environment, q.Model, page, token));
  token.ThrowIfCancellationRequested();
  last = Math.Max(1, (Math.Max(data.Total, events.Total) + 49) / 50);
  Documents.ItemsSource = data.Items;
  Events.ItemsSource = events.Items;
  PageText.Text = $"{data.Total} documento(s) · {events.Total} evento(s) · Página {page}/{last}";
 }

 private async Task Import(IReadOnlyList<string> sources)
 {
  await Run(async () =>
  {
   var progress = new Progress<ArchiveProgress>(p =>
   {
    if (busy) Message.Text = $"Processados: {p.Processed} · Novos documentos/eventos: {p.Imported} · {p.CurrentSource}";
   });
   report = await Service(s => s.ImportAsync(new(sources), progress, cancellation!.Token));
   ImportRows.ItemsSource = report.Rows;
   page = 1;
   await Load();
   Tabs.SelectedIndex = 2;
   Message.Text = $"{report.Imported} novo(s), {report.Duplicates} idêntico(s), {report.Conflicts} conflito(s). " +
    (report.Failed ? "Interrompida por falha; itens concluídos preservados." :
     report.Cancelled ? "Cancelada; itens concluídos preservados." :
     "Importação concluída. Confira o resultado por arquivo.");
  }, true);
 }

 private void History_Click(object sender, RoutedEventArgs e) => WorkspaceNavigator.Open(new ExportHistoryView(runtime));

 private async void ImportFiles_Click(object sender, RoutedEventArgs e)
 {
  var file = new Microsoft.Win32.OpenFileDialog { Filter = "XML ou ZIP (*.xml;*.zip)|*.xml;*.zip", Multiselect = true };
  if (file.ShowDialog(Owner) == true) await Import(file.FileNames);
 }

 private async void ImportFolder_Click(object sender, RoutedEventArgs e)
 {
  var folder = new Microsoft.Win32.OpenFolderDialog { Title = "Importar pasta e subpastas", Multiselect = false };
  if (folder.ShowDialog(Owner) == true) await Import([folder.FolderName]);
 }

 private async void Integrity_Click(object sender, RoutedEventArgs e) => await Run(async () =>
 {
  var progress = new Progress<int>(n => { if (busy) Message.Text = $"Conferindo arquivo {n}..."; });
  var result = await Service(s => s.CheckIntegrityAsync(progress, cancellation!.Token));
  IntegrityRows.ItemsSource = result.Problems;
  Tabs.SelectedIndex = 3;
  Message.Text = $"Conferidos: {result.Checked} · Íntegros: {result.Valid} · Pendências: {result.Problems.Count} · Órfãos preservados: {result.Orphans}." +
   (result.Cancelled ? " Conferência cancelada." : "");
 }, true);

 private async void Links_Click(object sender, RoutedEventArgs e) => await Run(async () =>
 {
  int count = await Service(s => s.RefreshLinksAsync(cancellation!.Token));
  await Load();
  Message.Text = $"{count} vínculo(s) de empresa acrescentado(s).";
 }, true);

 private async void Details_Click(object sender, RoutedEventArgs e) => await Details();

 private async void Documents_DoubleClick(object sender, MouseButtonEventArgs e)
 {
  if (!busy) await Details();
 }

 private async Task Details()
 {
  if (Tabs.SelectedIndex == 1 && Events.SelectedItem is ArchiveEventRow ev)
  {
   await Run(async () =>
   {
    var rows = await Service(s => s.EventFilesAsync(ev.Id));
    WorkspaceNavigator.Open(new ArchiveDetailWindow(runtime, $"{ev.CompanyLabel} · {ev.TypeLabel} · Seq. {ev.Sequence} · {ev.EnvironmentLabel}\nChave: {ev.Key}", rows, []));
   });
   return;
  }
  if (Tabs.SelectedIndex != 0)
  {
   Message.Text = "Selecione um evento na aba Eventos ou um documento na aba Documentos.";
   return;
  }
  if (Documents.SelectedItem is not ArchiveDocumentRow doc)
  {
   Message.Text = "Selecione um documento ou evento.";
   return;
  }
  await Run(async () =>
  {
   var detail = await Service(s => s.DetailAsync(doc.Id));
   WorkspaceNavigator.Open(new ArchiveDetailWindow(runtime, $"{doc.ModelLabel} Nº {doc.Number} (Série {doc.Series}) · {doc.EnvironmentLabel} · {doc.StatusLabel} · Chave: {doc.Key} · Emitente: {doc.IssuerName} ({doc.Issuer}) → Destinatário: {doc.RecipientName} ({doc.Recipient})", detail.Files, detail.Events));
  });
 }

 private async void ExportFilters_Click(object sender, RoutedEventArgs e) => await Run(() =>
 {
  WorkspaceNavigator.Open(new ExportWindow(runtime, Query()));
  return Task.CompletedTask;
 });

 private async void ExportSelected_Click(object sender, RoutedEventArgs e)
 {
  if (Tabs.SelectedIndex == 0 && Documents.SelectedItems.Count > 0)
  {
   await Run(() =>
   {
    WorkspaceNavigator.Open(new ExportWindow(runtime, Query(), Documents.SelectedItems.Cast<ArchiveDocumentRow>().Select(x => x.Id).ToArray()));
    return Task.CompletedTask;
   });
  }
  else if (Tabs.SelectedIndex == 1 && Events.SelectedItems.Count > 0)
  {
   await Run(() =>
   {
    var q = Query();
    WorkspaceNavigator.Open(new ExportWindow(runtime, new ArchiveQuery(Search: q.Search, Model: q.Model, Environment: q.Environment), events: Events.SelectedItems.Cast<ArchiveEventRow>().Select(x => x.Id).ToArray()));
    return Task.CompletedTask;
   });
  }
  else Message.Text = "Selecione notas ou eventos no acervo.";
 }

 private async void Report_Click(object sender, RoutedEventArgs e)
 {
  if (report is null)
  {
   Message.Text = "Importe arquivos para gerar o relatório desta sessão.";
   return;
  }
  var file = new Microsoft.Win32.SaveFileDialog { Filter = "Relatório CSV (*.csv)|*.csv", FileName = "Relatorio_Importacao_Acervo.csv" };
  if (file.ShowDialog(Owner) == true)
  {
   await Run(async () =>
   {
    await ArchiveReport.SaveAsync(report, file.FileName);
    Message.Text = "Relatório salvo.";
   });
  }
 }

 private async void Search_Click(object sender, RoutedEventArgs e)
 {
  page = 1;
  await Run(Load);
 }

 private async void Search_KeyDown(object sender, KeyEventArgs e)
 {
  if (e.Key == Key.Enter)
  {
   e.Handled = true;
   page = 1;
   await Run(Load);
  }
 }

 private async void Clear_Click(object sender, RoutedEventArgs e)
 {
  Search.Clear();
  Issuer.Clear();
  Recipient.Clear();
  Number.Clear();
  Series.Clear();
  Minimum.Clear();
  Maximum.Clear();
  From.SelectedDate = To.SelectedDate = null;
  Company.SelectedIndex = Model.SelectedIndex = EnvironmentFilter.SelectedIndex = Status.SelectedIndex = Role.SelectedIndex = 0;
  page = 1;
  await Run(Load);
 }

 private async void Previous_Click(object sender, RoutedEventArgs e)
 {
  page = Math.Max(1, page - 1);
  await Run(Load);
 }

 private async void Next_Click(object sender, RoutedEventArgs e)
 {
  page = Math.Min(last, page + 1);
  await Run(Load);
 }

 private void Cancel_Click(object sender, RoutedEventArgs e)
 {
  cancellation?.Cancel();
  CancelButton.IsEnabled = false;
  Message.Text = "Cancelamento solicitado. Aguardando a operação atual...";
 }

 // UI Toggle Handlers
 private void AdvancedDiagnosticToggle_Click(object sender, RoutedEventArgs e)
 {
  bool show = AdvancedDiagnosticToggle.IsChecked == true;
  AdvancedDiagnosticPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
 }

 private void AdvancedFiltersToggle_Click(object sender, RoutedEventArgs e)
 {
  bool show = AdvancedFiltersToggle.IsChecked == true;
  AdvancedFiltersPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
 }

 private void CloseAdvancedFilters_Click(object sender, RoutedEventArgs e)
 {
  AdvancedFiltersToggle.IsChecked = false;
  AdvancedFiltersPanel.Visibility = Visibility.Collapsed;
 }

 private async void FinancialVolumeToggle_Click(object sender, RoutedEventArgs e)
 {
  bool show = FinancialVolumeToggle.IsChecked == true;
  FinancialVolumePanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
  if (show)
  {
   await CalculateFinancialVolumeAsync();
  }
 }

 private void CloseFinancialVolume_Click(object sender, RoutedEventArgs e)
 {
  FinancialVolumeToggle.IsChecked = false;
  FinancialVolumePanel.Visibility = Visibility.Collapsed;
 }

 private void CloseInspector_Click(object sender, RoutedEventArgs e)
 {
  Documents.SelectedItem = null;
 }
 internal bool CloseInspector()
 {
  if(Documents.SelectedItem is null)return false;
  Documents.SelectedItem=null;return true;
 }

 // Financial Volume Calculation
 private async void CalculateFinancialVolume_Click(object sender, RoutedEventArgs e)
 {
  await CalculateFinancialVolumeAsync();
 }

 private async Task CalculateFinancialVolumeAsync()
 {
  await Run(async () =>
  {
   DateOnly? fromDate = FinFrom.SelectedDate.HasValue ? DateOnly.FromDateTime(FinFrom.SelectedDate.Value) : (From.SelectedDate.HasValue ? DateOnly.FromDateTime(From.SelectedDate.Value) : null);
   DateOnly? toDate = FinTo.SelectedDate.HasValue ? DateOnly.FromDateTime(FinTo.SelectedDate.Value) : (To.SelectedDate.HasValue ? DateOnly.FromDateTime(To.SelectedDate.Value) : null);

   if (fromDate.HasValue && toDate.HasValue && fromDate.Value > toDate.Value)
   {
    Message.Text = "A data inicial não pode ser superior à data final para apuração do volume financeiro.";
    return;
   }

   Guid? companyId = Company.SelectedItem is ArchiveCompany c ? c.Id : null;
   var model = (DocumentModel?)Selected(Model);
   var env = FiscalEnvironment.Production;
   var role = (ActorFilter?)Selected(Role);

   var finQuery = new ArchiveFinancialQuery(
    Search: Search.Text,
    CompanyId: companyId,
    Model: model,
    Environment: env,
    IssuerTaxId: Issuer.Text,
    RecipientTaxId: Recipient.Text,
    From: fromDate,
    To: toDate,
    Role: role);

   var summary = await Service(s => s.FinancialSummaryAsync(finQuery, cancellation?.Token ?? default));

   FinGrossValue.Text = $"R$ {summary.GrossAuthorized.ToString("N2", PtBr)}";
   FinGrossBadge.Text = $"{summary.AuthorizedCount:N0} autorizado(s)";

   FinCancelledValue.Text = $"R$ {summary.TotalCancelled.ToString("N2", PtBr)}";
   FinCancelledBadge.Text = $"{summary.CancelledCount:N0} cancelado(s)";

   FinNetValue.Text = $"R$ {summary.NetAmount.ToString("N2", PtBr)}";
   decimal retentionRate = summary.GrossAuthorized > 0 ? (summary.NetAmount / summary.GrossAuthorized) * 100m : 100m;
   FinNetBadge.Text = summary.GrossAuthorized > 0 ? $"{retentionRate:N1}% líquido" : "Líquido efetivo";

   Message.Text = $"Volume financeiro apurado: {summary.AuthorizedCount} autorizado(s) (R$ {summary.GrossAuthorized:N2}), {summary.CancelledCount} cancelado(s) (R$ {summary.TotalCancelled:N2}). Líquido: R$ {summary.NetAmount:N2}.";
  });
 }

 // Financial Volume Quick Ranges
 private async void FinRange_Today_Click(object sender, RoutedEventArgs e)
 {
  var today = DateTime.Today;
  FinFrom.SelectedDate = today;
  FinTo.SelectedDate = today;
  await CalculateFinancialVolumeAsync();
 }

 private async void FinRange_ThisMonth_Click(object sender, RoutedEventArgs e)
 {
  var today = DateTime.Today;
  FinFrom.SelectedDate = new DateTime(today.Year, today.Month, 1);
  FinTo.SelectedDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
  await CalculateFinancialVolumeAsync();
 }

 private async void FinRange_LastMonth_Click(object sender, RoutedEventArgs e)
 {
  var firstOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
  var lastMonth = firstOfThisMonth.AddMonths(-1);
  FinFrom.SelectedDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
  FinTo.SelectedDate = new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
  await CalculateFinancialVolumeAsync();
 }

 private async void FinRange_Last30Days_Click(object sender, RoutedEventArgs e)
 {
  FinFrom.SelectedDate = DateTime.Today.AddDays(-30);
  FinTo.SelectedDate = DateTime.Today;
  await CalculateFinancialVolumeAsync();
 }

 private async void FinRange_All_Click(object sender, RoutedEventArgs e)
 {
  FinFrom.SelectedDate = null;
  FinTo.SelectedDate = null;
  await CalculateFinancialVolumeAsync();
 }

 // Advanced Filters Date Presets
 private void FilterRange_Today_Click(object sender, RoutedEventArgs e)
 {
  var today = DateTime.Today;
  From.SelectedDate = today;
  To.SelectedDate = today;
 }

 private void FilterRange_ThisMonth_Click(object sender, RoutedEventArgs e)
 {
  var today = DateTime.Today;
  From.SelectedDate = new DateTime(today.Year, today.Month, 1);
  To.SelectedDate = new DateTime(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
 }

 private void FilterRange_LastMonth_Click(object sender, RoutedEventArgs e)
 {
  var firstOfThisMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
  var lastMonth = firstOfThisMonth.AddMonths(-1);
  From.SelectedDate = new DateTime(lastMonth.Year, lastMonth.Month, 1);
  To.SelectedDate = new DateTime(lastMonth.Year, lastMonth.Month, DateTime.DaysInMonth(lastMonth.Year, lastMonth.Month));
 }

 private void FilterRange_ClearDates_Click(object sender, RoutedEventArgs e)
 {
  From.SelectedDate = null;
  To.SelectedDate = null;
 }
 private void CopyInspectorKey_Click(object sender,RoutedEventArgs e)
 {if(sender is FrameworkElement {DataContext:ArchiveDocumentRow row})FiscalClipboard.CopyKey(row.Key,message=>Message.Text=message);}}
