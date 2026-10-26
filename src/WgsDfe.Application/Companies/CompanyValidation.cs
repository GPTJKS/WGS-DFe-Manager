using WgsDfe.Domain;
namespace WgsDfe.Application.Companies;
public static class CompanyValidation
{
 public static IReadOnlyList<string> States {get;}=Array.AsReadOnly(new[]{"AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"});
 public static CompanyInput Normalize(CompanyInput input)
 {
  if(!Cnpj.TryNormalize(input.Cnpj,out var cnpj))throw Invalid("Informe um CNPJ válido, numérico ou alfanumérico, com os 14 caracteres e dígitos verificadores.");
  var legal=Text(input.LegalName,300,"Razão social");
  if(legal is null)throw Invalid("Informe a razão social.");
  var state=input.State?.Trim().ToUpperInvariant()??"";
  if(!States.Contains(state))throw Invalid("Selecione uma UF válida.");
  if((input.EnabledDocuments&~EnabledDocuments.All)!=0)throw Invalid("Tipos de documento inválidos.");
  var storage=Text(input.StorageRelativePath,200,"Pasta de armazenamento");
  if(storage is not null)
  {
   storage=storage.Replace('\\','/');
   if(storage.StartsWith('/') || storage.Split('/').Any(p=>p is "" or "." or ".." || p.Any(c=>c is '<' or '>' or ':' or '"' or '|' or '?' or '*' || char.IsControl(c)) || p.EndsWith('.') || p.EndsWith(' ') || Reserved(p)))
    throw Invalid("A pasta deve ser relativa ao acervo, sem caracteres inválidos ou .. (exemplo: clientes/empresa). ");
  }
  return input with {LegalName=legal,Cnpj=cnpj,TradeName=Text(input.TradeName,300,"Nome fantasia"),State=state,
   StateRegistration=Text(input.StateRegistration,40,"Inscrição estadual"),City=Text(input.City,150,"Município"),
   StorageRelativePath=storage,Notes=Text(input.Notes,4000,"Observações")};
 }
 private static string? Text(string? value,int max,string label)
 {
  var text=value?.Trim();if(string.IsNullOrEmpty(text))return null;
  if(text.Length>max)throw Invalid($"{label}: limite de {max} caracteres.");
  if(text.Any(c=>char.IsControl(c)&&c is not '\r' and not '\n' and not '\t'))throw Invalid($"{label} contém caracteres inválidos.");
  return text;
 }
 private static bool Reserved(string component)
 {
  var root=component.Split('.')[0].ToUpperInvariant();
  return root is "CON" or "PRN" or "AUX" or "NUL" ||
   (root.Length==4 && (root.StartsWith("COM",StringComparison.Ordinal)||root.StartsWith("LPT",StringComparison.Ordinal)) && root[3] is >= '1' and <= '9');
 }
 private static CompanyException Invalid(string message)=>new(CompanyError.InvalidData,message);
}
