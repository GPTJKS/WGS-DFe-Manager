using System.Globalization;
using System.Text;
namespace WgsDfe.Domain;
public static class SearchNormalizer
{
 public static string Normalize(string? text)
 {
  if(text is null)return "";
  var result=new StringBuilder();
  foreach(var c in text.Normalize(NormalizationForm.FormD))
   if(CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark)result.Append(char.ToUpperInvariant(c));
  return result.ToString().Normalize(NormalizationForm.FormC).Trim();
 }
 public static string ForCompany(Company company)=>Normalize($"{company.LegalName} {company.TradeName} {company.Cnpj} {Cnpj.Format(company.Cnpj)} {company.City}");
}
