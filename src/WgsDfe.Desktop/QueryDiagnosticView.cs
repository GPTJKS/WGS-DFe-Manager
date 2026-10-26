using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WgsDfe.Application.Nfe;
namespace WgsDfe.Desktop;
public sealed class QueryDiagnosticView:WorkspaceView
{
 public QueryDiagnosticView(string company,string provider,string code,int? status,string nsu,int pages,int documents,int pending,DateTime? next,string description)
 {
  Title="Diagnóstico da consulta";
  PreferredDialogSize=new Size(860,560);
  string instruction=code switch
  {
   "WAIT_UNTIL_ALLOWED"=>next.HasValue?"Aguarde o horário indicado. O programa preserva a posição e não deve repetir a consulta durante o bloqueio.":"A consulta está em espera, mas o horário de liberação não foi informado. Confira os detalhes antes de tentar novamente.",
   "NFE_PAGE_COMMITTED" or "CTE_PAGE_COMMITTED"=>"Há lotes seguintes. Retorne à consulta, abra Opções avançadas, deixe Lotes = 0 (automático) e consulte novamente. O NSU salvo permite continuar sem recomeçar.",
   "NFE_ACCOUNTANT_LINK_DENIED"=>"Confira se o contador possui autorização para o CNPJ na SEF/SC e se o certificado correto foi selecionado em Certificados.",
   _ when code.StartsWith("NETWORK",StringComparison.Ordinal)||code.StartsWith("TLS",StringComparison.Ordinal)=>"Confira a conexão, data/hora do Windows e a validade do certificado. A posição recebida foi preservada; respeite o próximo horário permitido antes de repetir.",
   "NFE_SYNCHRONIZED" or "CTE_SYNCHRONIZED"=>"A sequência disponível terminou. O período filtra a exibição e a exportação; ausência de notas antigas não comprova inexistência de documentos na fonte.",
   _=>NfeMessages.Describe(code)+". Confira tipo habilitado na empresa, UF e certificado. Copie o diagnóstico para análise do suporte se o problema persistir."
  };
  string text=$"Empresa: {company}\nFonte / ambiente: {provider}\nResultado: {description}\nCódigo técnico: {code}\ncStat: {status?.ToString()??"Não recebido / não informado"}\nÚltimo NSU: {nsu}\nLotes nesta operação: {pages}\nXMLs recebidos: {documents}\nChaves/resumos ou itens pendentes: {pending}\nPróxima tentativa (horário local): {next?.ToString("dd/MM/yyyy HH:mm:ss")??"Não informado pela fonte"}\n\nOrientação: {instruction}\n\nA data de emissão filtra as notas e a exportação. O serviço de distribuição pode entregar outros meses na sequência do NSU.";
  var layout=new DockPanel{Margin=new Thickness(20,16,20,16)};
  var header=new StackPanel{Margin=new Thickness(0,0,0,10)};
  var eyebrow=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,2)};
  eyebrow.Children.Add(new System.Windows.Shapes.Ellipse{Width=8,Height=8,Fill=(Brush)FindResource("GoldBrush"),VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,6,4)});
  eyebrow.Children.Add(new TextBlock{Text="RESULTADO DA CONSULTA",Style=(Style)FindResource("Eyebrow")});
  header.Children.Add(eyebrow);
  header.Children.Add(new TextBlock{Text=Title,Style=(Style)FindResource("PageTitle"),Margin=new Thickness(0,0,0,8)});
  var cards=new System.Windows.Controls.Primitives.UniformGrid{Columns=2,Margin=new Thickness(0,0,0,10)};
  Border Metric(string label,string val,bool last=false)
  {
   var stack=new StackPanel();
   stack.Children.Add(new TextBlock{Text=label,Style=(Style)FindResource("Eyebrow"),Margin=new Thickness(0,0,0,2)});
   stack.Children.Add(new TextBlock{Text=val,FontSize=13.5,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("InkBrush"),TextWrapping=TextWrapping.Wrap});
   return new Border{Style=(Style)FindResource("Card"),Padding=new Thickness(12,8,12,8),Margin=last?new Thickness(0):new Thickness(0,0,8,0),Child=stack};
  }
  cards.Children.Add(Metric("EMPRESA / FONTE",$"{company}\n{provider}"));

  cards.Children.Add(Metric("DOCUMENTOS NESTA CONSULTA",$"{documents} XML(s) recebido(s) · {pending} resumo(s)/pendência(s)"));
  header.Children.Add(new TextBlock{Text=next.HasValue?$"Próxima consulta permitida: {next:dd/MM/yyyy HH:mm:ss} (horário local).":"Próxima consulta: horário não informado pela fonte.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,8)});
  header.Children.Add(cards);
  var guidanceStack=new StackPanel();
  guidanceStack.Children.Add(new TextBlock{Text="RESULTADO E ORIENTAÇÃO RECOMENDADA",Style=(Style)FindResource("Eyebrow"),Foreground=new SolidColorBrush(Color.FromRgb(194,65,12))});
  guidanceStack.Children.Add(new TextBlock{Text=description,FontSize=14.5,FontWeight=FontWeights.Bold,Foreground=(Brush)FindResource("InkBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,2,0,6)});
  guidanceStack.Children.Add(new TextBlock{Text="Orientação: "+instruction,FontSize=13.5,FontWeight=FontWeights.Medium,Foreground=new SolidColorBrush(Color.FromRgb(74,71,66)),TextWrapping=TextWrapping.Wrap});
  header.Children.Add(new Border{Background=new SolidColorBrush(Color.FromRgb(247,246,242)),BorderBrush=new SolidColorBrush(Color.FromRgb(228,225,216)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(14,10,14,10),Child=guidanceStack});

  var copy=new Button{Content="Copiar diagnóstico",Style=(Style)FindResource("PrimaryButton"),HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,10,0,0)};
  copy.Click+=(_,_)=>{try{Clipboard.SetText(text);copy.Content="Diagnóstico copiado";}catch(System.Runtime.InteropServices.COMException){copy.Content="Não foi possível copiar. Selecione o texto abaixo.";}};
  DockPanel.SetDock(copy,Dock.Bottom);layout.Children.Add(copy);
  header.Children.Add(new TextBlock{Text=documents>0?"Confira os documentos recebidos na lista do acervo. Os detalhes abaixo informam eventuais pendências de processamento.":"Nenhum XML recebido nesta consulta. Isso não comprova a inexistência de notas na fonte ou no acervo.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,8)});
  header.Children.Add(new Expander{Header="Detalhes técnicos",Content=new TextBox{Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Visible,FontFamily=Typography.Mono,FontSize=13.5,Padding=new Thickness(12,8,12,8),AcceptsReturn=true,MaxHeight=280}});
  layout.Children.Add(new ScrollViewer{Content=header,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  Content=layout;
 }
}
