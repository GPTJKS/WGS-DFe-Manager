using System.Text;
using WgsDfe.Application.Archive;
namespace WgsDfe.Infrastructure.Archive;
public static class ArchiveReport
{
 public static async Task SaveAsync(ArchiveImportResult result,string path,CancellationToken ct=default)
 {
  await using var output=new StreamWriter(path,false,new UTF8Encoding(true));
  await output.WriteLineAsync("Item;Origem;Resultado;Chave;Código".AsMemory(),ct);
  foreach(var row in result.Rows)await output.WriteLineAsync(string.Join(';',new[]{row.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),row.Source,row.Result,row.Key??"",row.Code}.Select(Quote)).AsMemory(),ct);
 }
 private static string Quote(string value)
 {if(value.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@'||value.StartsWith('\t')||value.StartsWith('\r'))value="'"+value;return "\""+value.Replace("\"","\"\"")+"\"";}
}
