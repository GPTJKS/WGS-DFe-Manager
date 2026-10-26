using System.Globalization;
using System.Text;
namespace WgsDfe.Domain;

/// <summary>Ranked name search; fiscal identifiers are only normalized, never approximated.</summary>
public static class SearchMatcher
{
 public static string Normalize(string? value)
 {
  var text=new StringBuilder();bool space=true;
  foreach(char c in (value??"").Normalize(NormalizationForm.FormD))
  {
   if(CharUnicodeInfo.GetUnicodeCategory(c)==UnicodeCategory.NonSpacingMark)continue;
   if(char.IsLetterOrDigit(c)){text.Append(char.ToUpperInvariant(c));space=false;}
   else if(!space){text.Append(' ');space=true;}
  }
  return text.ToString().Trim();
 }
 public static string Identifier(string? value)=>new((value??"").Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());
 public static int? Rank(string? query,IEnumerable<string?> names,IEnumerable<string?> identifiers)
 {
  string q=Normalize(query);if(q.Length==0)return string.IsNullOrWhiteSpace(query)?0:null;int best=int.MaxValue;string id=Identifier(query);
  foreach(var candidate in identifiers)
  {
   string value=Identifier(candidate);
   if(id.Length>0&&value.Length>0)
   {
    if(value==id)best=Math.Min(best,0);
    else if(value.StartsWith(id,StringComparison.Ordinal))best=Math.Min(best,100);
    else if(value.Contains(id,StringComparison.Ordinal)&&(value.Length<40||id.Length>=8))best=Math.Min(best,200);
   }
  }
  var values=names.Select(Normalize).Where(x=>x.Length>0).ToArray();
  foreach(string value in values){string compact=value.Replace(" ",""),compactQuery=q.Replace(" ","");if(compact.Contains(compactQuery,StringComparison.Ordinal))best=Math.Min(best,compact==compactQuery?0:compact.StartsWith(compactQuery,StringComparison.Ordinal)?100:200);if(value==q)best=Math.Min(best,0);else if(value.StartsWith(q,StringComparison.Ordinal))best=Math.Min(best,100);else if(value.Contains(q,StringComparison.Ordinal))best=Math.Min(best,200);}
  string[] tokens=q.Split(' '),words=string.Join(' ',values).Split(' ',StringSplitOptions.RemoveEmptyEntries);
  if(tokens.All(token=>words.Any(word=>word.Contains(token,StringComparison.Ordinal))))best=Math.Min(best,300);
  // Any digit makes the complete query deterministic, including alphanumeric CNPJ.
  if(!q.Any(char.IsDigit)&&q.Length>=4&&best>300)
  {
   int distance=0;bool match=true;
   foreach(string token in tokens)
   {
    int limit=token.Length>=8?2:token.Length>=4?1:0;int nearest=words.Select(word=>Distance(token,word,limit)).DefaultIfEmpty(limit+1).Min();
    if(nearest>limit){match=false;break;}distance+=nearest;
   }
   if(match)best=400+distance;
  }
  return best==int.MaxValue?null:best;
 }
 /// <summary>
 /// Precise ranking for fiscal documents. Purely numeric queries (digits with optional . - / spaces) are treated as
 /// fiscal identifiers: up to 9 digits matches the document number by numeric value (1103 = 000001103), never as a
 /// substring of access keys or CNPJs; 8+ digits also matches CNPJ/CPF prefixes (CNPJ root); 44 digits matches the
 /// exact access key. Also handles common fiscal prefixes like NFC 1103, NF-e 1103, Nº 1103, Nota 1103.
 /// Any other query falls back to <see cref="Rank"/>.
 /// </summary>
 public static int? RankFiscal(string? query,IEnumerable<string?> names,IEnumerable<string?> identifiers,IEnumerable<string?> documentNumbers)
 {
  string raw=(query??"").Trim();
  string stripped=CleanFiscalPrefix(raw);
  string digits=Identifier(stripped);
  bool numeric=digits.Length>0&&digits.All(char.IsAsciiDigit)&&stripped.All(c=>char.IsAsciiDigit(c)||c is '.' or '-' or '/' or ' ');
  if(!numeric)
  {
   string[] parts=stripped.Split([' '],StringSplitOptions.RemoveEmptyEntries);
   if(parts.Length>=2&&parts.Any(p=>p.All(char.IsAsciiDigit)&&p.Length<=9))
   {
    string numPart=parts.First(p=>p.All(char.IsAsciiDigit)&&p.Length<=9);
    string namePart=string.Join(' ',parts.Where(p=>p!=numPart));
    string numVal=numPart.TrimStart('0');if(numVal.Length==0)numVal="0";
    bool numMatch=documentNumbers.Any(n=>{string id=Identifier(n).TrimStart('0');if(id.Length==0&&Identifier(n).Length>0)id="0";return id==numVal;});
    if(numMatch)
    {
     var nameScore=Rank(namePart,names,[]);
     if(nameScore.HasValue)return nameScore.Value;
    }
   }
   return Rank(query,names,identifiers.Concat(documentNumbers));
  }
  string value=digits.TrimStart('0');if(value.Length==0)value="0";
  int best=int.MaxValue;
  var ids=identifiers.Select(Identifier).Where(x=>x.Length>0).ToArray();
  if(digits.Length<=9)
  {
   foreach(var number in documentNumbers){string n=Identifier(number).TrimStart('0');if(n.Length==0&&Identifier(number).Length>0)n="0";if(n==value)best=0;}
   foreach(var id in ids){if(id.Length<=15&&id.TrimStart('0')==value)best=Math.Min(best,10);else if(digits.Length>=8&&(id.Length is 11 or 14)&&id.StartsWith(digits,StringComparison.Ordinal))best=Math.Min(best,150);}
  }
  else if(digits.Length==44){foreach(var id in ids)if(id==digits)best=0;}
  else
  {
   foreach(var id in ids)
   {
    if(id==digits)best=Math.Min(best,0);
    else if(id.StartsWith(digits,StringComparison.Ordinal))best=Math.Min(best,100);
    else if(digits.Length>=15&&id.Contains(digits,StringComparison.Ordinal))best=Math.Min(best,200);
   }
  }
  return best==int.MaxValue?null:best;
 }
 private static string CleanFiscalPrefix(string raw)
 {
  string s=raw.Trim();string upper=s.ToUpperInvariant();
  string[] prefixes=["NFC-E","NFCE","NFC","NF-E","NFE","NF","CT-E","CTE","NFCOM","NOTA FISCAL","NOTA","Nº","N°","NO.","NO","NUM.","NUMERO","NUM","N."];
  foreach(var p in prefixes)
  {
   if(upper.StartsWith(p,StringComparison.Ordinal))
   {
    string rest=s[p.Length..].Trim();
    if(rest.Length>0&&(char.IsAsciiDigit(rest[0])||rest[0] is ' ' or ':' or '-' or '.' or 'º' or '°'))
     return rest.TrimStart(' ',':','-','.','º','°');
   }
  }
  return s;
 }
 private static int Distance(string a,string b,int limit)
 {
  if(Math.Abs(a.Length-b.Length)>limit||a.Length>200||b.Length>300)return limit+1;
  var before=new int[b.Length+1];var previous=Enumerable.Range(0,b.Length+1).ToArray();
  for(int i=1;i<=a.Length;i++)
  {
   var current=new int[b.Length+1];current[0]=i;int minimum=i;
   for(int j=1;j<=b.Length;j++){current[j]=Math.Min(Math.Min(previous[j]+1,current[j-1]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));if(i>1&&j>1&&a[i-1]==b[j-2]&&a[i-2]==b[j-1])current[j]=Math.Min(current[j],before[j-2]+1);minimum=Math.Min(minimum,current[j]);}
   if(minimum>limit)return limit+1;before=previous;previous=current;
  }
  return previous[b.Length];
 }
}
