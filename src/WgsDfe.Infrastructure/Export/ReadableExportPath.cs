using System.Globalization;
using WgsDfe.Application.Export;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Export;
internal static class ReadableExportPath
{
 private static readonly string[] Months=["Janeiro","Fevereiro","Março","Abril","Maio","Junho","Julho","Agosto","Setembro","Outubro","Novembro","Dezembro"];
 public static string Segment(string value,int maximum=100)
 {
  var invalid="<>:\"/\\|?*";var clean=new string(value.Where(c=>!char.IsControl(c)&&!invalid.Contains(c)).ToArray());
  clean=string.Join(' ',clean.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Trim(' ','.');
  if(clean.Length==0)clean="Empresa sem nome";
  string stem=clean.Split('.')[0].TrimEnd();
  if(new[]{"CON","PRN","AUX","NUL","CONIN$","CONOUT$"}.Contains(stem,StringComparer.OrdinalIgnoreCase)||stem.Length==4&&(stem.StartsWith("COM",StringComparison.OrdinalIgnoreCase)||stem.StartsWith("LPT",StringComparison.OrdinalIgnoreCase))&&"123456789¹²³".Contains(stem[^1]))clean="Empresa "+clean;
  if(maximum<1)throw new ExportException("EXPORT_PATH_TOO_LONG");
  if(clean.Length>maximum){int end=maximum;if(char.IsHighSurrogate(clean[end-1]))end--;clean=clean[..end].TrimEnd(' ','.');}return clean;
 }
 public static string Interval(DateOnly? from,DateOnly? to)=>from.HasValue&&to.HasValue?from==to?from.Value.ToString("dd-MM-yyyy"):from.Value.ToString("dd-MM-yyyy")+" a "+to.Value.ToString("dd-MM-yyyy"):from.HasValue?"A partir de "+from.Value.ToString("dd-MM-yyyy"):to.HasValue?"Até "+to.Value.ToString("dd-MM-yyyy"):"Todo o período";
 public static string Prefix(string firm,string status,DfeAccessKey key,FiscalEnvironment environment,DateOnly? from,DateOnly? to,string? issueDate=null)
 {
  int year=key.Year,month=key.Month;if(DateOnly.TryParseExact(issueDate,"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var issued)){year=issued.Year;month=issued.Month;}
  string model=key.Model switch{DocumentModel.NFe=>"NF-e",DocumentModel.NFCe=>"NFC-e",DocumentModel.CTe=>"CT-e",DocumentModel.CTeOS=>"CT-e OS",_=>"NFCom"};
  return $"{firm}/{status}/{year:D4}/{month:D2} - {Months[month-1]}/{Interval(from,to)}/{model}/{(environment==FiscalEnvironment.Production?"Produção":"Homologação")}/";
 }
}
