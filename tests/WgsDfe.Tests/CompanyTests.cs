using System.Xml.Linq;
using System.IO.Compression;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using WgsDfe.Application.Companies;
using WgsDfe.Database;
using WgsDfe.Domain;
using WgsDfe.Infrastructure.Companies;
using Xunit;
namespace WgsDfe.Tests;
public sealed class CompanyTests
{
 [Theory][InlineData("xlsx")][InlineData("csv")]
 public async Task ShippedTemplateAcceptsFilledCompanyAndPreservesLeadingZeros(string extension)
 {
  await using var f=await Fixture.Create();var path=f.File("filled."+extension);System.IO.File.Copy(Path.Combine(AppContext.BaseDirectory,"Modelos","Modelo_Importacao_Empresas."+extension),path);
  if(extension=="csv")await System.IO.File.AppendAllTextAsync(path,"\n00000000000191;Empresa modelo;;;SC;;Sim;Todos;;\n");
  else
  {
   using var zip=new ZipArchive(System.IO.File.Open(path,FileMode.Open),ZipArchiveMode.Update);
   var entry=zip.GetEntry("xl/worksheets/sheet1.xml")!;System.Xml.Linq.XDocument doc;using(var stream=entry.Open())doc=System.Xml.Linq.XDocument.Load(stream);
   System.Xml.Linq.XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";var data=doc.Root!.Element(ns+"sheetData")!;
   data.Elements(ns+"row").Where(x=>(string?)x.Attribute("r")=="2").Remove();
   var row=new System.Xml.Linq.XElement(ns+"row",new System.Xml.Linq.XAttribute("r",2));
   foreach(var pair in new[]{("A2","00000000000191"),("B2","Empresa modelo"),("E2","SC"),("G2","Sim"),("H2","Todos")})row.Add(new System.Xml.Linq.XElement(ns+"c",new System.Xml.Linq.XAttribute("r",pair.Item1),new System.Xml.Linq.XAttribute("t","inlineStr"),new System.Xml.Linq.XElement(ns+"is",new System.Xml.Linq.XElement(ns+"t",pair.Item2))));
   data.Elements(ns+"row").First().AddAfterSelf(row);entry.Delete();using var output=zip.CreateEntry("xl/worksheets/sheet1.xml").Open();doc.Save(output);
  }
  var plan=await f.Import.AnalyzeAsync(path);Assert.Equal(1,plan.Ready);Assert.Equal("00000000000191",Assert.Single(plan.Rows).Data!.Cnpj);Assert.Equal(1,(await f.Import.ImportAsync(plan)).Imported);
 }
 [Theory]
 [InlineData("12.345.678/0001-95","12345678000195")]
 [InlineData("12.ABC.345/01DE-35","12ABC34501DE35")]
 [InlineData("12abc34501de35","12ABC34501DE35")]
 [InlineData(" 12345678000195 ","12345678000195")]
 [InlineData("00000000000191","00000000000191")]
 public void OfficialCnpjExamples_Validate(string input,string expected)
 {Assert.True(Cnpj.TryNormalize(input,out var actual));Assert.Equal(expected,actual);}
 [Theory]
 [InlineData("00000000000000")][InlineData("12345678000194")][InlineData("12ABC34501DE34")]
 [InlineData("1234567800019")][InlineData("12ABC34501DE3A")][InlineData("12ÁBC34501DE35")]
 [InlineData("12 ABC34501DE35")][InlineData("12.ABC.345/01DE--35")][InlineData("١٢٣٤٥٦٧٨٠٠٠١٩٥")]
 public void InvalidCnpj_IsRejected(string value)=>Assert.False(Cnpj.TryNormalize(value,out _));
 [Fact] public async Task Crud_Normalizes_Duplicates_Searches_AndAudits()
 {
  await using var f=await Fixture.Create();
  var row=await f.Companies.CreateAsync(Input() with{LegalName=" Ação Contábil ",TradeName="WGS",City="São José",Cnpj="12.345.678/0001-95"},Guid.NewGuid());
  Assert.Equal("12345678000195",row.Data.Cnpj);Assert.Equal("Ação Contábil",row.LegalName);
  var duplicate=await Assert.ThrowsAsync<CompanyException>(()=>f.Companies.CreateAsync(Input(),Guid.NewGuid()));Assert.Equal(CompanyError.DuplicateCnpj,duplicate.Code);
  Assert.Single((await f.Companies.SearchAsync(new("acao sao jose"))).Items);
  Assert.Single((await f.Companies.SearchAsync(new("12.345.678/0001-95"))).Items);
  Assert.Empty((await f.Companies.SearchAsync(new("%"))).Items);
  var updated=await f.Companies.UpdateAsync(row.Id,row.Revision,row.Data with{LegalName="Empresa Atualizada"},Guid.NewGuid());
  await f.Companies.SetActiveAsync(updated.Id,updated.Revision,false,Guid.NewGuid());
  Assert.Empty((await f.Companies.SearchAsync(new(IsActive:true))).Items);
  var inactive=await f.Companies.FindAsync(row.Id);Assert.False(inactive!.Data.IsActive);
  await f.Companies.DeleteAsync(inactive.Id,inactive.Revision,Guid.NewGuid());
  Assert.Equal(0,(await f.Companies.SearchAsync(new())).Total);Assert.Equal(4,await f.Db.AuditLogs.CountAsync());
 }
 [Fact] public async Task StaleRevision_AndWrongOrganization_AreRejected()
 {
  await using var f=await Fixture.Create();var row=await f.Companies.CreateAsync(Input(),Guid.NewGuid());
  await f.Companies.UpdateAsync(row.Id,row.Revision,row.Data with{City="Blumenau"},Guid.NewGuid());
  Assert.Equal(CompanyError.Conflict,(await Assert.ThrowsAsync<CompanyException>(()=>f.Companies.UpdateAsync(row.Id,row.Revision,row.Data,Guid.NewGuid()))).Code);
  await using var other=DatabaseFactory.Create(f.DbPath,Guid.NewGuid());var otherService=new CompanyService(other);
  Assert.Null(await otherService.FindAsync(row.Id));
  Assert.Equal(CompanyError.NotFound,(await Assert.ThrowsAsync<CompanyException>(()=>otherService.DeleteAsync(row.Id,row.Revision,Guid.NewGuid()))).Code);
 }
 [Fact] public async Task DeleteHidesInactiveCompanyAndPreservesCursorThenRecreateRecoversIt()
 {
  await using var f=await Fixture.Create();var row=await f.Companies.CreateAsync(Input(),Guid.NewGuid());
  f.Db.DocumentSyncStates.Add(new(){OrganizationId=f.Office,CompanyId=row.Id,ProviderCode="TEST",Model=DocumentModel.NFe,LastNsu="1234",NextAllowedAtUtc=DateTime.UtcNow.AddHours(12)});await f.Db.SaveChangesAsync();
  Assert.Equal(CompanyError.HasDependencies,(await Assert.ThrowsAsync<CompanyException>(()=>f.Companies.UpdateAsync(row.Id,row.Revision,row.Data with{Cnpj="12ABC34501DE35"},Guid.NewGuid()))).Code);
  await f.Companies.SetActiveAsync(row.Id,row.Revision,false,Guid.NewGuid());
  var inactive=(await f.Companies.FindAsync(row.Id))!;
  await f.Companies.DeleteAsync(inactive.Id,inactive.Revision,Guid.NewGuid());
  Assert.Empty((await f.Companies.SearchAsync(new(All:true))).Items);
  Assert.Null(await f.Companies.FindAsync(row.Id));
  Assert.Equal("1234",(await f.Db.DocumentSyncStates.SingleAsync()).LastNsu);
  Assert.True((await f.Db.Companies.IgnoreQueryFilters().SingleAsync()).IsDeleted);
  var restored=await f.Companies.CreateAsync(Input(),Guid.NewGuid());Assert.Equal(row.Id,restored.Id);
  Assert.Single((await f.Companies.SearchAsync(new())).Items);
  Assert.True((await f.Db.DocumentSyncStates.SingleAsync()).NextAllowedAtUtc>DateTime.UtcNow);
 }
 [Fact] public async Task FullListContainsMoreThanFiftyCompaniesAndRetainsSearchFilter()
 {
  await using var f=await Fixture.Create();
  for(int i=0;i<75;i++)f.Db.Companies.Add(new(){OrganizationId=f.Office,Cnpj=(10000000000000L+i).ToString(),LegalName=$"Empresa {i:D3}",SearchText=$"EMPRESA {i:D3}",State="SC",IsActive=i%2==0});
  await f.Db.SaveChangesAsync();
  Assert.Equal(50,(await f.Companies.SearchAsync(new())).Items.Count);
  var all=await f.Companies.SearchAsync(new(All:true));Assert.Equal(75,all.Items.Count);Assert.Equal(75,all.Total);
  Assert.Equal(38,(await f.Companies.SearchAsync(new(IsActive:true,All:true))).Items.Count);
 }
 [Theory]
 [InlineData("../empresa")][InlineData("C:\\clientes")][InlineData("/clientes")][InlineData("clientes//teste")][InlineData("clientes/teste.")][InlineData("clientes/CON")][InlineData("AUX.txt")]
 public void UnsafeStoragePath_IsRejected(string path)=>Assert.Throws<CompanyException>(()=>CompanyValidation.Normalize(Input() with{StorageRelativePath=path}));
 [Fact] public void ValidationRejectsInvalidUfAndLongName()
 {Assert.Throws<CompanyException>(()=>CompanyValidation.Normalize(Input() with{State="XX"}));Assert.Throws<CompanyException>(()=>CompanyValidation.Normalize(Input() with{LegalName=new string('a',301)}));}
 [Fact] public async Task CsvPreview_Import_Deduplicate_AndCheckpoint()
 {
  await using var f=await Fixture.Create();var path=f.File("empresas.csv");
  await File.WriteAllTextAsync(path,"CNPJ;Razão Social;UF;Município;Ativa;Documentos;Observações\n12.345.678/0001-95;\"Empresa; com separador\";SC;Joinville;Sim;NFe|CTe;\"Duas linhas\nsegunda linha\"\n12345678000195;Duplicada;SC;;;;\n12ABC34501DE35;Alfanumérica;SC;Blumenau;Não;NFCe|NFCom;\n12345678000194;Inválida;SC;;;;\n",new UTF8Encoding(true));
  var plan=await f.Import.AnalyzeAsync(path);Assert.Equal(4,plan.Rows.Count);Assert.Equal(2,plan.Ready);Assert.Equal(1,plan.Invalid);Assert.Equal(1,plan.Duplicates);
  Assert.Equal(2,plan.Rows[0].RowNumber);Assert.Contains("segunda linha",plan.Rows[0].Data!.Notes);Assert.Equal(0,await f.Db.Companies.CountAsync());
  var result=await f.Import.ImportAsync(plan);Assert.Equal(2,result.Imported);Assert.Equal(1,result.Duplicates);Assert.Equal(1,result.Invalid);
  Assert.Equal(4,(await f.Db.ProcessingJobs.SingleAsync()).ProcessedItems);Assert.Equal(4,await f.Db.ProcessingJobItems.CountAsync());
  var repeated=await f.Import.AnalyzeAsync(path);Assert.Equal(0,repeated.Ready);Assert.Equal(3,repeated.Duplicates);
  Assert.False((await f.Companies.SearchAsync(new("Alfanumerica"))).Items.Single().Data.IsActive);
 }
 [Fact] public async Task CsvDelimiterIgnoresSeparatorsInsideQuotedHeaders()
 {
  await using var f=await Fixture.Create();var path=f.File("quoted.csv");
  await File.WriteAllTextAsync(path,"CNPJ,RazaoSocial,UF,\"Extra;;;;;;;\"\n12345678000195,Empresa,SC,Ignorado");
  Assert.Equal(1,(await f.Import.AnalyzeAsync(path)).Ready);
 }
 [Fact] public async Task LegacyLowercaseCnpj_IsStillDuplicate()
 {
  await using var f=await Fixture.Create();f.Db.Companies.Add(new(){OrganizationId=f.Office,Cnpj="12abc34501de35",LegalName="Legado",State="SC"});await f.Db.SaveChangesAsync();
  Assert.Equal(CompanyError.DuplicateCnpj,(await Assert.ThrowsAsync<CompanyException>(()=>f.Companies.CreateAsync(Input() with{Cnpj="12ABC34501DE35"},Guid.NewGuid()))).Code);
  var path=f.File("legacy.csv");await File.WriteAllTextAsync(path,"CNPJ;RazaoSocial;UF\n12ABC34501DE35;Não sobrescrever;SC");
  Assert.Equal(1,(await f.Import.AnalyzeAsync(path)).Duplicates);
 }
 [Fact] public async Task Import_RechecksExistingCnpj_AfterPreview()
 {
  await using var f=await Fixture.Create();var path=f.File("companies.csv");await File.WriteAllTextAsync(path,"CNPJ,RAZAOSOCIAL,UF\n12345678000195,Novo,SC");
  var plan=await f.Import.AnalyzeAsync(path);await f.Companies.CreateAsync(Input() with{LegalName="Preservar"},Guid.NewGuid());
  var result=await f.Import.ImportAsync(plan);Assert.Equal(0,result.Imported);Assert.Equal(1,result.Duplicates);Assert.Equal("Preservar",(await f.Db.Companies.SingleAsync()).LegalName);
 }
 [Fact] public async Task CancelledImport_PreservesFirstCommittedCompany_AndCanBeReanalyzed()
 {
  await using var f=await Fixture.Create();var path=f.File("cancel.csv");await File.WriteAllTextAsync(path,"CNPJ;RazaoSocial;UF\n12345678000195;Primeira;SC\n12ABC34501DE35;Segunda;SC");
  var plan=await f.Import.AnalyzeAsync(path);using var cancellation=new CancellationTokenSource();
  var result=await f.Import.ImportAsync(plan,new InlineProgress(_=>cancellation.Cancel()),cancellation.Token);
  Assert.True(result.Cancelled);Assert.Equal(1,result.Imported);Assert.Equal(1,result.Rows.Count(x=>x.Status==ImportRowStatus.Cancelled));
  Assert.Equal(JobStatus.Cancelled,(await f.Db.ProcessingJobs.SingleAsync()).Status);
  var retry=await f.Import.AnalyzeAsync(path);Assert.Equal(1,retry.Ready);Assert.Equal(1,retry.Duplicates);
  var completed=await f.Import.ImportAsync(retry);Assert.Equal(1,completed.Imported);Assert.Equal(2,await f.Db.Companies.CountAsync());
 }
 [Fact] public async Task Csv1252_Quotes_AndUnknownColumns_AreSupported()
 {
  await using var f=await Fixture.Create();Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
  var path=f.File("ansi.csv");await File.WriteAllTextAsync(path,"CNPJ;RazaoSocial;UF;Extra\n12345678000195;\"Ação \"\"Teste\"\"\";SC;Ignorado",Encoding.GetEncoding(1252));
  var plan=await f.Import.AnalyzeAsync(path);Assert.Equal("Ação \"Teste\"",plan.Rows.Single().Data!.LegalName);Assert.Equal(1,plan.Ready);
 }
 [Theory]
 [InlineData("CNPJ;RazaoSocial;UF\n12345678000195;\"Não fechada;SC")]
 [InlineData("CNPJ;CNPJ;UF\n12345678000195;Empresa;SC")]
 [InlineData("CNPJ;RazaoSocial\n12345678000195;Empresa")]
 public async Task BadCsv_IsRejectedWithoutWrites(string content)
 {await using var f=await Fixture.Create();var path=f.File("invalid.csv");await File.WriteAllTextAsync(path,content);await Assert.ThrowsAsync<CompanyException>(()=>f.Import.AnalyzeAsync(path));Assert.Equal(0,await f.Db.Companies.CountAsync());}
 [Fact] public async Task XlsxReadsSharedStrings_NumericCnpj_AndFirstVisibleSheet()
 {
  await using var f=await Fixture.Create();var path=f.File("empresas.xlsx");Xlsx(path,"<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"inlineStr\"><is><t>RazaoSocial</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>UF</t></is></c></row><row r=\"2\"><c r=\"A2\"><v>12345678000195</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>Empresa</t></is></c><c r=\"C2\" t=\"s\"><v>1</v></c></row>");
  var plan=await f.Import.AnalyzeAsync(path);Assert.Equal("Empresas",plan.SheetName);Assert.Equal(1,plan.Ready);Assert.Equal(1,(await f.Import.ImportAsync(plan)).Imported);
 }
 [Fact] public async Task XlsxFormula_IsRejectedEvenWithValidCachedCnpj()
 {
  await using var f=await Fixture.Create();var path=f.File("formula.xlsx");Xlsx(path,Header()+"<row r=\"2\"><c r=\"A2\"><f>12345678000195</f><v>12345678000195</v></c><c r=\"B2\" t=\"inlineStr\"><is><t>Empresa</t></is></c><c r=\"C2\" t=\"inlineStr\"><is><t>SC</t></is></c></row>");
  var plan=await f.Import.AnalyzeAsync(path);Assert.Equal(1,plan.Invalid);Assert.Equal(0,plan.Ready);
 }
 [Fact] public async Task XlsxExternalRelationshipAndDtd_AreRejected()
 {
  await using var f=await Fixture.Create();var path=f.File("external.xlsx");Xlsx(path,Header(),true);await Assert.ThrowsAsync<CompanyException>(()=>f.Import.AnalyzeAsync(path));
  path=f.File("dtd.xlsx");Xlsx(path,Header(),false,true);await Assert.ThrowsAsync<CompanyException>(()=>f.Import.AnalyzeAsync(path));
 }
 [Fact] public void CsvReport_ProtectsAgainstExcelFormulaInjection()
 {
  var path=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".csv");
  try{CompanyImportReport.SaveCsv(path,new[]{new ImportRow(2,Input() with{LegalName="=HYPERLINK(\"bad\")"},"",ImportRowStatus.Invalid,"@erro")});var csv=File.ReadAllText(path);Assert.Contains("'=HYPERLINK",csv);Assert.Contains("'@erro",csv);}finally{File.Delete(path);}
 }
 [Fact] public async Task UpgradeFromPhase1_PreservesAndMakesOldCompanySearchable()
 {
  var dir=Path.Combine(Path.GetTempPath(),"wgs-upgrade-"+Guid.NewGuid());Directory.CreateDirectory(dir);var path=Path.Combine(dir,"upgrade.db");var office=Guid.NewGuid();
  try
  {
   await using(var db=DatabaseFactory.Create(path,office))
   {
    var migrator=db.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>();
    await migrator.MigrateAsync(db.Database.GetMigrations().First());
    await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Organizations (Id, Name, CreatedAtUtc) VALUES ({office}, {"Teste"}, {DateTime.UtcNow})");
    var id=Guid.NewGuid();await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Companies (Id, OrganizationId, CreatedAtUtc, UpdatedAtUtc, Revision, LegalName, Cnpj, State, IsActive, EnabledDocuments, City) VALUES ({id}, {office}, {DateTime.UtcNow}, {DateTime.UtcNow}, {1}, {"Ação Antiga"}, {"12345678000195"}, {"SC"}, {true}, {31}, {"São José"})");
    await db.Database.MigrateAsync();Assert.Single((await new CompanyService(db).SearchAsync(new("acao sao jose"))).Items);Assert.Equal(9,(await db.Database.GetAppliedMigrationsAsync()).Count());
   }
  }finally{Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
 }
 [Fact] public void ExistingAuditEnumOrdinalsRemainStable()
 {Assert.Equal(3,(int)AuditAction.CertificateReplaced);Assert.Equal(7,(int)AuditAction.ExportCompleted);}
 private static CompanyInput Input()=>new("Empresa teste",null,"12345678000195",null,"SC","Joinville",true,EnabledDocuments.All,null,null);
 private sealed class InlineProgress(Action<ImportProgress> action):IProgress<ImportProgress>{public void Report(ImportProgress value)=>action(value);}
 private static string Header()=>"<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>CNPJ</t></is></c><c r=\"B1\" t=\"inlineStr\"><is><t>RazaoSocial</t></is></c><c r=\"C1\" t=\"inlineStr\"><is><t>UF</t></is></c></row>";
 private static void Xlsx(string path,string rows,bool external=false,bool dtd=false)
 {
  using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
  void Part(string name,string value){using var writer=new StreamWriter(zip.CreateEntry(name).Open());writer.Write(value);}
  Part("xl/workbook.xml",(dtd?"<!DOCTYPE workbook [<!ENTITY x SYSTEM 'file:///etc/passwd'>]>":"")+"<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Oculta\" state=\"hidden\" sheetId=\"2\" r:id=\"rId2\"/><sheet name=\"Empresas\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
  Part("xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" "+(external?"TargetMode=\"External\" ":"")+"Target=\"worksheets/sheet1.xml\"/></Relationships>");
  Part("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData>"+rows+"</sheetData></worksheet>");
  Part("xl/sharedStrings.xml","<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>CNPJ</t></si><si><t>SC</t></si></sst>");
 }
 private sealed class Fixture:IAsyncDisposable
 {
  public Guid Office{get;}=Guid.NewGuid();public string DirectoryPath{get;}=Path.Combine(Path.GetTempPath(),"wgs-company-"+Guid.NewGuid());
  public string DbPath=>Path.Combine(DirectoryPath,"test.db");public WgsDbContext Db{get;private set;}=null!;
  public CompanyService Companies{get;private set;}=null!;public CompanyImportService Import{get;private set;}=null!;
  public static async Task<Fixture> Create(){var f=new Fixture();Directory.CreateDirectory(f.DirectoryPath);f.Db=DatabaseFactory.Create(f.DbPath,f.Office);await f.Db.Database.MigrateAsync();f.Db.Organizations.Add(new(){Id=f.Office,Name="Teste"});await f.Db.SaveChangesAsync();f.Companies=new(f.Db);f.Import=new(f.Db,f.Companies);return f;}
  public string File(string name)=>Path.Combine(DirectoryPath,name);
  public async ValueTask DisposeAsync(){await Db.DisposeAsync();Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();Directory.Delete(DirectoryPath,true);}
 }
}
