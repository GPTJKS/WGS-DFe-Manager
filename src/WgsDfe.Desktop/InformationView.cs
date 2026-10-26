using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WgsDfe.Desktop;

public sealed class InformationView : WorkspaceView
{
 public bool About { get; }

 public InformationView(bool about)
 {
  About = about;
  Title = about ? "Sobre o programa" : "Ajuda";
  var panel = new StackPanel { Margin = new Thickness(24, 18, 24, 24), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
  var eyebrow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 2) };
  eyebrow.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = (Brush)FindResource("GoldBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 4) });
  eyebrow.Children.Add(new TextBlock { Text = about ? "APRESENTAÇÃO INSTITUCIONAL & ESPECIFICAÇÕES DA PLATAFORMA" : "GUIA DE OPERAÇÃO FISCAL · DOCUMENTAÇÃO", Style = (Style)FindResource("Eyebrow") });
  panel.Children.Add(eyebrow);
  panel.Children.Add(new TextBlock { Text = about ? "Sobre o WGS DF-e Manager 0.17.5" : Title, Style = (Style)FindResource("PageTitle"), Margin = new Thickness(0, 0, 0, 10) });

  if (about)
  {
   // Executive Brand Hero Card (Warm Champagne Gold & Alabaster Stone)
   var heroBg = new LinearGradientBrush(
    Color.FromRgb(0xF8, 0xF3, 0xE8),
    Color.FromRgb(0xF1, 0xE8, 0xD6),
    new Point(0, 0),
    new Point(1, 1));
   var heroCard = new Border
   {
    Background = heroBg,
    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0xC2, 0x97)),
    BorderThickness = new Thickness(1),
    CornerRadius = new CornerRadius(14),
    Padding = new Thickness(20, 16, 20, 16),
    Margin = new Thickness(0, 4, 0, 12)
   };
   var heroDock = new DockPanel();
   var logoBox = new Border
   {
    Width = 56,
    Height = 56,
    CornerRadius = new CornerRadius(13),
    Background = new SolidColorBrush(Color.FromRgb(0xFA, 0xF6, 0xEE)),
    BorderBrush = new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x34)),
    BorderThickness = new Thickness(1.5),
    Margin = new Thickness(0, 0, 16, 0),
    ClipToBounds = true,
    VerticalAlignment = VerticalAlignment.Center
   };
   try
   {
    logoBox.Child = new Image
    {
     Source = new BitmapImage(new Uri("pack://application:,,,/Assets/wgs-icon.png")),
     Stretch = Stretch.UniformToFill
    };
   }
   catch { }
   DockPanel.SetDock(logoBox, Dock.Left);
   heroDock.Children.Add(logoBox);

   var heroText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
   heroText.Children.Add(new TextBlock
   {
    Text = "WGS DF-e Manager · Versão 0.17.5 (Edição Executiva Corporativa)",
    FontFamily = Typography.Ui,
    FontSize = 18,
    FontWeight = FontWeights.SemiBold,
    Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B))
   });
   heroText.Children.Add(new TextBlock
   {
    Text = "Plataforma especializada em governança, captura automatizada, auditoria criptográfica e visualização gráfica de Documentos Fiscais Eletrônicos (NF-e, NFC-e, CT-e, CT-e OS e NFCom).",
    FontSize = 12.5,
    Foreground = new SolidColorBrush(Color.FromRgb(0x4E, 0x4A, 0x43)),
    TextWrapping = TextWrapping.Wrap,
    Margin = new Thickness(0, 4, 0, 0)
   });
   heroDock.Children.Add(heroText);
   heroCard.Child = heroDock;
   panel.Children.Add(heroCard);

   Add("1. Propósito e Excelência Operacional",
    "O WGS DF-e Manager foi projetado para escritórios de contabilidade, departamentos fiscais e empresas que exigem rigor técnico, velocidade e rastreabilidade na recepção e guarda de documentos fiscais eletrônicos.\n\n" +
    "Concebido a partir da experiência prática da WGS Assessoria Contábil, o sistema elimina gargalos manuais de coleta de XMLs, centraliza a administração de múltiplos CNPJs e certificados digitais A1 e assegura que nenhuma versão de nota fiscal ou evento legal seja perdida.");

   Add("2. Principais Capacidades da Versão 0.17.5",
    "• Sincronismo Inteligente Multiproviders: Integração direta com o Ambiente Nacional de Distribuição de DF-e (AN/SEFAZ) e webservices estaduais (SEF/SC) para captura contínua de NF-e (55), NFC-e (65), CT-e (57), CT-e OS (67) e NFCom (62).\n" +
    "• Visualizador Gráfico Nativo (Espelho DANFE / DACTE): Renderização imediata da nota fiscal em tela com quadro completo de tributação (BC ICMS, ICMS, ICMS ST, IPI, PIS, COFINS, DIFAL, FCP e Tributos Aproximados), emitente, destinatário, itens detalhados com NCM/CST/CFOP, duplicatas e transporte.\n" +
    "• Recuperação Histórica em Lote: Importação estruturada de planilhas e listas de chaves de 44 dígitos para auditoria de acervos retroativos, cruzamento instantâneo com o repositório local e retomada automática respeitando as janelas de liberação da SEFAZ.\n" +
    "• Diagnóstico Assistido: Tradução clara de códigos técnicos e retornos da SEFAZ (como cStat 137, 138 e 656), com cronograma exato da próxima janela permitida para cada empresa.");

   Add("3. Segurança, Conformidade e Governança Corporativa",
    "• Cofre de Certificados Protegido (DPAPI): Os arquivos PFX (Certificados A1 de empresas e procurações contábeis) são criptografados com proteção nativa do Windows vinculada ao perfil de segurança da estação, jamais expondo senhas em logs ou diagnósticos.\n" +
    "• Custódia Imutável e Auditoria SHA-256: Cada XML armazenado no repositório local possui assinatura de integridade SHA-256 verificável. Divergências, cancelamentos, cartas de correção e múltiplas versões são preservados integralmente para fins de auditoria fiscal e contábil.\n" +
    "• Isolamento Organizacional e Banco Transacional: Arquitetura nativa sobre .NET 10 (64 bits) e motor relacional SQLite com controle estrito de concorrência, garantindo alta performance mesmo em acervos com centenas de milhares de documentos.");

   Add("4. Identidade Institucional & Direitos",
    "Idealização e Engenharia Fiscal: WGS Assessoria Contábil (wgsassessoria.com.br).\n" +
    "Licenciamento Corporativo: Permite personalização da razão social do escritório ou organização titular em Configurações → Identificação Corporativa.\n" +
    "Recomenda-se manter rotinas periódicas de backup do banco de dados e preservação externa dos XMLs conforme os prazos previstos na legislação tributária brasileira.");
  }
  else
  {
   Add("1. Cadastre suas empresas e certificados", "Em Empresas, cadastre ou importe uma planilha pelo modelo fornecido. O e-CNPJ da empresa atende à distribuição nacional; para as fontes SC, use o certificado do contador em Certificados.");
   Add("2. Consulte todos os lotes disponíveis", "Em Consultas, escolha NF-e, NFC-e, NFCom ou CT-e/OS, selecione as empresas e informe o período de emissão. A continuação automática busca os lotes seguintes enquanto a fonte permitir. Você pode cancelar com segurança. A fonte pode impor um horário de espera, apresentado no resultado.");
   Add("3. Entenda o período", "Os serviços de distribuição percorrem a sequência de NSU e podem entregar XMLs de outros meses. As notas exibidas em Notas por período e a exportação respeitam as datas escolhidas. A data não reinicia a sequência nem permite recuperar documentos que a fonte já não disponibiliza.");
   Add("4. Visualize a Nota Fiscal (DANFE / DACTE) e Exporte seus XMLs", "Em qualquer lista de notas (Consultas, Documentos ou Painel Inicial), clique em Visualizar Nota (DANFE / XML) ou dê duplo clique para abrir o espelho gráfico completo da nota com impostos, itens, emitente e destinatário. Em Exportar XMLs, escolha pasta ou ZIP sem limite de 500 notas por empresa.");
   Add("5. Veja o diagnóstico", "Na aba Resultado e diagnóstico, selecione uma empresa e clique em Ver diagnóstico. O painel mostra o resultado, documentos recebidos, horário de espera e orientação. Os códigos e registros ficam em Detalhes técnicos. Copiar diagnóstico permite encaminhar essas informações ao suporte sem expor senha ou certificado.");
   Add("6. Exclusão de empresas", "Excluir remove a empresa das listas e interrompe suas automações. Os XMLs, vínculos e histórico ficam preservados para não perder documentos nem reiniciar prazos da SEFAZ. Cadastrar o mesmo CNPJ recupera a posição anterior.");
  }

  if(!about)
  {
   panel.Children.Add(new TextBlock{Text="Atalhos de teclado",Style=(Style)FindResource("SectionTitle"),Margin=new Thickness(0,18,0,8)});
   var shortcuts=new DataGrid{IsReadOnly=true,AutoGenerateColumns=false,CanUserAddRows=false,HeadersVisibility=DataGridHeadersVisibility.Column,ItemsSource=new[]{
    new{Atalho="Ctrl + rolagem",Acao="Aumentar ou diminuir o zoom (75% a 150%)"},
    new{Atalho="Ctrl + 0",Acao="Restaurar zoom para 100%"},
    new{Atalho="Ctrl + F",Acao="Focar a pesquisa visível da tela atual"},
    new{Atalho="F1",Acao="Abrir Ajuda"},
    new{Atalho="Alt + ←",Acao="Voltar à tela anterior ou fechar a visualização secundária"},
    new{Atalho="Esc",Acao="Fechar menus e visualizações; alterações pendentes pedem confirmação"}
   }};
   shortcuts.Columns.Add(new DataGridTextColumn{Header="Atalho",Binding=new System.Windows.Data.Binding("Atalho"),Width=180});
   shortcuts.Columns.Add(new DataGridTextColumn{Header="Ação",Binding=new System.Windows.Data.Binding("Acao"),Width=new DataGridLength(1,DataGridLengthUnitType.Star)});
   panel.Children.Add(shortcuts);
  }
  Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };

  void Add(string title, string text)
  {
   var inner = new StackPanel();
   inner.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 14.5, Foreground = new SolidColorBrush(Color.FromRgb(0x14, 0x16, 0x1B)), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
   inner.Children.Add(new TextBlock { Text = text.Replace("\\n", "\n"), TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0x47, 0x42)), FontSize = 12.5, LineHeight = 20 });
   panel.Children.Add(new Border { Style = (Style)FindResource("Card"), Padding = new Thickness(16, 12, 16, 12), Child = inner, Margin = new Thickness(0, 8, 0, 0) });
  }
 }
}
