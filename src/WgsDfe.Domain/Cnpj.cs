using System.Text.RegularExpressions;
namespace WgsDfe.Domain;
public static partial class Cnpj
{
 // Receita Federal / SERPRO: ASCII - 48, pesos 2..9, módulo 11.
 public static bool TryNormalize(string? input,out string normalized)
 {
  normalized="";
  if(input is null)return false;
  var text=input.Trim().ToUpperInvariant();
  if(Masked().IsMatch(text))text=text.Replace(".","").Replace("/","").Replace("-","");
  if(!Unmasked().IsMatch(text) || text.Distinct().Count()==1)return false;
  if(Digit(text.AsSpan(0,12))!=text[12]-'0' || Digit(text.AsSpan(0,13))!=text[13]-'0')return false;
  normalized=text;return true;
 }
 public static string Format(string value)=>value.Length==14 ? $"{value[..2]}.{value[2..5]}.{value[5..8]}/{value[8..12]}-{value[12..]}" : value;
 private static int Digit(ReadOnlySpan<char> value)
 {
  var sum=0;var weight=2;
  for(var i=value.Length-1;i>=0;i--){sum+=(value[i]-48)*weight;weight=weight==9?2:weight+1;}
  var remainder=sum%11;return remainder<2?0:11-remainder;
 }
 [GeneratedRegex("^[0-9A-Z]{12}[0-9]{2}$",RegexOptions.CultureInvariant)] private static partial Regex Unmasked();
 [GeneratedRegex("^[0-9A-Z]{2}\\.[0-9A-Z]{3}\\.[0-9A-Z]{3}/[0-9A-Z]{4}-[0-9]{2}$",RegexOptions.CultureInvariant)] private static partial Regex Masked();
}
