using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WgsDfe.Application.Archive;
using WgsDfe.Application.Certificates;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure;

namespace WgsDfe.Desktop;

public sealed class HomeDashboardView : WorkspaceView
{
 private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
 private readonly FoundationRuntime runtime;
 private readonly Action<string> navigate;

 private readonly TextBlock orgTitleText = new() { FontSize = 11.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0xC2, 0x6A, 0x1B)) };
 private readonly TextBlock kpiCompaniesValue = new() { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)) };
 private readonly TextBlock kpiCompaniesSub = new() { Text = "Carregando carteira…", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x60, 0x59)) };
 private readonly TextBlock kpiDocsValue = new() { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)) };
 private readonly TextBlock kpiDocsSub = new() { Text = "Carregando acervo…", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x60, 0x59)) };
 private readonly TextBlock kpiCertsValue = new() { Text = "—", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)) };
 private readonly TextBlock kpiCertsSub = new() { Text = "Verificando cofre A1…", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x60, 0x59)) };
 private readonly DataGrid recentGrid = new()
 {
  IsReadOnly = true,
  AutoGenerateColumns = false,
  CanUserAddRows = false,
  CanUserDeleteRows = false,
  SelectionMode = DataGridSelectionMode.Single,
  RowHeight = 38,
  BorderThickness = new Thickness(0),
  MinHeight = 190
 };
 private readonly StackPanel governanceList = new();
 private bool busy;

 private static Thickness Pad(double h, double v) => new(h, v, h, v);

 public HomeDashboardView(FoundationRuntime runtime, Action<string> navigate)
 {
  this.runtime = runtime;
  this.navigate = navigate;
  Title = "Início · Painel Executivo";
  Background = (Brush)FindResource("CanvasBrush");

  var outerScroll = new ScrollViewer
  {
   VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
   HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
  };
  var root = new StackPanel { Margin = new Thickness(16, 10, 16, 12) };

  // 1. HERO EXECUTIVE BANNER (Warm Champagne Gold & Alabaster Stone)
  var heroBg = new LinearGradientBrush(
   Color.FromRgb(0xF8, 0xF3, 0xE8),
   Color.FromRgb(0xF1, 0xE8, 0xD6),
   new Point(0, 0),
   new Point(1, 1));
  var hero = new Border
  {
   Background = heroBg,
   BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xC2, 0x97)),
   BorderThickness = new Thickness(1),
   CornerRadius = new CornerRadius(14),
   Padding = Pad(18, 12),
   Margin = new Thickness(0, 0, 0, 10)
  };
  var heroGrid = new Grid();
  heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
  heroGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

  var heroLeft = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
  var logoBorder = new Border
  {
   Width = 48,
   Height = 48,
   CornerRadius = new CornerRadius(12),
   Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF6, 0xEE)),
   BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)),
   BorderThickness = new Thickness(1.5),
   Margin = new Thickness(0, 0, 14, 0),
   ClipToBounds = true,
   VerticalAlignment = VerticalAlignment.Center
  };
  try
  {
   logoBorder.Child = new Image
   {
    Source = new BitmapImage(new Uri("pack://application:,,,/Assets/wgs-icon.png")),
    Stretch = Stretch.UniformToFill
   };
  }
  catch { }
  DockPanel.SetDock(logoBorder, Dock.Left);
  heroLeft.Children.Add(logoBorder);

  var heroTitles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
  var eyebrowRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
  eyebrowRow.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
  eyebrowRow.Children.Add(new TextBlock { Text = "CENTRAL EXECUTIVA DE GOVERNANÇA E DOCUMENTOS FISCAIS · ", FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0x68, 0x62, 0x57)), VerticalAlignment = VerticalAlignment.Center });
  eyebrowRow.Children.Add(orgTitleText);
  heroTitles.Children.Add(eyebrowRow);

  heroTitles.Children.Add(new TextBlock
  {
   Text = "WGS DF-e Manager 0.17.5 — Plataforma Corporativa",
   FontFamily = Typography.Ui,
   FontSize = 19,
   FontWeight = FontWeights.Bold,
   Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B))
  });
  heroTitles.Children.Add(new TextBlock
  {
   Text = "Sincronismo nacional SEFAZ, custódia íntegra de XMLs, espelho gráfico de notas fiscais (DANFE/DACTE) e recuperação histórica em lote.",
   FontSize = 12,
   Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x4A, 0x43)),
   TextWrapping = TextWrapping.Wrap,
   Margin = new Thickness(0, 2, 0, 0)
  });
  heroLeft.Children.Add(heroTitles);
  Grid.SetColumn(heroLeft, 0);
  heroGrid.Children.Add(heroLeft);

  var heroActions = new WrapPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 0) };
  var startQueryBtn = new Button
  {
   Content = "Consultar SEFAZ Agora",
   Background = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)),
   Foreground = Brushes.White,
   BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)),
   Padding = Pad(14, 6),
   Margin = new Thickness(0, 0, 8, 0),
   FontWeight = FontWeights.Bold
  };
  Ui.SetIcon(startQueryBtn, "\uE721");
  startQueryBtn.Click += (_, _) => navigate("consultas");
  heroActions.Children.Add(startQueryBtn);

  var refreshBtn = new Button
  {
   Content = "Atualizar painel",
   Padding = Pad(12, 6),
   Margin = new Thickness(0)
  };
  Ui.SetIcon(refreshBtn, "\uE72C");
  refreshBtn.Click += async (_, _) => await RefreshDashboardAsync();
  heroActions.Children.Add(refreshBtn);
  Grid.SetColumn(heroActions, 1);
  heroGrid.Children.Add(heroActions);
  hero.Child = heroGrid;
  root.Children.Add(hero);

  // 2. ROW OF 3 KPI CARDS
  var kpiGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 10) };
  kpiGrid.Children.Add(BuildKpiCard("\uEC06", "EMPRESAS CADASTRADAS", kpiCompaniesValue, kpiCompaniesSub, "Carteira", Color.FromRgb(0xEC, 0xFD, 0xF5), Color.FromRgb(0x04, 0x78, 0x57), () => navigate("empresas"), true));
  kpiGrid.Children.Add(BuildKpiCard("\uE8A5", "DOCUMENTOS NO ACERVO", kpiDocsValue, kpiDocsSub, "Repositório XML", Color.FromRgb(0xEE, 0xF2, 0xFF), Color.FromRgb(0x37, 0x30, 0xA3), () => navigate("documentos"), true));
  kpiGrid.Children.Add(BuildKpiCard("\uE83D", "SAÚDE DE CERTIFICADOS A1", kpiCertsValue, kpiCertsSub, "Cofre Digital", Color.FromRgb(0xFA, 0xF5, 0xFF), Color.FromRgb(0x6D, 0x28, 0xD9), () => navigate("certificados"), false));
  root.Children.Add(kpiGrid);

  // 3. MÓDULOS OPERACIONAIS (4 QUICK LAUNCH CARDS)
  var modulesHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
  modulesHeader.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
  modulesHeader.Children.Add(new TextBlock { Text = "ACESSO RÁPIDO AOS MÓDULOS DO SISTEMA", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0) });
  root.Children.Add(modulesHeader);

  var modulesGrid = new UniformGrid { Columns = 4, Margin = new Thickness(0, 0, 0, 10) };
  modulesGrid.Children.Add(BuildModuleCard(
   "\uE721",
   "Consultas & Downloads",
   "Distribuição nacional SEFAZ e SEF/SC para NF-e, NFC-e, CT-e e NFCom com diagnóstico assistido.",
   "Abrir Consultas →",
   () => navigate("consultas"),
   true));
  modulesGrid.Children.Add(BuildModuleCard(
   "\uEC06",
   "Empresas & Filiais",
   "Cadastro central de emitentes, importação em lote por planilha e vínculo automático com certificados A1.",
   "Gerenciar Empresas →",
   () => navigate("empresas"),
   true));
  modulesGrid.Children.Add(BuildModuleCard(
   "\uE8A5",
   "Documentos & DANFE",
   "Repositório local com busca avançada, visualizador gráfico de Nota Fiscal (tributação completa) e exportação.",
   "Explorar Acervo →",
   () => navigate("documentos"),
   true));
  modulesGrid.Children.Add(BuildModuleCard(
   "\uE81C",
   "Recuperação Histórica",
   "Auditoria de chaves de 44 dígitos em lote (XLSX/CSV/TXT), cruzamento local e retomada inteligente.",
   "Recuperar Chaves →",
   () => navigate("recuperacao"),
   false));
  root.Children.Add(modulesGrid);

  // 4. BOTTOM SPLIT: ÚLTIMOS DOCUMENTOS & GOVERNANÇA CORPORATIVA
  var bottomGrid = new Grid();
  bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.65, GridUnitType.Star) });
  bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
  bottomGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

  // Left: Recent Documents
  var recentCard = new Border { Style = (Style)FindResource("Card"), Padding = Pad(12, 10) };
  var recentDock = new DockPanel();
  var recentTop = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
  var recentActions = new StackPanel { Orientation = Orientation.Horizontal };
  var viewNoteBtn = new Button { Content = "Visualizar Nota (DANFE)", Style = (Style)FindResource("PrimaryButton"), Padding = Pad(10, 4), Margin = new Thickness(0, 0, 6, 0) };
  Ui.SetIcon(viewNoteBtn, "\uE8A5");
  viewNoteBtn.Click += async (_, _) => await OpenSelectedDocumentAsync();
  recentActions.Children.Add(viewNoteBtn);
  var allDocsBtn = new Button { Content = "Ver todos", Padding = Pad(10, 4), Margin = new Thickness(0) };
  allDocsBtn.Click += (_, _) => navigate("documentos");
  recentActions.Children.Add(allDocsBtn);
  DockPanel.SetDock(recentActions, Dock.Right);
  recentTop.Children.Add(recentActions);

  var recentTitles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
  recentTitles.Children.Add(new TextBlock { Text = "ÚLTIMOS DOCUMENTOS FISCAIS NO ACERVO", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0) });
  recentTitles.Children.Add(new TextBlock { Text = "Dê duplo clique ou clique em Visualizar Nota para abrir o espelho gráfico (DANFE / DACTE)", Style = (Style)FindResource("Caption") });
  recentTop.Children.Add(recentTitles);
  DockPanel.SetDock(recentTop, Dock.Top);
  recentDock.Children.Add(recentTop);

  ConfigureRecentGridColumns();
  recentGrid.MouseDoubleClick += async (_, _) => await OpenSelectedDocumentAsync();
  recentDock.Children.Add(recentGrid);
  recentCard.Child = recentDock;
  Grid.SetColumn(recentCard, 0);
  bottomGrid.Children.Add(recentCard);

  // Right: Governance & Corporate Tools
  var govCard = new Border { Style = (Style)FindResource("Card"), Padding = Pad(14, 10) };
  var govStack = new StackPanel();
  govStack.Children.Add(new TextBlock { Text = "AMBIENTE CORPORATIVO & FERRAMENTAS", Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0, 0, 0, 2) });
  govStack.Children.Add(new TextBlock { Text = "Governança, Auditoria e Utilitários", FontWeight = FontWeights.Bold, FontSize = 13.5, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)), Margin = new Thickness(0, 0, 0, 8) });
  govStack.Children.Add(governanceList);

  var utilWrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
  var inspectBtn = new Button { Content = "Inspecionar XML Avulso", Padding = Pad(10, 5), Margin = new Thickness(0, 0, 6, 6) };
  Ui.SetIcon(inspectBtn, "\uE943");
  inspectBtn.Click += (_, _) => navigate("inspecionar");
  utilWrap.Children.Add(inspectBtn);

  var settingsBtn = new Button { Content = "Configurações & Licença", Padding = Pad(10, 5), Margin = new Thickness(0, 0, 6, 6) };
  Ui.SetIcon(settingsBtn, "\uE713");
  settingsBtn.Click += (_, _) => navigate("configuracoes");
  utilWrap.Children.Add(settingsBtn);

  var aboutBtn = new Button { Content = "Sobre a Plataforma", Padding = Pad(10, 5), Margin = new Thickness(0, 0, 0, 6) };
  Ui.SetIcon(aboutBtn, "\uE946");
  aboutBtn.Click += (_, _) => navigate("sobre");
  utilWrap.Children.Add(aboutBtn);

  govStack.Children.Add(utilWrap);
  govCard.Child = govStack;
  Grid.SetColumn(govCard, 2);
  bottomGrid.Children.Add(govCard);

  root.Children.Add(bottomGrid);
  outerScroll.Content = root;
  Content = outerScroll;

  Loaded += async (_, _) => await RefreshDashboardAsync();
 }

 private Border BuildKpiCard(string glyph, string title, TextBlock valueBlock, TextBlock subBlock, string pillText, Color badgeBg, Color badgeFg, Action onClick, bool rightMargin)
 {
  var card = new Border
  {
   Style = (Style)FindResource("Card"),
   Padding = Pad(12, 10),
   Margin = new Thickness(0, 0, rightMargin ? 8 : 0, 0),
   Cursor = Cursors.Hand
  };
  card.MouseLeftButtonUp += (_, _) => onClick();
  var dock = new DockPanel();
  var pill = new Border
  {
   Background = new SolidColorBrush(badgeBg),
   CornerRadius = new CornerRadius(6),
   Padding = Pad(7, 2),
   VerticalAlignment = VerticalAlignment.Top
  };
  pill.Child = new TextBlock { Text = pillText, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(badgeFg) };
  DockPanel.SetDock(pill, Dock.Right);
  dock.Children.Add(pill);

  var iconBox = new Border
  {
   Width = 34,
   Height = 34,
   CornerRadius = new CornerRadius(9),
   Background = new SolidColorBrush(badgeBg),
   Margin = new Thickness(0, 0, 10, 0),
   VerticalAlignment = VerticalAlignment.Center
  };
  iconBox.Child = new TextBlock
  {
   Text = glyph,
   FontFamily = new FontFamily("Segoe MDL2 Assets"),
   FontSize = 15,
   Foreground = new SolidColorBrush(badgeFg),
   HorizontalAlignment = HorizontalAlignment.Center,
   VerticalAlignment = VerticalAlignment.Center
  };
  DockPanel.SetDock(iconBox, Dock.Left);
  dock.Children.Add(iconBox);

  var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
  stack.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Eyebrow"), Margin = new Thickness(0) });
  stack.Children.Add(valueBlock);
  stack.Children.Add(subBlock);
  dock.Children.Add(stack);
  card.Child = dock;
  return card;
 }

 private Border BuildModuleCard(string glyph, string title, string description, string cta, Action onClick, bool rightMargin)
 {
  var card = new Border
  {
   Style = (Style)FindResource("Card"),
   Padding = Pad(13, 10),
   Margin = new Thickness(0, 0, rightMargin ? 8 : 0, 0),
   Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF9, 0xF6))
  };
  var stack = new DockPanel();
  var btn = new Button
  {
   Content = cta,
   HorizontalAlignment = HorizontalAlignment.Left,
   Padding = Pad(10, 4),
   Margin = new Thickness(0, 8, 0, 0),
   FontSize = 11.5
  };
  btn.Click += (_, _) => onClick();
  DockPanel.SetDock(btn, Dock.Bottom);
  stack.Children.Add(btn);

  var body = new StackPanel();
  var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
  var badge = new Border
  {
   Width = 26,
   Height = 26,
   CornerRadius = new CornerRadius(7),
   Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xF3, 0xE6)),
   Margin = new Thickness(0, 0, 8, 0)
  };
  badge.Child = new TextBlock
  {
   Text = glyph,
   FontFamily = new FontFamily("Segoe MDL2 Assets"),
   FontSize = 13,
   Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)),
   HorizontalAlignment = HorizontalAlignment.Center,
   VerticalAlignment = VerticalAlignment.Center
  };
  topRow.Children.Add(badge);
  topRow.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)), VerticalAlignment = VerticalAlignment.Center });
  body.Children.Add(topRow);
  body.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Caption"), TextWrapping = TextWrapping.Wrap, FontSize = 11.5 });
  stack.Children.Add(body);
  card.Child = stack;
  return card;
 }

 private void ConfigureRecentGridColumns()
 {
  var cellStyle = (Style)FindResource("CellText");
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "MODELO", Binding = new System.Windows.Data.Binding("ModelLabel"), Width = 72, MinWidth = 65, ElementStyle = cellStyle });
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "EMISSÃO", Binding = new System.Windows.Data.Binding("IssueDateLabel"), Width = 88, MinWidth = 82, ElementStyle = cellStyle });
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "Nº", Binding = new System.Windows.Data.Binding("Number"), Width = 82, MinWidth = 74, ElementStyle = cellStyle });
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "EMITENTE", Binding = new System.Windows.Data.Binding("IssuerName"), Width = new DataGridLength(1, DataGridLengthUnitType.Star), MinWidth = 140, ElementStyle = cellStyle });
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "TOTAL (R$)", Binding = new System.Windows.Data.Binding("Total") { StringFormat = "N2" }, Width = 96, MinWidth = 85, ElementStyle = cellStyle });
  recentGrid.Columns.Add(new DataGridTextColumn { Header = "SITUAÇÃO", Binding = new System.Windows.Data.Binding("StatusLabel"), Width = 130, MinWidth = 115, ElementStyle = cellStyle });
 }

 public async Task RefreshDashboardAsync()
 {
  if (busy) return;
  busy = true;
  try
  {
   var prefs = UserPreferences.Load(runtime.DataDirectory);
   string licensedOrg = !string.IsNullOrWhiteSpace(prefs.LicensedOrganization) ? prefs.LicensedOrganization : "WGS Assessoria Contábil";
   orgTitleText.Text = $"AMBIENTE LICENCIADO: {licensedOrg.ToUpperInvariant()}";

   var stats = await Task.Run(async () =>
   {
    await using var scope = runtime.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<WgsDbContext>();
    var archive = scope.ServiceProvider.GetRequiredService<IArchiveService>();
    var certSvc = scope.ServiceProvider.GetRequiredService<ICertificateService>();

    int totalCompanies = await db.Companies.AsNoTracking().CountAsync();
    int activeCompanies = await db.Companies.AsNoTracking().CountAsync(c => c.IsActive);
    int totalDocs = await db.FiscalDocuments.AsNoTracking().CountAsync();
    int totalEvents = await db.FiscalDocumentEvents.AsNoTracking().CountAsync();
    int totalBatches = await db.HistoricalRecoveryBatches.AsNoTracking().CountAsync();
    int activeSchedules = await db.SyncSchedules.AsNoTracking().CountAsync(s => s.Enabled);

    var certs = await certSvc.ListAsync();
    int certAlerts = certs.Count(x => x.ValidityAt(DateTime.UtcNow) != CertificateValidity.Valid);
    var recentPage = await archive.SearchAsync(new ArchiveQuery(Page: 1, PageSize: 8));

    return (totalCompanies, activeCompanies, totalDocs, totalEvents, totalBatches, activeSchedules, certsCount: certs.Count, certAlerts, recent: recentPage.Items);
   });

   kpiCompaniesValue.Text = $"{stats.activeCompanies} ativas";
   kpiCompaniesSub.Text = $"{stats.totalCompanies} empresa(s) cadastrada(s) no total";

   kpiDocsValue.Text = stats.totalDocs.ToString("N0", PtBr);
   kpiDocsSub.Text = $"{stats.totalEvents:N0} evento(s) fiscal(is) vinculado(s)";
   kpiCertsValue.Text = stats.certsCount == 0 ? "0 A1" : stats.certAlerts > 0 ? $"{stats.certAlerts} atenção" : $"{stats.certsCount} em dia";
   kpiCertsSub.Text = stats.certsCount == 0 ? "Adicione certificados em Certificados" : $"{stats.certsCount} certificado(s) A1 no cofre";

   recentGrid.ItemsSource = stats.recent;

   governanceList.Children.Clear();
   governanceList.Children.Add(BuildGovernanceRow("Custódia e Integridade SHA-256", $"{stats.totalDocs:N0} XMLs originais preservados localmente", true));
   governanceList.Children.Add(BuildGovernanceRow("Lotes de Recuperação Histórica", $"{stats.totalBatches} lote(s) auditado(s) no sistema", true));
   governanceList.Children.Add(BuildGovernanceRow("Agendamentos Automáticos", stats.activeSchedules > 0 ? $"{stats.activeSchedules} rotina(s) ativa(s)" : "Consultas sob demanda habilitadas", true));
   governanceList.Children.Add(BuildGovernanceRow("Cofre Criptografado DPAPI (Windows)", stats.certAlerts == 0 ? "Todos os certificados vigentes" : $"{stats.certAlerts} certificado(s) requer(em) revisão", stats.certAlerts == 0));
  }
  catch
  {
   kpiCompaniesSub.Text = "Resumo indisponível no momento.";
  }
  finally
  {
   busy = false;
  }
 }

 private static Border BuildGovernanceRow(string label, string detail, bool ok)
 {
  var bd = new Border
  {
   Background = new SolidColorBrush(Color.FromRgb(0xF7, 0xF6, 0xF2)),
   BorderBrush = new SolidColorBrush(Color.FromRgb(0xE6, 0xE4, 0xDD)),
   BorderThickness = new Thickness(1),
   CornerRadius = new CornerRadius(8),
   Padding = Pad(10, 6),
   Margin = new Thickness(0, 0, 0, 5)
  };
  var dock = new DockPanel();
  var dot = new Ellipse
  {
   Width = 7,
   Height = 7,
   Fill = new SolidColorBrush(ok ? Color.FromRgb(0x10, 0xB9, 0x81) : Color.FromRgb(0xE0, 0x8A, 0x34)),
   VerticalAlignment = VerticalAlignment.Center,
   Margin = new Thickness(0, 0, 8, 0)
  };
  DockPanel.SetDock(dot, Dock.Left);
  dock.Children.Add(dot);

  var st = new StackPanel();
  st.Children.Add(new TextBlock { Text = label, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)) });
  st.Children.Add(new TextBlock { Text = detail, FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x60, 0x59)) });
  dock.Children.Add(st);
  bd.Child = dock;
  return bd;
 }

 private async Task OpenSelectedDocumentAsync()
 {
  if (busy || recentGrid.SelectedItem is not ArchiveDocumentRow doc) return;
  busy = true;
  try
  {
   var detail = await Task.Run(async () =>
   {
    await using var scope = runtime.Services.CreateAsyncScope();
    return await scope.ServiceProvider.GetRequiredService<IArchiveService>().DetailAsync(doc.Id);
   });
   WorkspaceNavigator.Open(new ArchiveDetailWindow(runtime, $"{doc.ModelLabel} Nº {doc.Number} (Série {doc.Series}) · {doc.EnvironmentLabel} · {doc.StatusLabel} · Chave: {doc.Key} · Emitente: {doc.IssuerName} → Destinatário: {doc.RecipientName}", detail.Files, detail.Events));
  }
  catch { }
  finally { busy = false; }
 }
}