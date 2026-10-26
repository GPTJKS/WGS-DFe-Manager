using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace WgsDfe.Desktop;

public sealed class DanfeViewerControl : UserControl
{
 private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
 private static readonly Brush PaperBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
 private static readonly Brush SheetBgBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF5, 0xF4, 0xF0)));
 private static readonly Brush BoxBorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xC8, 0xC4, 0xB8)));
 private static readonly Brush StrongBorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x1B)));
 private static readonly Brush HeaderFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF8, 0xF7, 0xF4)));
 private static readonly Brush HighlightFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xEE)));
 private static readonly Brush SageFillBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5)));
 private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x64, 0x60, 0x59)));
 private static readonly Brush ValueBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)));
 private static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)));
 private static readonly Brush EmeraldBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x04, 0x78, 0x57)));

 private readonly ScrollViewer scroller;
 private readonly StackPanel rootHost;

 private static Thickness Pad(double h, double v) => new(h, v, h, v);

 public DanfeViewerControl()
 {
  rootHost = new StackPanel { Margin = new Thickness(10) };
  scroller = new ScrollViewer
  {
   VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
   HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
   Background = SheetBgBrush,
   Content = rootHost
  };
  Content = scroller;
  ClearView("Selecione uma versão ou carregue um documento XML para visualizar o espelho gráfico da Nota Fiscal (DANFE / DACTE).");
 }

 private static T Freeze<T>(T freezable) where T : Freezable { freezable.Freeze(); return freezable; }

 public void ClearView(string placeholder)
 {
  rootHost.Children.Clear();
  var card = new Border
  {
   Background = PaperBrush,
   BorderBrush = BoxBorderBrush,
   BorderThickness = new Thickness(1),
   CornerRadius = new CornerRadius(12),
   Padding = new Thickness(24),
   Margin = new Thickness(4)
  };
  var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
  stack.Children.Add(new TextBlock
  {
   Text = "\uE8A5",
   FontFamily = new FontFamily("Segoe MDL2 Assets"),
   FontSize = 28,
   Foreground = AccentBrush,
   HorizontalAlignment = HorizontalAlignment.Center,
   Margin = new Thickness(0, 0, 0, 8)
  });
  stack.Children.Add(new TextBlock
  {
   Text = placeholder,
   FontSize = 13,
   Foreground = LabelBrush,
   TextWrapping = TextWrapping.Wrap,
   TextAlignment = TextAlignment.Center
  });
  card.Child = stack;
  rootHost.Children.Add(card);
 }

 public void LoadXml(string? xmlText)
 {
  rootHost.Children.Clear();
  if (string.IsNullOrWhiteSpace(xmlText))
  {
   ClearView("Nenhum conteúdo XML carregado para exibição gráfica.");
   return;
  }

  try
  {
   var doc = XDocument.Parse(xmlText);
   var infNFe = Desc(doc, "infNFe");
   if (infNFe is not null)
   {
    RenderNfe(doc, infNFe);
    return;
   }

   var infCte = Desc(doc, "infCte");
   if (infCte is not null)
   {
    RenderCte(doc, infCte);
    return;
   }

   var infNFCom = Desc(doc, "infNFCom");
   if (infNFCom is not null)
   {
    RenderNfcom(doc, infNFCom);
    return;
   }

   var infEvento = Desc(doc, "infEvento");
   if (infEvento is not null)
   {
    RenderEvento(doc);
    return;
   }

   RenderSummaryOrGeneric(doc);
  }
  catch
  {
   ClearView("Não foi possível montar o espelho gráfico para este XML. Consulte a aba 'XML Original' para inspecionar o conteúdo bruto.");
  }
 }

 private void RenderNfe(XDocument doc, XElement infNFe)
 {
  var sheet = CreateSheetContainer();
  var ide = Child(infNFe, "ide");
  var emit = Child(infNFe, "emit");
  var dest = Child(infNFe, "dest");
  var total = Child(Child(infNFe, "total"), "ICMSTot");
  var issqnTot = Child(Child(infNFe, "total"), "ISSQNtot");
  var transp = Child(infNFe, "transp");
  var cobr = Child(infNFe, "cobr");
  var pag = Child(infNFe, "pag");
  var infAdic = Child(infNFe, "infAdic");
  var prot = Desc(doc, "infProt");

  string rawKey = Attr(infNFe, "Id").Replace("NFe", "", StringComparison.OrdinalIgnoreCase).Trim();
  if (string.IsNullOrWhiteSpace(rawKey)) rawKey = Val(prot, "chNFe");
  string mod = Val(ide, "mod", "55");
  string modLabel = mod == "65" ? "DANFE NFC-e · MODELO 65" : "DANFE · DOCUMENTO AUXILIAR DA NOTA FISCAL ELETRÔNICA";
  string nNF = FormatNumber(Val(ide, "nNF"));
  string serie = Val(ide, "serie", "1");
  string tpNF = Val(ide, "tpNF", "1");
  string tpAmb = Val(ide, "tpAmb", "1");
  string cStat = Val(prot, "cStat");
  string xMotivo = Val(prot, "xMotivo");
  string nProt = Val(prot, "nProt");
  string dhRecbto = FormatDateTime(Val(prot, "dhRecbto"));

  // 1. CANHOTO / TOP STATUS STRIP
  var topBar = new Border
  {
   Background = tpAmb == "2" ? HighlightFillBrush : SageFillBrush,
   BorderBrush = BoxBorderBrush,
   BorderThickness = new Thickness(1),
   CornerRadius = new CornerRadius(8),
   Padding = Pad(12, 6),
   Margin = new Thickness(0, 0, 0, 8)
  };
  var topDock = new DockPanel();
  if (!string.IsNullOrWhiteSpace(rawKey))
  {
   var copyBtn = new Button
   {
    Content = "Copiar Chave (44 dígitos)",
    Padding = Pad(10, 3),
    FontSize = 11,
    Margin = new Thickness(8, 0, 0, 0)
   };
   copyBtn.Click += (_, _) => FiscalClipboard.CopyKey(rawKey,message=>copyBtn.Content=message);
   DockPanel.SetDock(copyBtn, Dock.Right);
   topDock.Children.Add(copyBtn);
  }
  topDock.Children.Add(new TextBlock
  {
   Text = $"ESPELHO FISCAL COMPLETO ({(mod == "65" ? "NFC-e 65" : "NF-e 55")}) · {(tpAmb == "2" ? "AMBIENTE DE HOMOLOGAÇÃO" : "AMBIENTE DE PRODUÇÃO")} · {(string.IsNullOrWhiteSpace(cStat) ? "XML Local" : $"SEFAZ cStat {cStat} ({xMotivo})")}",
   FontWeight = FontWeights.Bold,
   FontSize = 11.5,
   Foreground = tpAmb == "2" ? AccentBrush : EmeraldBrush,
   VerticalAlignment = VerticalAlignment.Center,
   TextWrapping = TextWrapping.Wrap
  });
  topBar.Child = topDock;
  sheet.Children.Add(topBar);

  // 2. QUADRO CABEÇALHO DANFE (Emitente | DANFE | Código de Barras & Chave)
  var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 6) };
  headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
  headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.95, GridUnitType.Star) });
  headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });

  var enderEmit = Child(emit, "enderEmit");
  var emitBox = new Border
  {
   BorderBrush = StrongBorderBrush,
   BorderThickness = new Thickness(1, 1, 0, 1),
   CornerRadius = new CornerRadius(6, 0, 0, 6),
   Padding = Pad(10, 8),
   Background = PaperBrush
  };
  var emitStack = new StackPanel();
  emitStack.Children.Add(new TextBlock { Text = "IDENTIFICAÇÃO DO EMITENTE", FontSize = 9, FontWeight = FontWeights.Bold, Foreground = LabelBrush });
  emitStack.Children.Add(new TextBlock { Text = Val(emit, "xNome", "Emitente não informado"), FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = ValueBrush, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 2) });
  string xFant = Val(emit, "xFant");
  if (!string.IsNullOrWhiteSpace(xFant))
   emitStack.Children.Add(new TextBlock { Text = $"Nome Fantasia: {xFant}", FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = LabelBrush });
  emitStack.Children.Add(new TextBlock
  {
   Text = FormatAddress(enderEmit),
   FontSize = 10.5,
   Foreground = ValueBrush,
   TextWrapping = TextWrapping.Wrap,
   Margin = new Thickness(0, 3, 0, 0)
  });
  emitStack.Children.Add(new TextBlock
  {
   Text = $"Regime Tributário (CRT): {FormatCrt(Val(emit, "CRT"))}",
   FontSize = 10,
   FontWeight = FontWeights.SemiBold,
   Foreground = AccentBrush,
   Margin = new Thickness(0, 3, 0, 0)
  });
  emitBox.Child = emitStack;
  Grid.SetColumn(emitBox, 0);
  headerGrid.Children.Add(emitBox);

  var danfeCenter = new Border
  {
   BorderBrush = StrongBorderBrush,
   BorderThickness = new Thickness(1),
   Padding = Pad(10, 8),
   Background = HeaderFillBrush
  };
  var centerStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
  centerStack.Children.Add(new TextBlock { Text = mod == "65" ? "DANFE NFC-e" : "DANFE", FontSize = 16, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = ValueBrush });
  centerStack.Children.Add(new TextBlock { Text = modLabel, FontSize = 8.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Foreground = LabelBrush, Margin = new Thickness(0, 1, 0, 4) });
  var ioBadge = new Border { BorderBrush = StrongBorderBrush, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = Pad(8, 2), HorizontalAlignment = HorizontalAlignment.Center, Background = PaperBrush, Margin = new Thickness(0, 0, 0, 4) };
  ioBadge.Child = new TextBlock { Text = tpNF == "0" ? "0 - ENTRADA" : "1 - SAÍDA", FontSize = 10.5, FontWeight = FontWeights.Bold, Foreground = ValueBrush };
  centerStack.Children.Add(ioBadge);
  centerStack.Children.Add(new TextBlock { Text = $"Nº {nNF}", FontSize = 13.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = ValueBrush });
  centerStack.Children.Add(new TextBlock { Text = $"SÉRIE {serie} · MODELO {mod}", FontSize = 10.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = LabelBrush });
  danfeCenter.Child = centerStack;
  Grid.SetColumn(danfeCenter, 1);
  headerGrid.Children.Add(danfeCenter);

  var keyRight = new Border
  {
   BorderBrush = StrongBorderBrush,
   BorderThickness = new Thickness(0, 1, 1, 1),
   CornerRadius = new CornerRadius(0, 6, 6, 0),
   Padding = Pad(10, 8),
   Background = PaperBrush
  };
  var keyStack = new StackPanel();
  keyStack.Children.Add(new TextBlock { Text = "CONTROLE DO FISCO · REPRESENTAÇÃO DO CÓDIGO DE BARRAS", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = LabelBrush });
  keyStack.Children.Add(BuildBarcodeCanvas(rawKey));
  keyStack.Children.Add(new TextBlock { Text = "CHAVE DE ACESSO (44 DÍGITOS)", FontSize = 8.5, FontWeight = FontWeights.Bold, Foreground = LabelBrush, Margin = new Thickness(0, 4, 0, 1) });
  keyStack.Children.Add(new TextBlock
  {
   Text = FormatAccessKey(rawKey),
   FontFamily = Typography.Mono,
   FontSize = 11.5,
   FontWeight = FontWeights.Bold,
   Foreground = ValueBrush,
   TextWrapping = TextWrapping.Wrap
  });
  keyStack.Children.Add(new TextBlock
  {
   Text = "Consulta de autenticidade no portal nacional da NF-e www.nfe.fazenda.gov.br/portal ou no site da SEFAZ Autorizadora.",
   FontSize = 9,
   Foreground = LabelBrush,
   TextWrapping = TextWrapping.Wrap,
   Margin = new Thickness(0, 4, 0, 0)
  });
  keyRight.Child = keyStack;
  Grid.SetColumn(keyRight, 2);
  headerGrid.Children.Add(keyRight);
  sheet.Children.Add(headerGrid);

  // 3. NATUREZA DA OPERAÇÃO & PROTOCOLO & INSCRIÇÕES DO EMITENTE
  sheet.Children.Add(BuildRow(
   ("NATUREZA DA OPERAÇÃO", Val(ide, "natOp", "Não informada"), 2.0, false),
   ("PROTOCOLO DE AUTORIZAÇÃO DE USO", string.IsNullOrWhiteSpace(nProt) ? "Sem protocolo anexado" : $"{nProt} — {dhRecbto}", 2.0, false)));

  sheet.Children.Add(BuildRow(
   ("INSCRIÇÃO ESTADUAL DO EMITENTE", Val(emit, "IE", "ISENTO"), 1.2, false),
   ("INSCRIÇÃO ESTADUAL DO SUBST. TRIBUTÁRIO", Val(emit, "IEST", "—"), 1.2, false),
   ("INSCRIÇÃO MUNICIPAL", Val(emit, "IM", "—"), 0.9, false),
   ("CNPJ / CPF DO EMITENTE", FormatTaxId(FirstNonEmpty(Val(emit, "CNPJ"), Val(emit, "CPF"))), 1.4, true)));

  // 4. DESTINATÁRIO / REMETENTE
  sheet.Children.Add(SectionHeader("DESTINATÁRIO / REMETENTE"));
  var enderDest = Child(dest, "enderDest");
  sheet.Children.Add(BuildRow(
   ("NOME / RAZÃO SOCIAL", Val(dest, "xNome", "CONSUMIDOR / DESTINATÁRIO NÃO IDENTIFICADO"), 2.4, false),
   ("CNPJ / CPF", FormatTaxId(FirstNonEmpty(Val(dest, "CNPJ"), Val(dest, "CPF"), Val(dest, "idEstrangeiro"))), 1.2, true),
   ("DATA DA EMISSÃO", FormatDateTime(FirstNonEmpty(Val(ide, "dhEmi"), Val(ide, "dEmi"))), 1.1, false)));

  sheet.Children.Add(BuildRow(
   ("ENDEREÇO", CombineAddressLine(enderDest), 2.1, false),
   ("BAIRRO / DISTRITO", Val(enderDest, "xBairro", "—"), 1.1, false),
   ("CEP", FormatCep(Val(enderDest, "CEP")), 0.7, true),
   ("DATA DA SAÍDA / ENTRADA", FormatDateTime(FirstNonEmpty(Val(ide, "dhSaiEnt"), Val(ide, "dSaiEnt"))), 1.1, false)));

  sheet.Children.Add(BuildRow(
   ("MUNICÍPIO", Val(enderDest, "xMun", "—"), 1.7, false),
   ("UF", Val(enderDest, "UF", "—"), 0.4, false),
   ("FONE / FAX", Val(enderDest, "fone", "—"), 0.9, false),
   ("INSCRIÇÃO ESTADUAL / INDICADOR IE", $"{Val(dest, "IE", "—")} ({FormatIndIe(Val(dest, "indIEDest"))})", 1.3, false),
   ("DESTINO DA OPERAÇÃO", FormatIdDest(Val(ide, "idDest")), 0.9, false)));

  // 5. FATURA / DUPLICATAS / FORMAS DE PAGAMENTO
  sheet.Children.Add(SectionHeader("FATURA / DUPLICATAS / FORMAS DE PAGAMENTO"));
  var fat = Child(cobr, "fat");
  var dups = cobr?.Elements().Where(e => e.Name.LocalName == "dup").ToList() ?? [];
  var detPags = pag?.Elements().Where(e => e.Name.LocalName == "detPag").ToList() ?? [];
  string faturaSummary = fat is not null
   ? $"Fatura Nº {Val(fat, "nFat", "—")} · Valor Original: R$ {Money(Val(fat, "vOrig"))} · Desconto: R$ {Money(Val(fat, "vDesc"))} · Valor Líquido: R$ {Money(Val(fat, "vLiq"))}"
   : "Sem fatura comercial consolidada";
  string dupSummary = dups.Count > 0
   ? string.Join("   |   ", dups.Select(d => $"Parc. {Val(d, "nDup")}: Venc. {FormatDateTime(Val(d, "dVenc"))} — R$ {Money(Val(d, "vDup"))}"))
   : "Sem duplicatas informadas";
  string pagSummary = detPags.Count > 0
   ? string.Join("   |   ", detPags.Select(p => $"{FormatPaymentType(Val(p, "tPag"))}: R$ {Money(Val(p, "vPag"))}"))
   : "Sem detalhamento de pagamento";

  sheet.Children.Add(BuildRow(
   ("DADOS DA FATURA", faturaSummary, 1.5, false),
   ("PARCELAS / DUPLICATAS", dupSummary, 2.0, false),
   ("MEIOS DE PAGAMENTO", pagSummary, 1.5, false)));

  // 6. CÁLCULO DO IMPOSTO / TRIBUTAÇÃO COMPLETA
  sheet.Children.Add(SectionHeader("CÁLCULO DO IMPOSTO & QUADRO COMPLETO DE TRIBUTAÇÃO"));
  sheet.Children.Add(BuildRow(
   ("BASE DE CÁLC. DO ICMS", $"R$ {Money(Val(total, "vBC"))}", 1.0, true),
   ("VALOR DO ICMS", $"R$ {Money(Val(total, "vICMS"))}", 1.0, true),
   ("BASE DE CÁLC. ICMS S.T.", $"R$ {Money(Val(total, "vBCST"))}", 1.0, true),
   ("VALOR DO ICMS SUBST. (ST)", $"R$ {Money(Val(total, "vST"))}", 1.0, true),
   ("ICMS DESONERADO", $"R$ {Money(Val(total, "vICMSDeson"))}", 1.0, true),
   ("VALOR TOTAL DOS PRODUTOS", $"R$ {Money(Val(total, "vProd"))}", 1.2, true)));

  sheet.Children.Add(BuildRow5(
   ("VALOR DO FRETE", $"R$ {Money(Val(total, "vFrete"))}", 1.0, true, false),
   ("VALOR DO SEGURO", $"R$ {Money(Val(total, "vSeg"))}", 1.0, true, false),
   ("DESCONTO TOTAL", $"R$ {Money(Val(total, "vDesc"))}", 1.0, true, false),
   ("OUTRAS DESP. ACESSÓRIAS", $"R$ {Money(Val(total, "vOutro"))}", 1.0, true, false),
   ("VALOR TOTAL DO IPI", $"R$ {Money(Val(total, "vIPI"))}", 1.0, true, false),
   ("VALOR TOTAL DA NOTA FISCAL", $"R$ {Money(Val(total, "vNF"))}", 1.2, true, true)));

  // PIS / COFINS com base e alíquota (consolidadas a partir dos itens)
  var dets = infNFe.Elements().Where(e => e.Name.LocalName == "det").ToList();
  var impostos = dets.Select(d => Child(d, "imposto")).ToList();
  var pisGroups = impostos.Select(i => Child(i, "PIS")?.Elements().FirstOrDefault()).ToList();
  var cofinsGroups = impostos.Select(i => Child(i, "COFINS")?.Elements().FirstOrDefault()).ToList();
  decimal bcPis = pisGroups.Sum(g => Dec(Val(g, "vBC")));
  decimal bcCofins = cofinsGroups.Sum(g => Dec(Val(g, "vBC")));
  decimal vPis = Dec(FirstNonEmpty(Val(total, "vPIS"), pisGroups.Sum(g => Dec(Val(g, "vPIS"))).ToString(CultureInfo.InvariantCulture)));
  decimal vCofins = Dec(FirstNonEmpty(Val(total, "vCOFINS"), cofinsGroups.Sum(g => Dec(Val(g, "vCOFINS"))).ToString(CultureInfo.InvariantCulture)));
  sheet.Children.Add(BuildRow5(
   ("BASE DE CÁLC. DO PIS", $"R$ {bcPis.ToString("N2", PtBr)}", 1.0, true, false),
   ("ALÍQUOTA DO PIS", RateSummary(pisGroups.Select(g => Val(g, "pPIS")), vPis, bcPis), 1.0, true, false),
   ("VALOR DO PIS", $"R$ {vPis.ToString("N2", PtBr)}", 1.0, true, true),
   ("BASE DE CÁLC. DA COFINS", $"R$ {bcCofins.ToString("N2", PtBr)}", 1.0, true, false),
   ("ALÍQUOTA DA COFINS", RateSummary(cofinsGroups.Select(g => Val(g, "pCOFINS")), vCofins, bcCofins), 1.0, true, false),
   ("VALOR DA COFINS", $"R$ {vCofins.ToString("N2", PtBr)}", 1.0, true, true)));

  sheet.Children.Add(BuildRow(
   ("IMPOSTO DE IMPORTAÇÃO (II)", $"R$ {Money(Val(total, "vII"))}", 1.0, true),
   ("FCP + FCP ST", $"R$ {Money(AddDec(Val(total, "vFCP"), Val(total, "vFCPST")))}", 1.0, true),
   ("DIFAL (ICMS UF DEST / REMET)", $"R$ {Money(Val(total, "vICMSUFDest"))} / R$ {Money(Val(total, "vICMSUFRemet"))}", 1.3, true),
   ("TRIBUTOS APROXIMADOS (LEI 12.741)", $"R$ {Money(Val(total, "vTotTrib"))}", 1.2, true)));

  if (issqnTot is not null)
  {
   sheet.Children.Add(BuildRow(
    ("TOTAL SERVIÇOS (ISSQN)", $"R$ {Money(Val(issqnTot, "vServ"))}", 1.2, true),
    ("BASE CÁLC. ISSQN", $"R$ {Money(Val(issqnTot, "vBC"))}", 1.2, true),
    ("VALOR TOTAL ISSQN", $"R$ {Money(Val(issqnTot, "vISS"))}", 1.2, true)));
  }

  // 6.1 REFORMA TRIBUTÁRIA — IBS / CBS / IS (LC 214/2025 · NT 2025.002)
  var totalNode = Child(infNFe, "total");
  foreach (var element in BuildReformSection(Child(totalNode, "IBSCBSTot"), Child(totalNode, "ISTot"), Val(totalNode, "vNFTot"), impostos))
   sheet.Children.Add(element);

  // 7. TRANSPORTADOR / VOLUMES TRANSPORTADOS
  sheet.Children.Add(SectionHeader("TRANSPORTADOR / VOLUMES TRANSPORTADOS"));
  var transporta = Child(transp, "transporta");
  var veic = Child(transp, "veicTransp");
  var vol = Child(transp, "vol");
  sheet.Children.Add(BuildRow(
   ("NOME / RAZÃO SOCIAL DA TRANSPORTADORA", Val(transporta, "xNome", "—"), 1.9, false),
   ("MODALIDADE DO FRETE", FormatModFrete(Val(transp, "modFrete")), 1.3, false),
   ("CÓDIGO ANTT", Val(veic, "RNTC", "—"), 0.7, false),
   ("PLACA DO VEÍCULO", Val(veic, "placa", "—"), 0.6, true),
   ("UF", Val(veic, "UF", "—"), 0.3, false),
   ("CNPJ / CPF TRANSPORTADOR", FormatTaxId(FirstNonEmpty(Val(transporta, "CNPJ"), Val(transporta, "CPF"))), 1.1, true)));

  sheet.Children.Add(BuildRow(
   ("ENDEREÇO / MUNICÍPIO / UF", $"{Val(transporta, "xEnder", "—")} · {Val(transporta, "xMun", "—")}/{Val(transporta, "UF", "—")}", 2.0, false),
   ("INSCRIÇÃO ESTADUAL", Val(transporta, "IE", "—"), 0.8, false),
   ("QUANTIDADE VOL.", Val(vol, "qVol", "—"), 0.7, true),
   ("ESPÉCIE / MARCA", $"{Val(vol, "esp", "—")} / {Val(vol, "marca", "—")}", 0.9, false),
   ("PESO BRUTO (KG)", Val(vol, "pesoB", "—"), 0.7, true),
   ("PESO LÍQUIDO (KG)", Val(vol, "pesoL", "—"), 0.7, true)));

  // 8. DADOS DOS PRODUTOS / SERVIÇOS E TRIBUTOS POR ITEM
  sheet.Children.Add(SectionHeader($"DADOS DOS PRODUTOS / SERVIÇOS E TRIBUTAÇÃO POR ITEM ({dets.Count} ITEM/ITENS)"));
  sheet.Children.Add(BuildItemsTable(dets));

  // 9. DADOS ADICIONAIS / INFORMAÇÕES COMPLEMENTARES
  sheet.Children.Add(SectionHeader("DADOS ADICIONAIS & INFORMAÇÕES COMPLEMENTARES"));
  sheet.Children.Add(BuildRow(
   ("INFORMAÇÕES COMPLEMENTARES DE INTERESSE DO CONTRIBUINTE", Val(infAdic, "infCpl", "Sem informações complementares registradas no XML."), 2.2, false),
   ("INFORMAÇÕES ADICIONAIS DE INTERESSE DO FISCO", Val(infAdic, "infAdFisco", "—"), 1.0, false)));

  rootHost.Children.Add(sheet);
 }

 private UIElement BuildItemsTable(IReadOnlyList<XElement> dets)
 {
  var rows = new List<DanfeItemRow>(dets.Count);
  foreach (var det in dets)
  {
   var prod = Child(det, "prod");
   var imposto = Child(det, "imposto");
   var icmsGroup = Child(imposto, "ICMS")?.Elements().FirstOrDefault();
   var ipiGroup = Child(imposto, "IPI")?.Elements().FirstOrDefault(e => e.Name.LocalName is "IPITrib" or "IPINT");
   var pisGroup = Child(imposto, "PIS")?.Elements().FirstOrDefault();
   var cofinsGroup = Child(imposto, "COFINS")?.Elements().FirstOrDefault();
   var ibscbs = Child(imposto, "IBSCBS");
   var gIbsCbs = Child(ibscbs, "gIBSCBS");
   var gIbsUf = Child(gIbsCbs, "gIBSUF");
   var gIbsMun = Child(gIbsCbs, "gIBSMun");
   var gCbs = Child(gIbsCbs, "gCBS");
   var isGroup = Child(imposto, "IS");

   string orig = Val(icmsGroup, "orig");
   string cstOrCsosn = FirstNonEmpty(Val(icmsGroup, "CST"), Val(icmsGroup, "CSOSN"), "—");
   string fullCst = string.IsNullOrWhiteSpace(orig) ? cstOrCsosn : $"{orig}/{cstOrCsosn}";
   string xProd = Val(prod, "xProd");
   string infAdProd = Val(det, "infAdProd");
   if (!string.IsNullOrWhiteSpace(infAdProd)) xProd += $"\n({infAdProd})";

   string vIbs = FirstNonEmpty(Val(gIbsCbs, "vIBS"), Val(Desc(ibscbs, "gIBSCBSMono"), "vTotIBSMonoItem"),
    gIbsCbs is null ? "" : AddDec(Val(gIbsUf, "vIBSUF"), Val(gIbsMun, "vIBSMun")));
   string vCbs = FirstNonEmpty(Val(gCbs, "vCBS"), Val(Desc(ibscbs, "gIBSCBSMono"), "vTotCBSMonoItem"));

   rows.Add(new DanfeItemRow(
    Attr(det, "nItem", (rows.Count + 1).ToString(CultureInfo.InvariantCulture)),
    Val(prod, "cProd", "—"),
    xProd,
    Val(prod, "NCM", "—"),
    fullCst,
    Val(prod, "CFOP", "—"),
    Val(prod, "uCom", "—"),
    DecFmt(Val(prod, "qCom"), "N2"),
    Money(Val(prod, "vUnCom")),
    Money(Val(prod, "vProd")),
    Money(Val(icmsGroup, "vBC")),
    Money(Val(icmsGroup, "vICMS")),
    DecFmt(Val(icmsGroup, "pICMS"), "N2") + "%",
    Money(Val(ipiGroup, "vIPI")),
    $"{Val(pisGroup, "CST", "—")} / {Val(cofinsGroup, "CST", "—")}",
    Money(Val(pisGroup, "vBC")),
    ItemRate(pisGroup, "pPIS"),
    Money(Val(pisGroup, "vPIS")),
    ItemRate(cofinsGroup, "pCOFINS"),
    Money(Val(cofinsGroup, "vCOFINS")),
    ibscbs is null ? "—" : $"{Val(ibscbs, "CST", "—")} · {Val(ibscbs, "cClassTrib", "—")}",
    gIbsCbs is null ? "—" : Money(Val(gIbsCbs, "vBC")),
    RateWithEffective(gIbsUf, "pIBSUF"),
    RateWithEffective(gIbsMun, "pIBSMun"),
    string.IsNullOrWhiteSpace(vIbs) ? "—" : Money(vIbs),
    RateWithEffective(gCbs, "pCBS"),
    string.IsNullOrWhiteSpace(vCbs) ? "—" : Money(vCbs),
    isGroup is null ? "—" : $"{Pct(Val(isGroup, "pIS"))} · R$ {Money(Val(isGroup, "vIS"))}"));
  }
  bool hasSelective = rows.Any(r => r.ImpostoSeletivo != "—");

  var grid = new DataGrid
  {
   ItemsSource = rows,
   IsReadOnly = true,
   AutoGenerateColumns = false,
   CanUserAddRows = false,
   CanUserDeleteRows = false,
   HeadersVisibility = DataGridHeadersVisibility.Column,
   RowHeight = double.NaN,
   MinRowHeight = 32,
   FontSize = 11,
   Margin = new Thickness(0, 0, 0, 6),
   MaxHeight = 320,
   VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
   HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
  };

  void Col(string header, string prop, double width, bool mono = false, bool bold = false)
  {
   var style = new Style(typeof(TextBlock));
   style.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
   style.Setters.Add(new Setter(TextBlock.PaddingProperty, Pad(3, 2)));
   style.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
   style.Setters.Add(new Setter(TextBlock.ForegroundProperty, ValueBrush));
   if (mono) style.Setters.Add(new Setter(TextBlock.FontFamilyProperty, Typography.Mono));
   if (bold) style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.Bold));
   grid.Columns.Add(new DataGridTextColumn
   {
    Header = header,
    Binding = new System.Windows.Data.Binding(prop),
    Width = new DataGridLength(width),
    MinWidth = Math.Max(42, width - 10),
    ElementStyle = style
   });
  }

  Col("#", nameof(DanfeItemRow.Item), 36, true);
  Col("CÓDIGO", nameof(DanfeItemRow.Code), 80, true);
  Col("DESCRIÇÃO DO PRODUTO / SERVIÇO", nameof(DanfeItemRow.Description), 220, false, true);
  Col("NCM/SH", nameof(DanfeItemRow.Ncm), 74, true);
  Col("ORIG/CST", nameof(DanfeItemRow.Cst), 68, true);
  Col("CFOP", nameof(DanfeItemRow.Cfop), 52, true, true);
  Col("UN", nameof(DanfeItemRow.Unit), 46);
  Col("QTD", nameof(DanfeItemRow.Qty), 66, true);
  Col("VLR UNIT", nameof(DanfeItemRow.UnitPrice), 82, true);
  Col("VLR TOTAL", nameof(DanfeItemRow.TotalPrice), 88, true, true);
  Col("BC ICMS", nameof(DanfeItemRow.BcIcms), 78, true);
  Col("VLR ICMS", nameof(DanfeItemRow.VlrIcms), 74, true);
  Col("% ICMS", nameof(DanfeItemRow.AliqIcms), 58, true);
  Col("VLR IPI", nameof(DanfeItemRow.VlrIpi), 68, true);
  Col("CST PIS/COF", nameof(DanfeItemRow.CstPisCofins), 74, true);
  Col("BC PIS/COF", nameof(DanfeItemRow.BcPisCofins), 80, true);
  Col("% PIS", nameof(DanfeItemRow.AliqPis), 62, true);
  Col("VLR PIS", nameof(DanfeItemRow.VlrPis), 70, true);
  Col("% COFINS", nameof(DanfeItemRow.AliqCofins), 66, true);
  Col("VLR COFINS", nameof(DanfeItemRow.VlrCofins), 76, true);
  Col("CST · CLASS. IBS/CBS", nameof(DanfeItemRow.CstIbsCbs), 112, true);
  Col("BC IBS/CBS", nameof(DanfeItemRow.BcIbsCbs), 80, true);
  Col("% IBS UF", nameof(DanfeItemRow.AliqIbsUf), 66, true);
  Col("% IBS MUN", nameof(DanfeItemRow.AliqIbsMun), 70, true);
  Col("VLR IBS", nameof(DanfeItemRow.VlrIbs), 70, true, true);
  Col("% CBS", nameof(DanfeItemRow.AliqCbs), 62, true);
  Col("VLR CBS", nameof(DanfeItemRow.VlrCbs), 70, true, true);
  if (hasSelective) Col("IMP. SELETIVO", nameof(DanfeItemRow.ImpostoSeletivo), 110, true);
  return grid;
 }

 private static decimal Dec(string raw)
  => decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0m;

 private static string Pct(string raw)
  => string.IsNullOrWhiteSpace(raw) ? "—" : Dec(raw).ToString("0.00##", PtBr) + "%";

 /// <summary>Alíquota do item: percentual (pPIS/pCOFINS) ou valor por unidade (vAliqProd) quando tributado por quantidade.</summary>
 private static string ItemRate(XElement? group, string percentName)
 {
  if (group is null) return "—";
  string p = Val(group, percentName);
  if (!string.IsNullOrWhiteSpace(p)) return Pct(p);
  string perUnit = Val(group, "vAliqProd");
  return string.IsNullOrWhiteSpace(perUnit) ? "—" : $"R$ {Dec(perUnit).ToString("0.0000", PtBr)}/un";
 }

 /// <summary>Alíquota IBS/CBS com indicação da alíquota efetiva quando houver redução (gRed/pAliqEfet).</summary>
 private static string RateWithEffective(XElement? group, string percentName)
 {
  if (group is null) return "—";
  string p = Val(group, percentName);
  string effective = Val(Child(group, "gRed"), "pAliqEfet");
  if (!string.IsNullOrWhiteSpace(effective) && Dec(effective) != Dec(p)) return $"{Pct(p)} (ef. {Pct(effective)})";
  return Pct(p);
 }

 /// <summary>Resumo de alíquota: única quando todos os itens usam a mesma; caso contrário, alíquota efetiva (valor / base).</summary>
 private static string RateSummary(IEnumerable<string> rates, decimal value, decimal baseValue)
 {
  var distinct = rates.Where(r => !string.IsNullOrWhiteSpace(r)).Select(Dec).Distinct().ToList();
  if (distinct.Count == 1) return distinct[0].ToString("0.00##", PtBr) + "%";
  if (baseValue <= 0) return distinct.Count == 0 ? "—" : "Variável por item";
  string effective = (value / baseValue * 100m).ToString("0.00", PtBr) + "%";
  return distinct.Count == 0 ? $"{effective} (efetiva)" : $"Variável · {effective} efetiva";
 }

 /// <summary>
 /// Quadro da Reforma Tributária do Consumo (LC 214/2025 · NT 2025.002): IBS (UF + Município), CBS e Imposto Seletivo.
 /// Usa os totais do grupo IBSCBSTot quando presentes e, na ausência, consolida a partir dos itens.
 /// </summary>
 private static IEnumerable<UIElement> BuildReformSection(XElement? ibsCbsTot, XElement? isTot, string vNFTot, IReadOnlyList<XElement?> impostos)
 {
  yield return SectionHeader("REFORMA TRIBUTÁRIA DO CONSUMO · IBS / CBS / IMPOSTO SELETIVO (LC 214/2025)");
  var groups = impostos.Select(i => Child(i, "IBSCBS")).Where(g => g is not null).ToList();
  bool anyIs = impostos.Any(i => Child(i, "IS") is not null) || isTot is not null;
  bool hasReforma = ibsCbsTot is not null || groups.Count > 0 || anyIs;
  if (!hasReforma)
  {
   yield return BuildRow(("QUADRO DE TRANSIÇÃO (EC 132/2023 · LC 214/2025)", "Quadro estruturado e pronto para recepção de notas fiscais emitidas com IBS e CBS da Reforma Tributária.", 1.0, false));
  }

  var g = groups.Select(x => Child(x, "gIBSCBS")).ToList();
  decimal itemBc = g.Sum(x => Dec(Val(x, "vBC")));
  decimal itemIbsUf = g.Sum(x => Dec(Val(Child(x, "gIBSUF"), "vIBSUF")));
  decimal itemIbsMun = g.Sum(x => Dec(Val(Child(x, "gIBSMun"), "vIBSMun")));
  decimal itemIbs = g.Sum(x => Dec(FirstNonEmpty(Val(x, "vIBS"), AddDec(Val(Child(x, "gIBSUF"), "vIBSUF"), Val(Child(x, "gIBSMun"), "vIBSMun")))));
  decimal itemCbs = g.Sum(x => Dec(Val(Child(x, "gCBS"), "vCBS")));
  decimal itemIs = impostos.Sum(i => Dec(Val(Child(i, "IS"), "vIS")));

  var gIbsT = Child(ibsCbsTot, "gIBS");
  var gIbsUfT = Child(gIbsT, "gIBSUF");
  var gIbsMunT = Child(gIbsT, "gIBSMun");
  var gCbsT = Child(ibsCbsTot, "gCBS");
  decimal Pick(XElement? node, string name, decimal fallback) => node is not null && !string.IsNullOrWhiteSpace(Val(node, name)) ? Dec(Val(node, name)) : fallback;

  decimal bc = Pick(ibsCbsTot, "vBCIBSCBS", itemBc);
  decimal ibsUf = Pick(gIbsUfT, "vIBSUF", itemIbsUf);
  decimal ibsMun = Pick(gIbsMunT, "vIBSMun", itemIbsMun);
  decimal ibs = Pick(gIbsT, "vIBS", itemIbs);
  decimal cbs = Pick(gCbsT, "vCBS", itemCbs);
  decimal sel = Pick(isTot, "vIS", itemIs);
  decimal credPresIbs = Dec(Val(gIbsT, "vCredPres"));
  decimal credPresCbs = Dec(Val(gCbsT, "vCredPres"));
  decimal difIbs = Dec(Val(gIbsUfT, "vDif")) + Dec(Val(gIbsMunT, "vDif"));
  decimal difCbs = Dec(Val(gCbsT, "vDif"));

  string R(decimal v) => $"R$ {v.ToString("N2", PtBr)}";
  yield return BuildRow5(
   ("BASE DE CÁLC. IBS / CBS", R(bc), 1.0, true, false),
   ("ALÍQUOTA IBS UF", RateSummary(g.Select(x => Val(Child(x, "gIBSUF"), "pIBSUF")), ibsUf, bc), 0.9, true, false),
   ("VALOR IBS UF", R(ibsUf), 0.9, true, false),
   ("ALÍQUOTA IBS MUNICIPAL", RateSummary(g.Select(x => Val(Child(x, "gIBSMun"), "pIBSMun")), ibsMun, bc), 0.9, true, false),
   ("VALOR IBS MUNICIPAL", R(ibsMun), 0.9, true, false),
   ("VALOR TOTAL DO IBS", R(ibs), 1.0, true, true));
  yield return BuildRow5(
   ("ALÍQUOTA CBS", RateSummary(g.Select(x => Val(Child(x, "gCBS"), "pCBS")), cbs, bc), 0.9, true, false),
   ("VALOR TOTAL DA CBS", R(cbs), 1.0, true, true),
   ("CRÉD. PRESUMIDO IBS / CBS", $"{R(credPresIbs)} / {R(credPresCbs)}", 1.2, true, false),
   ("DIFERIMENTO IBS / CBS", $"{R(difIbs)} / {R(difCbs)}", 1.2, true, false),
   ("IMPOSTO SELETIVO (IS)", R(sel), 0.9, true, false),
   ("TOTAL DA NOTA C/ IBS, CBS E IS", string.IsNullOrWhiteSpace(vNFTot) ? "—" : $"R$ {Money(vNFTot)}", 1.1, true, true));
 }

 private void RenderCte(XDocument doc, XElement infCte)
 {
  var sheet = CreateSheetContainer();
  var ide = Child(infCte, "ide");
  var emit = Child(infCte, "emit");
  var rem = Child(infCte, "rem");
  var dest = Child(infCte, "dest");
  var exped = Child(infCte, "exped");
  var receb = Child(infCte, "receb");
  var vPrest = Child(infCte, "vPrest");
  var imp = Child(infCte, "imp");
  var infCarga = Desc(infCte, "infCarga");
  var prot = Desc(doc, "infProt");

  string rawKey = Attr(infCte, "Id").Replace("CTe", "", StringComparison.OrdinalIgnoreCase).Trim();
  if (string.IsNullOrWhiteSpace(rawKey)) rawKey = Val(prot, "chCTe");

  sheet.Children.Add(SectionHeader($"DACTE · DOCUMENTO AUXILIAR DO CONHECIMENTO DE TRANSPORTE ELETRÔNICO (MOD. {Val(ide, "mod", "57")})"));
  sheet.Children.Add(BuildRow(
   ("EMITENTE TRANSPORTADOR", Val(emit, "xNome", "—"), 2.0, false),
   ("CNPJ EMITENTE", FormatTaxId(Val(emit, "CNPJ")), 1.1, true),
   ("Nº CT-e / SÉRIE", $"Nº {FormatNumber(Val(ide, "nCT"))} · Série {Val(ide, "serie", "1")}", 1.0, false),
   ("EMISSÃO", FormatDateTime(Val(ide, "dhEmi")), 1.0, false)));

  sheet.Children.Add(BuildRow(
   ("CHAVE DE ACESSO (44 DÍGITOS)", FormatAccessKey(rawKey), 2.5, true),
   ("PROTOCOLO SEFAZ", $"{Val(prot, "nProt", "—")} ({Val(prot, "cStat", "—")} · {Val(prot, "xMotivo", "XML Local")})", 2.0, false)));

  sheet.Children.Add(BuildRow(
   ("CFOP / NATUREZA DA PRESTAÇÃO", $"{Val(ide, "CFOP")} — {Val(ide, "natOp")}", 1.8, false),
   ("INÍCIO DA PRESTAÇÃO", $"{Val(ide, "xMunIni")} / {Val(ide, "UFIni")}", 1.2, false),
   ("TÉRMINO DA PRESTAÇÃO", $"{Val(ide, "xMunFim")} / {Val(ide, "UFFim")}", 1.2, false)));

  sheet.Children.Add(SectionHeader("ATORES DO TRANSPORTE (REMETENTE, DESTINATÁRIO, EXPEDIDOR E RECEBEDOR)"));
  sheet.Children.Add(BuildRow(
   ("REMETENTE", $"{Val(rem, "xNome", "—")} ({FormatTaxId(FirstNonEmpty(Val(rem, "CNPJ"), Val(rem, "CPF")))})", 1.5, false),
   ("DESTINATÁRIO", $"{Val(dest, "xNome", "—")} ({FormatTaxId(FirstNonEmpty(Val(dest, "CNPJ"), Val(dest, "CPF")))})", 1.5, false)));
  if (exped is not null || receb is not null)
  {
   sheet.Children.Add(BuildRow(
    ("EXPEDIDOR", $"{Val(exped, "xNome", "—")} ({FormatTaxId(FirstNonEmpty(Val(exped, "CNPJ"), Val(exped, "CPF")))})", 1.5, false),
    ("RECEBEDOR", $"{Val(receb, "xNome", "—")} ({FormatTaxId(FirstNonEmpty(Val(receb, "CNPJ"), Val(receb, "CPF")))})", 1.5, false)));
  }

  sheet.Children.Add(SectionHeader("VALORES DA PRESTAÇÃO, TRIBUTAÇÃO ICMS E CARGA"));
  var icmsNode = Child(imp, "ICMS")?.Elements().FirstOrDefault();
  sheet.Children.Add(BuildRow5(
   ("VALOR TOTAL DA PRESTAÇÃO", $"R$ {Money(Val(vPrest, "vTPrest"))}", 1.2, true, true),
   ("VALOR A RECEBER", $"R$ {Money(Val(vPrest, "vRec"))}", 1.1, true, false),
   ("CST ICMS", FirstNonEmpty(Val(icmsNode, "CST"), "—"), 0.7, true, false),
   ("BASE CÁLC. ICMS", $"R$ {Money(Val(icmsNode, "vBC"))}", 1.0, true, false),
   ("ALÍQ. ICMS", $"{DecFmt(Val(icmsNode, "pICMS"), "N2")}%", 0.7, true, false),
   ("VALOR DO ICMS", $"R$ {Money(Val(icmsNode, "vICMS"))}", 1.0, true, false),
   ("TRIBUTOS APROX.", $"R$ {Money(Val(imp, "vTotTrib"))}", 1.0, true, false)));

  foreach (var element in BuildReformSection(null, null, Val(imp, "vTotDFe"), new[] { imp }))
   sheet.Children.Add(element);

  sheet.Children.Add(BuildRow(
   ("VALOR TOTAL DA CARGA", $"R$ {Money(Val(infCarga, "vCarga"))}", 1.2, true),
   ("PRODUTO PREDOMINANTE", Val(infCarga, "proPred", "—"), 1.8, false),
   ("OBSERVAÇÕES GERAIS", Val(Desc(infCte, "compl"), "xObs", "—"), 2.0, false)));

  rootHost.Children.Add(sheet);
 }

 private void RenderNfcom(XDocument doc, XElement infNFCom)
 {
  var sheet = CreateSheetContainer();
  var ide = Child(infNFCom, "ide");
  var emit = Child(infNFCom, "emit");
  var dest = Child(infNFCom, "dest");
  var total = Child(infNFCom, "total");
  string rawKey = Attr(infNFCom, "Id").Replace("NFCom", "", StringComparison.OrdinalIgnoreCase).Trim();

  sheet.Children.Add(SectionHeader("DANFCOM · DOCUMENTO AUXILIAR DA NOTA FISCAL FATURA DE SERVIÇOS DE COMUNICAÇÃO (MOD. 62)"));
  sheet.Children.Add(BuildRow(
   ("EMITENTE", Val(emit, "xNome", "—"), 2.0, false),
   ("CNPJ EMITENTE", FormatTaxId(Val(emit, "CNPJ")), 1.1, true),
   ("Nº / SÉRIE", $"Nº {Val(ide, "nNF")} · Série {Val(ide, "serie")}", 1.0, false),
   ("EMISSÃO", FormatDateTime(Val(ide, "dhEmi")), 1.0, false)));
  sheet.Children.Add(BuildRow(
   ("DESTINATÁRIO / ASSINANTE", Val(dest, "xNome", "—"), 2.0, false),
   ("CNPJ / CPF", FormatTaxId(FirstNonEmpty(Val(dest, "CNPJ"), Val(dest, "CPF"))), 1.2, true),
   ("CHAVE DE ACESSO", FormatAccessKey(rawKey), 2.2, true)));
  sheet.Children.Add(BuildRow5(
   ("VALOR PRODUTOS/SERVIÇOS", $"R$ {Money(Val(total, "vProd"))}", 1.0, true, false),
   ("BC ICMS", $"R$ {Money(Val(Desc(total, "ICMSTot"), "vBC"))}", 1.0, true, false),
   ("VALOR ICMS", $"R$ {Money(Val(Desc(total, "ICMSTot"), "vICMS"))}", 1.0, true, false),
   ("VALOR PIS / COFINS", $"R$ {Money(Val(total, "vPIS"))} / R$ {Money(Val(total, "vCOFINS"))}", 1.2, true, false),
   ("VALOR TOTAL DA NFCom", $"R$ {Money(Val(total, "vNF"))}", 1.2, true, true)));

  var nfcomImpostos = infNFCom.Elements().Where(e => e.Name.LocalName == "det").Select(d => Child(d, "imposto")).ToList();
  foreach (var element in BuildReformSection(Child(total, "IBSCBSTot"), null, Val(total, "vNFTot"), nfcomImpostos))
   sheet.Children.Add(element);

  rootHost.Children.Add(sheet);
 }

 private void RenderEvento(XDocument doc)
 {
  var sheet = CreateSheetContainer();
  var infEvento = doc.Descendants().First(e => e.Name.LocalName == "infEvento");
  var detEvento = Desc(infEvento, "detEvento");
  var retInf = doc.Descendants().LastOrDefault(e => e.Name.LocalName == "infEvento" && !ReferenceEquals(e, infEvento));

  string key = FirstNonEmpty(Val(infEvento, "chNFe"), Val(infEvento, "chCTe"));
  string tpEvento = Val(infEvento, "tpEvento");
  string descEvento = FirstNonEmpty(Val(detEvento, "descEvento"), tpEvento);

  sheet.Children.Add(SectionHeader($"ESPELHO DE EVENTO FISCAL VINCULADO · {descEvento.ToUpperInvariant()}"));
  sheet.Children.Add(BuildRow(
   ("CHAVE DE ACESSO DO DOCUMENTO", FormatAccessKey(key), 2.4, true),
   ("TIPO DE EVENTO / SEQUÊNCIA", $"{tpEvento} — {descEvento} (Seq. {Val(infEvento, "nSeqEvento", "1")})", 1.8, false)));
  sheet.Children.Add(BuildRow(
   ("CNPJ / CPF DO AUTOR", FormatTaxId(FirstNonEmpty(Val(infEvento, "CNPJ"), Val(infEvento, "CPF"))), 1.2, true),
   ("DATA E HORA DO EVENTO", FormatDateTime(Val(infEvento, "dhEvento")), 1.2, false),
   ("PROTOCOLO E RETORNO SEFAZ", $"{Val(retInf, "nProt", "—")} · cStat {Val(retInf, "cStat", "—")} ({Val(retInf, "xMotivo", "Registrado")})", 2.0, false)));
  sheet.Children.Add(BuildRow(
   ("JUSTIFICATIVA / CORREÇÃO / DETALHES DO EVENTO", FirstNonEmpty(Val(detEvento, "xJust"), Val(detEvento, "xCorrecao"), Val(detEvento, "descEvento"), "Sem texto adicional no evento."), 1.0, false)));

  rootHost.Children.Add(sheet);
 }

 private void RenderSummaryOrGeneric(XDocument doc)
 {
  var sheet = CreateSheetContainer();
  var root = doc.Root;
  sheet.Children.Add(SectionHeader($"RESUMO / DOCUMENTO FISCAL ({root?.Name.LocalName ?? "XML"})"));
  sheet.Children.Add(BuildRow5(
   ("CHAVE DE ACESSO", FormatAccessKey(FirstNonEmpty(Val(root, "chNFe"), Val(root, "chCTe"))), 2.2, true, false),
   ("EMITENTE / AUTOR", FirstNonEmpty(Val(root, "xNome"), FormatTaxId(FirstNonEmpty(Val(root, "CNPJ"), Val(root, "CPF")))), 1.8, false, false),
   ("VALOR TOTAL", $"R$ {Money(Val(root, "vNF"))}", 1.0, true, true)));
  rootHost.Children.Add(sheet);
 }

 private static StackPanel CreateSheetContainer() => new()
 {
  Background = PaperBrush,
  Margin = new Thickness(2)
 };

 private static Border SectionHeader(string title) => new()
 {
  Background = Freeze(new SolidColorBrush(Color.FromRgb(0xF3, 0xEC, 0xDE))),
  BorderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xDF, 0xC3, 0x9D))),
  BorderThickness = new Thickness(1),
  CornerRadius = new CornerRadius(4),
  Padding = Pad(8, 3),
  Margin = new Thickness(0, 4, 0, 3),
  Child = new TextBlock
  {
   Text = title,
   FontSize = 9.5,
   FontWeight = FontWeights.Bold,
   Foreground = ValueBrush
  }
 };

 private static Grid BuildRow5(params (string Label, string Value, double Weight, bool Mono, bool Highlight)[] cells)
 {
  var grid = new Grid { Margin = new Thickness(0, 0, 0, 3) };
  for (int i = 0; i < cells.Length; i++)
  {
   grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cells[i].Weight, GridUnitType.Star) });
   var c = cells[i];
   var border = new Border
   {
    Background = c.Highlight ? HighlightFillBrush : PaperBrush,
    BorderBrush = BoxBorderBrush,
    BorderThickness = new Thickness(1),
    CornerRadius = new CornerRadius(4),
    Padding = Pad(7, 4),
    Margin = new Thickness(i == 0 ? 0 : 3, 0, 0, 0)
   };
   var stack = new StackPanel();
   stack.Children.Add(new TextBlock
   {
    Text = c.Label,
    FontSize = 8.5,
    FontWeight = FontWeights.Bold,
    Foreground = LabelBrush,
    TextTrimming = TextTrimming.CharacterEllipsis
   });
   stack.Children.Add(new TextBlock
   {
    Text = string.IsNullOrWhiteSpace(c.Value) ? "—" : c.Value,
    FontSize = c.Highlight ? 12.5 : 11,
    FontWeight = c.Highlight ? FontWeights.Bold : FontWeights.SemiBold,
    FontFamily = c.Mono ? Typography.Mono : Typography.Ui,
    Foreground = c.Highlight ? AccentBrush : ValueBrush,
    TextWrapping = TextWrapping.Wrap,
    Margin = new Thickness(0, 1, 0, 0)
   });
   border.Child = stack;
   Grid.SetColumn(border, i);
   grid.Children.Add(border);
  }
  return grid;
 }

 private static Grid BuildRow(params (string Label, string Value, double Weight, bool Mono)[] cells)
  => BuildRow5(cells.Select(x => (x.Label, x.Value, x.Weight, x.Mono, false)).ToArray());

 private static Border BuildBarcodeCanvas(string key)
 {
  var host = new Border
  {
   Background = PaperBrush,
   BorderBrush = BoxBorderBrush,
   BorderThickness = new Thickness(1),
   CornerRadius = new CornerRadius(4),
   Height = 34,
   Padding = Pad(6, 4),
   Margin = new Thickness(0, 2, 0, 0)
  };
  var panel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
  string seed = string.IsNullOrWhiteSpace(key) ? "42261005486315000104550010001489201004829104" : key;
  for (int i = 0; i < seed.Length; i++)
  {
   int digit = seed[i] - '0';
   double w1 = (digit % 3) + 1.2;
   double w2 = ((digit + i) % 2) + 1.0;
   panel.Children.Add(new Rectangle { Width = w1, Height = 24, Fill = StrongBorderBrush, Margin = new Thickness(0, 0, 1.2, 0) });
   panel.Children.Add(new Rectangle { Width = w2, Height = 24, Fill = StrongBorderBrush, Margin = new Thickness(0, 0, (digit % 2) + 1.0, 0) });
  }
  host.Child = panel;
  return host;
 }

 private static XElement? Desc(XContainer? c, string localName) => c?.Descendants().FirstOrDefault(e => e.Name.LocalName == localName);
 private static XElement? Child(XElement? e, string localName) => e?.Elements().FirstOrDefault(x => x.Name.LocalName == localName);
 private static string Val(XElement? e, string localName, string fallback = "") => e?.Elements().FirstOrDefault(x => x.Name.LocalName == localName)?.Value.Trim() is { Length: > 0 } v ? v : fallback;
 private static string Attr(XElement? e, string attrName, string fallback = "") => e?.Attributes().FirstOrDefault(a => a.Name.LocalName == attrName)?.Value.Trim() is { Length: > 0 } v ? v : fallback;
 private static string FirstNonEmpty(params string?[] values) => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

 private static string Money(string raw)
  => decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d.ToString("N2", PtBr) : "0,00";

 private static string DecFmt(string raw, string fmt)
  => decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d.ToString(fmt, PtBr) : "0,00";

 private static string AddDec(string a, string b)
 {
  decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out var da);
  decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out var db);
  return (da + db).ToString(CultureInfo.InvariantCulture);
 }

 private static string FormatAccessKey(string key)
 {
  string digits = new(key.Where(char.IsLetterOrDigit).ToArray());
  if (digits.Length == 0) return "—";
  return string.Join(" ", digits.Chunk(4).Select(c => new string(c)));
 }

 private static string FormatTaxId(string raw)
 {
  string d = new(raw.Where(char.IsDigit).ToArray());
  return d.Length switch
  {
   14 => $"{d[..2]}.{d[2..5]}.{d[5..8]}/{d[8..12]}-{d[12..]}",
   11 => $"{d[..3]}.{d[3..6]}.{d[6..9]}-{d[9..]}",
   > 0 => raw,
   _ => "—"
  };
 }

 private static string FormatCep(string raw)
 {
  string d = new(raw.Where(char.IsDigit).ToArray());
  return d.Length == 8 ? $"{d[..5]}-{d[5..]}" : string.IsNullOrWhiteSpace(raw) ? "—" : raw;
 }

 private static string FormatNumber(string raw)
  => long.TryParse(raw, out var n) ? n.ToString("000,000,000", PtBr) : string.IsNullOrWhiteSpace(raw) ? "—" : raw;

 private static string FormatDateTime(string raw)
 {
  if (string.IsNullOrWhiteSpace(raw)) return "—";
  if (DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
   return dto.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", PtBr);
  if (DateOnly.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
   return d.ToString("dd/MM/yyyy", PtBr);
  return raw;
 }

 private static string CombineAddressLine(XElement? addr)
 {
  if (addr is null) return "—";
  string lgr = Val(addr, "xLgr");
  string nro = Val(addr, "nro");
  string cpl = Val(addr, "xCpl");
  string result = string.IsNullOrWhiteSpace(nro) ? lgr : $"{lgr}, {nro}";
  if (!string.IsNullOrWhiteSpace(cpl)) result += $" ({cpl})";
  return string.IsNullOrWhiteSpace(result) ? "—" : result;
 }

 private static string FormatAddress(XElement? addr)
 {
  if (addr is null) return "Endereço não informado";
  return $"{CombineAddressLine(addr)} · {Val(addr, "xBairro")} · {Val(addr, "xMun")}/{Val(addr, "UF")} · CEP {FormatCep(Val(addr, "CEP"))}"
   + (string.IsNullOrWhiteSpace(Val(addr, "fone")) ? "" : $" · Fone: {Val(addr, "fone")}");
 }

 private static string FormatCrt(string crt) => crt switch
 {
  "1" => "1 · Simples Nacional",
  "2" => "2 · Simples Nacional (Excesso Sublimite)",
  "3" => "3 · Regime Normal (Lucro Presumido / Real)",
  "4" => "4 · Simples Nacional — MEI",
  _ => string.IsNullOrWhiteSpace(crt) ? "Não informado" : crt
 };

 private static string FormatIndIe(string ind) => ind switch
 {
  "1" => "Contribuinte ICMS",
  "2" => "Contribuinte Isento",
  "9" => "Não Contribuinte",
  _ => "—"
 };

 private static string FormatIdDest(string idDest) => idDest switch
 {
  "1" => "1 · Operação Interna",
  "2" => "2 · Operação Interestadual",
  "3" => "3 · Operação com Exterior",
  _ => "—"
 };

 private static string FormatModFrete(string mod) => mod switch
 {
  "0" => "0 · Remetente (CIF)",
  "1" => "1 · Destinatário (FOB)",
  "2" => "2 · Terceiros",
  "3" => "3 · Próprio p/ Remetente",
  "4" => "4 · Próprio p/ Destinatário",
  "9" => "9 · Sem Ocorrência de Transporte",
  _ => string.IsNullOrWhiteSpace(mod) ? "—" : mod
 };

 private static string FormatPaymentType(string tPag) => tPag switch
 {
  "01" => "Dinheiro",
  "02" => "Cheque",
  "03" => "Cartão de Crédito",
  "04" => "Cartão de Débito",
  "05" => "Crédito Loja",
  "10" => "Vale Alimentação",
  "11" => "Vale Refeição",
  "14" => "Duplicata Mercantil",
  "15" => "Boleto Bancário",
  "16" => "Depósito Bancário",
  "17" => "PIX",
  "18" => "Transferência / Carteira Digital",
  "90" => "Sem Pagamento",
  "99" => "Outros",
  _ => $"Meio {tPag}"
 };

 private sealed record DanfeItemRow(
  string Item,
  string Code,
  string Description,
  string Ncm,
  string Cst,
  string Cfop,
  string Unit,
  string Qty,
  string UnitPrice,
  string TotalPrice,
  string BcIcms,
  string VlrIcms,
  string AliqIcms,
  string VlrIpi,
  string CstPisCofins,
  string BcPisCofins,
  string AliqPis,
  string VlrPis,
  string AliqCofins,
  string VlrCofins,
  string CstIbsCbs,
  string BcIbsCbs,
  string AliqIbsUf,
  string AliqIbsMun,
  string VlrIbs,
  string AliqCbs,
  string VlrCbs,
  string ImpostoSeletivo);
}
