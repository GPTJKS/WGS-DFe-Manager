using System.Text;
using WgsDfe.Application.Companies;
namespace WgsDfe.Infrastructure.Companies;
public static class CompanyImportReport
{
 public static void SaveCsv(string path,IReadOnlyList<ImportRow> rows)
 {
  using var writer=new StreamWriter(path,false,new UTF8Encoding(true));writer.WriteLine("Linha;CNPJ;RazaoSocial;Situacao;Motivo");
  foreach(var row in rows)writer.WriteLine(string.Join(';',new[]{row.RowNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),row.Data?.Cnpj??row.RawCnpj,row.Data?.LegalName??"",Label(row.Status),row.Reason}.Select(Safe)));
 }
 public static string Label(ImportRowStatus status)=>status switch
 {ImportRowStatus.Ready=>"Pronta",ImportRowStatus.Imported=>"Importada",ImportRowStatus.Invalid=>"Inválida",ImportRowStatus.DuplicateInFile=>"Duplicada no arquivo",ImportRowStatus.DuplicateInDatabase=>"Já cadastrada",ImportRowStatus.Cancelled=>"Não processada",_=>"Desconhecida"};
 private static string Safe(string value)
 {
  var trimmed=value.TrimStart();if(trimmed.StartsWith('=')||trimmed.StartsWith('+')||trimmed.StartsWith('-')||trimmed.StartsWith('@')||value.StartsWith('\t')||value.StartsWith('\r'))value="'"+value;
  return "\""+value.Replace("\"","\"\"")+"\"";
 }
}
