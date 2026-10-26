using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
namespace WgsDfe.Desktop;
public sealed class PillBadge : Border
{
 public static readonly DependencyProperty TextProperty=DependencyProperty.Register(nameof(Text),typeof(string),typeof(PillBadge),new PropertyMetadata("",(target,_)=>((PillBadge)target).Update()));
 public string Text {get=>(string)GetValue(TextProperty);set=>SetValue(TextProperty,value);}
 public PillBadge()
 {
  CornerRadius=new CornerRadius(7);Padding=new Thickness(8,2,8,2);HorizontalAlignment=HorizontalAlignment.Left;VerticalAlignment=VerticalAlignment.Center;
  Loaded+=(_,_)=>Update();
 }
 private void Update()
 {
  string text=Text??"";
  string foreground="#4A4742",background="#F3F2EE",dotColor="";string? glyph=null;string glyphColor="#059669";
  if(text is "Ativa" or "No prazo" or "138"||text.StartsWith("Autorizado",StringComparison.Ordinal)||text.StartsWith("Conectado",StringComparison.Ordinal)||text.StartsWith("Sincronizado",StringComparison.Ordinal)||text.StartsWith("Disponível",StringComparison.OrdinalIgnoreCase))
  {foreground="#065F46";background="#ECFDF5";dotColor="#10B981";}
  else if(text is "NF-e" or "55"||text.StartsWith("NF-e",StringComparison.OrdinalIgnoreCase))
  {foreground="#3730A3";background="#EEF2FF";}
  else if(text is "NFC-e" or "65" or "NFCom" or "62"||text.StartsWith("NFC-e",StringComparison.OrdinalIgnoreCase))
  {foreground="#0F766E";background="#F0FDFA";}
  else if(text is "CT-e" or "57" or "CT-e OS" or "67"||text.StartsWith("CT-e",StringComparison.OrdinalIgnoreCase))
  {foreground="#C2410C";background="#FFF7ED";}
  else if(text.StartsWith("Aceito",StringComparison.OrdinalIgnoreCase)||text is "Repositório local" or "Lotes de chaves" or "Produção")
  {foreground="#44403C";background="#F3F2EE";glyph="\uE73E";glyphColor="#E08A34";}
  else if(text.Contains("Vencido",StringComparison.OrdinalIgnoreCase)||text.StartsWith("Cancelado",StringComparison.Ordinal)||text.StartsWith("Recusado",StringComparison.Ordinal)||text.StartsWith("Denegado",StringComparison.Ordinal)||text.Contains("Não verificado",StringComparison.OrdinalIgnoreCase))
  {foreground="#BE123C";background="#FFF1F2";glyph="\uE783";glyphColor="#E11D48";}
  else if(text is "Inativa" or "137"||text.Contains("vence em",StringComparison.OrdinalIgnoreCase)||text.Contains("não configurada",StringComparison.OrdinalIgnoreCase)||text.Contains("Não confirmado",StringComparison.OrdinalIgnoreCase)||text.Contains("Aguardar",StringComparison.OrdinalIgnoreCase))
  {foreground="#B45309";background="#FFFBEB";dotColor="#F59E0B";}
  else if(text is "Não testada")
  {foreground="#57534E";background="#F3F2EE";dotColor="#A8A29E";}
  else if(text.Length<=6)
  {foreground="#14161B";background="#EFECE6";}
  var converter=new BrushConverter();
  Background=(Brush)converter.ConvertFromString(background)!;
  var fg=(Brush)converter.ConvertFromString(foreground)!;
  var label=new TextBlock{Text=text,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=fg,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,ToolTip=text};
  if(glyph is not null)
  {
   var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   row.Children.Add(new TextBlock{Text=glyph,FontFamily=new FontFamily("Segoe MDL2 Assets"),FontSize=11,Foreground=(Brush)converter.ConvertFromString(glyphColor)!,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});
   row.Children.Add(label);Child=row;
  }
  else if(dotColor.Length>0)
  {
   var row=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};
   row.Children.Add(new Ellipse{Width=6,Height=6,Fill=(Brush)converter.ConvertFromString(dotColor)!,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});
   row.Children.Add(label);Child=row;
  }
  else Child=label;
 }
}
public sealed class InitialsConverter : IValueConverter
{
 public static readonly InitialsConverter Instance=new();
 public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)
 {
  if(value is not string name||string.IsNullOrWhiteSpace(name))return "DF";
  var parts=name.Split([' ','-','/','&','.'],StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries)
   .Where(p=>char.IsLetterOrDigit(p[0])&&!p.Equals("DE",StringComparison.OrdinalIgnoreCase)&&!p.Equals("DA",StringComparison.OrdinalIgnoreCase)&&!p.Equals("DO",StringComparison.OrdinalIgnoreCase)&&!p.Equals("E",StringComparison.OrdinalIgnoreCase)&&!p.Equals("LTDA",StringComparison.OrdinalIgnoreCase)&&!p.Equals("ME",StringComparison.OrdinalIgnoreCase)&&!p.Equals("SA",StringComparison.OrdinalIgnoreCase)).ToArray();
  if(parts.Length>=2)return $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[1][0])}";
  if(parts.Length==1)return parts[0].Length>=2?parts[0][..2].ToUpperInvariant():parts[0].ToUpperInvariant();
  return name.Trim()[..Math.Min(2,name.Trim().Length)].ToUpperInvariant();
 }
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
public sealed class KeyBlocksConverter : IValueConverter
{
 public static readonly KeyBlocksConverter Instance=new();
 public object Convert(object? value,Type targetType,object? parameter,CultureInfo culture)
 {
  if(value is not string s||string.IsNullOrWhiteSpace(s))return "—";
  var digits=new string(s.Where(char.IsDigit).ToArray());
  if(digits.Length!=44)return s;
  return string.Join(" ",Enumerable.Range(0,11).Select(i=>digits.Substring(i*4,4)));
 }
 public object ConvertBack(object? value,Type targetType,object? parameter,CultureInfo culture)=>Binding.DoNothing;
}
