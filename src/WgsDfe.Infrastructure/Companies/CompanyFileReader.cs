using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using WgsDfe.Application.Companies;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Companies;
internal sealed record SourceCompanyRow(int Number,string Cnpj,CompanyInput? Data,string? Error);
internal sealed record CompanyFile(string? SheetName,IReadOnlyList<SourceCompanyRow> Rows);
internal sealed record SourceCell(string Value,bool Invalid=false,bool IsNumeric=false);
internal sealed record SourceRow(int Number,IReadOnlyDictionary<int,SourceCell> Cells);
internal static class CompanyFileReader
{
 public const int MaxRows=10000;
 public static CompanyFile Read(string path,CancellationToken ct)
 {
  var info=new FileInfo(path);if(!info.Exists||info.Length>20*1024*1024)throw Error("Arquivo ausente ou maior que 20 MB.");
  try
  {
   var (sheet,rows)=Path.GetExtension(path).ToLowerInvariant() switch{".csv"=>((string?)null,Csv(path,ct)),".xlsx"=>Xlsx(path,ct),_=>throw Error("Selecione um arquivo .csv ou .xlsx.")};
   if(rows.Count==0)throw Error("O arquivo está vazio.");
   var columns=new Dictionary<int,string>();var known=new HashSet<string>();
   foreach(var cell in rows[0].Cells)
   {
    if(cell.Value.Invalid)throw Error("Cabeçalho inválido: use texto simples, sem fórmulas.");
    var name=Header(cell.Value.Value);if(name is null)continue;
    if(!known.Add(name))throw Error($"Coluna repetida no cabeçalho: {name}.");columns[cell.Key]=name;
   }
   if(!known.Contains("cnpj")||!known.Contains("razao")||!known.Contains("uf"))throw Error("O cabeçalho precisa conter CNPJ, RazaoSocial e UF. Use o modelo fornecido.");
   var result=new List<SourceCompanyRow>();
   foreach(var row in rows.Skip(1))
   {
    ct.ThrowIfCancellationRequested();var v=new Dictionary<string,string>();string? problem=null;
    foreach(var col in columns){var cell=row.Cells.GetValueOrDefault(col.Key)??new SourceCell("");v[col.Value]=cell.Value.Trim();if(cell.Invalid)problem="Linha contém fórmula, erro ou célula não suportada. Corrija e importe novamente.";}
    if(v.Values.All(string.IsNullOrWhiteSpace)&&problem is null)continue;var cnpj=v.GetValueOrDefault("cnpj","");
    try
    {
     var data=new CompanyInput(v.GetValueOrDefault("razao",""),v.GetValueOrDefault("fantasia"),cnpj,v.GetValueOrDefault("ie"),v.GetValueOrDefault("uf",""),v.GetValueOrDefault("municipio"),Active(v.GetValueOrDefault("ativa","")),Documents(v.GetValueOrDefault("documentos","")),v.GetValueOrDefault("pasta"),v.GetValueOrDefault("observacoes"));
     result.Add(new(row.Number,cnpj,data,problem));
    }catch(CompanyException e){result.Add(new(row.Number,cnpj,null,e.Message));}
    if(result.Count>MaxRows)throw Error("Limite de 10.000 empresas por arquivo.");
   }
   if(result.Count==0)throw Error("Nenhuma empresa encontrada após o cabeçalho.");return new(sheet,result.AsReadOnly());
  }
  catch(CompanyException){throw;}catch(OperationCanceledException){throw;}
  catch(Exception e) when(e is XmlException or InvalidDataException or FormatException or OverflowException or IOException or UnauthorizedAccessException)
  {throw Error("Não foi possível ler o arquivo. Confira o formato, as permissões e o modelo de importação.");}
 }
 private static string? Header(string input)=>new string(SearchNormalizer.Normalize(input).Where(char.IsLetterOrDigit).ToArray()) switch
 {"CNPJ"=>"cnpj","RAZAOSOCIAL" or "RAZAO"=>"razao","NOMEFANTASIA" or "FANTASIA"=>"fantasia","INSCRICAOESTADUAL" or "IE"=>"ie","UF" or "ESTADO"=>"uf","MUNICIPIO" or "CIDADE"=>"municipio","ATIVA" or "ATIVO" or "SITUACAO" or "STATUS"=>"ativa","DOCUMENTOS" or "TIPOSDEDOCUMENTOS"=>"documentos","PASTA" or "PASTADEARMAZENAMENTO"=>"pasta","OBSERVACOES" or "OBS"=>"observacoes",_=>null};
 private static bool Active(string text)=>SearchNormalizer.Normalize(text) switch
 {"" or "SIM" or "S" or "1" or "TRUE" or "ATIVA" or "ATIVO"=>true,"NAO" or "N" or "0" or "FALSE" or "INATIVA" or "INATIVO"=>false,_=>throw Error("Situação inválida. Use Sim/Não ou Ativa/Inativa.")};
 private static EnabledDocuments Documents(string text)
 {
  if(string.IsNullOrWhiteSpace(text))return EnabledDocuments.All;var result=EnabledDocuments.None;
  foreach(var part in text.Split(new[]{'|',',',';'},StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries))
   result|=new string(SearchNormalizer.Normalize(part).Where(char.IsLetterOrDigit).ToArray()) switch
   {"NFE" or "55"=>EnabledDocuments.NFe,"NFCE" or "65"=>EnabledDocuments.NFCe,"CTE" or "57"=>EnabledDocuments.CTe,"CTEOS" or "67"=>EnabledDocuments.CTeOS,"NFCOM" or "62"=>EnabledDocuments.NFCom,"TODOS"=>EnabledDocuments.All,"NENHUM"=>EnabledDocuments.None,_=>throw Error("Documento inválido. Use NFe|NFCe|CTe|CTeOS|NFCom.")};
  return result;
 }
 internal static IReadOnlyList<SourceRow> Csv(string path,CancellationToken ct,int maxRows=MaxRows)
 {
  var bytes=File.ReadAllBytes(path);string text;
  try{text=new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF');}
  catch(DecoderFallbackException){Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);text=Encoding.GetEncoding(1252).GetString(bytes);}
  var first=text.Split(new[]{'\r','\n'},2)[0];var delimiter=Delimiter(first);
  using var reader=new StringReader(text);var rows=new List<SourceRow>();var fields=new List<string>();var field=new StringBuilder();
  var quoted=false;var closed=false;var line=1;var start=1;
  void EndField(){fields.Add(field.ToString());field.Clear();closed=false;if(fields.Count>32)throw Error("Limite de 32 colunas.");}
  void EndRow(){EndField();if(fields.Any(s=>!string.IsNullOrWhiteSpace(s)))rows.Add(new(start,fields.Select((v,i)=>new KeyValuePair<int,SourceCell>(i+1,new(v))).ToDictionary()));fields.Clear();start=line+1;if(rows.Count>maxRows+1)throw Error("Limite de 10.000 linhas.");}
  while(reader.Read() is var code&&code>=0)
  {
   ct.ThrowIfCancellationRequested();var c=(char)code;
   if(quoted){if(c=='"'){if(reader.Peek()=='"'){reader.Read();field.Append('"');}else{quoted=false;closed=true;}}else{field.Append(c);if(c=='\n')line++;}}
   else if(c==delimiter)EndField();
   else if(c is '\r' or '\n'){if(c=='\r'&&reader.Peek()=='\n')reader.Read();EndRow();line++;}
   else if(closed){if(c is not (' ' or '\t'))throw Error("CSV inválido: texto após aspas.");}
   else if(c=='"'){if(field.Length!=0)throw Error("CSV inválido: aspas no meio de campo.");quoted=true;}
   else field.Append(c);
   if(field.Length>8000)throw Error("Campo maior que 8.000 caracteres.");
  }
  if(quoted)throw Error("CSV inválido: aspas não fechadas.");if(field.Length>0||fields.Count>0||closed)EndRow();return rows;
 }
 private static char Delimiter(string text)
 {
  var quoted=false;var commas=0;var semicolons=0;
  for(var i=0;i<text.Length;i++)
  {
   if(text[i]=='"'){if(quoted&&i+1<text.Length&&text[i+1]=='"'){i++;continue;}quoted=!quoted;}
   else if(!quoted){if(text[i]==',')commas++;if(text[i]==';')semicolons++;}
  }
  return semicolons>=commas?';':',';
 }
 internal static (string? Sheet,IReadOnlyList<SourceRow> Rows) Xlsx(string path,CancellationToken ct,int maxRows=MaxRows)
 {
  using var zip=ZipFile.OpenRead(path);
  if(zip.Entries.Count>2000||zip.Entries.Sum(e=>e.Length)>64*1024*1024||zip.Entries.Select(e=>e.FullName).Distinct().Count()!=zip.Entries.Count)throw Error("Planilha excede os limites seguros ou contém entradas repetidas.");
  XElement Part(string name)
  {
   ct.ThrowIfCancellationRequested();var entry=zip.GetEntry(name)??throw Error("Estrutura XLSX incompleta.");if(entry.Length>20*1024*1024)throw Error("Parte da planilha excede 20 MB.");
   using var stream=entry.Open();using var xml=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=20*1024*1024});return XElement.Load(xml);
  }
  var workbook=Part("xl/workbook.xml");
  if(workbook.Name.LocalName!="workbook"||workbook.Name.NamespaceName is not ("http://schemas.openxmlformats.org/spreadsheetml/2006/main" or "http://purl.oclc.org/ooxml/spreadsheetml/main"))throw Error("Formato XLSX não reconhecido.");
  var ns=workbook.Name.Namespace;
  var sheet=workbook.Element(ns+"sheets")?.Elements(ns+"sheet").FirstOrDefault(s=>s.Attribute("state")?.Value is not ("hidden" or "veryHidden"))??throw Error("Planilha sem aba visível.");
  var id=sheet.Attributes().FirstOrDefault(a=>a.Name.LocalName=="id")?.Value??throw Error("Aba sem referência.");
  var relations=Part("xl/_rels/workbook.xml.rels");var matches=relations.Elements().Where(x=>x.Attribute("Id")?.Value==id).ToArray();if(matches.Length!=1)throw Error("Referência da aba inválida.");
  var relation=matches[0];if(relation.Attribute("TargetMode")?.Value=="External")throw Error("Referência externa não suportada.");var target=relation.Attribute("Target")?.Value??throw Error("Aba sem caminho.");
  if(target.Contains('\\')||(!target.StartsWith('/')&&Uri.TryCreate(target,UriKind.Absolute,out _)))throw Error("Caminho de aba inválido.");
  var parts=new List<string>();foreach(var part in (target.StartsWith('/')?target:"xl/"+target).Split('/',StringSplitOptions.RemoveEmptyEntries))
  {if(part==".")continue;if(part==".."){if(parts.Count==0)throw Error("Caminho de aba inválido.");parts.RemoveAt(parts.Count-1);}else parts.Add(part);}
  var name=string.Join('/',parts);if(!name.StartsWith("xl/",StringComparison.Ordinal))throw Error("Caminho fora da planilha.");
  var strings=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml") is not null)
   foreach(var item in Part("xl/sharedStrings.xml").Elements().Where(x=>x.Name.LocalName=="si")){var value=string.Concat(item.Descendants().Where(x=>x.Name.LocalName=="t").Select(x=>x.Value));if(value.Length>8000||strings.Count>=200000)throw Error("Textos da planilha excedem o limite.");strings.Add(value);}
  var worksheet=Part(name);if(worksheet.Name.LocalName!="worksheet"||worksheet.Name.Namespace!=ns)throw Error("Aba inválida.");
  var rows=new List<SourceRow>();var numbers=new HashSet<int>();
  foreach(var row in worksheet.Element(ns+"sheetData")?.Elements(ns+"row")??Enumerable.Empty<XElement>())
  {
   ct.ThrowIfCancellationRequested();var number=int.Parse(row.Attribute("r")?.Value??throw Error("Linha sem número."),CultureInfo.InvariantCulture);if(number<1||!numbers.Add(number))throw Error("Numeração de linhas inválida.");var cells=new Dictionary<int,SourceCell>();
   foreach(var cell in row.Elements(ns+"c"))
   {
    var reference=cell.Attribute("r")?.Value??throw Error("Célula sem referência.");var column=0;var i=0;
    while(i<reference.Length&&reference[i] is >= 'A' and <= 'Z'){column=checked(column*26+reference[i]-'A'+1);i++;}
    if(column<1||column>32||!int.TryParse(reference.AsSpan(i),out var actual)||actual!=number)throw Error("Referência de célula inválida ou mais de 32 colunas.");
    var type=cell.Attribute("t")?.Value;var raw=cell.Element(ns+"v")?.Value??"";var invalid=cell.Element(ns+"f") is not null;var value=raw;
    if(type=="s"){if(!int.TryParse(raw,out var index)||index<0||index>=strings.Count)throw Error("Texto compartilhado inválido.");value=strings[index];}
    else if(type=="inlineStr")value=string.Concat(cell.Element(ns+"is")?.Descendants(ns+"t").Select(x=>x.Value)??Enumerable.Empty<string>());
    else if(type=="b")value=raw=="1"?"Sim":raw=="0"?"Não":"Inválido";
    else if(type is "e" or "d")invalid=true;
    else if(type is null or "n"){if(raw!=""&&decimal.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out var n)&&n==decimal.Truncate(n))value=n.ToString("0",CultureInfo.InvariantCulture);else if(raw!="")invalid=true;}
    else if(type!="str")invalid=true;
    if(value.Length>8000||!cells.TryAdd(column,new(value,invalid,type is null or "n")))throw Error("Campo excessivo ou célula repetida.");
   }
   if(cells.Values.Any(c=>c.Value!=""||c.Invalid))rows.Add(new(number,cells));if(rows.Count>maxRows+1)throw Error("Limite de 10.000 linhas.");
  }
  rows.Sort((a,b)=>a.Number.CompareTo(b.Number));return(sheet.Attribute("name")?.Value,rows);
 }
 private static CompanyException Error(string message)=>new(CompanyError.InvalidData,message);
}
