using System.Text.Json;
using WgsDfe.Domain;
using WgsDfe.Application.Parsing;
using WgsDfe.Infrastructure.Parsing;
using WgsDfe.Infrastructure;
if(args.Length==2&&args[0] is "key" or "parse")
{
 try
 {
  object result=args[0]=="key"?DfeAccessKey.Parse(args[1]):await new DfeParser().ParseFileAsync(args[1]);
  Console.WriteLine(JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true}));return 0;
 }
 catch(AccessKeyException e){Console.Error.WriteLine($"{e.Code}: {e.Message}");return 1;}
 catch(DfeParseException e){Console.Error.WriteLine($"{e.Code}: {e.Message}");return 1;}
 catch{Console.Error.WriteLine("Falha ao inspecionar DF-e.");return 1;}
}
if(args.Length!=2 || args[0]!="init")
{
 Console.Error.WriteLine("Uso: WgsDfe.Tools init <appsettings.json> | key <chave> | parse <arquivo.xml>"); return 2;
}
try
{
 await using var runtime=new FoundationRuntime(FoundationOptions.Load(args[1]));
 var status=await runtime.InitializeAsync();
 Console.WriteLine($"Fundação pronta. Empresas: {status.Companies}; XMLs: {status.Documents}; migrations: {status.Migrations}.");
 return 0;
}
catch
{
 Console.Error.WriteLine("Falha ao inicializar. Verifique configuração, acesso à pasta e instância aberta."); return 1;
}
