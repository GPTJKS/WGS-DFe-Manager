using System.Text.Json;
namespace WgsDfe.Infrastructure;
public sealed record UserPreferences(string ExportDirectory="",bool Zip=false,int RetentionDays=0,int QueryType=0,int QuerySource=1,Guid[]? QueryCompanies=null,DateTime? QueryFrom=null,DateTime? QueryTo=null,bool HasQueryPeriod=false,string? LicensedOrganization=null,string? LicensedCnpj=null,double InterfaceZoom=1)
{
 private static readonly object updateGate=new();
 public static void Update(string dataDirectory,Func<UserPreferences,UserPreferences> update)
 {lock(updateGate)update(Load(dataDirectory)).Save(dataDirectory);}
 public static UserPreferences Load(string dataDirectory)
 {
  try{return JsonSerializer.Deserialize<UserPreferences>(File.ReadAllText(Path.Combine(dataDirectory,"preferences.json")))??new();}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or JsonException){return new();}
 }
 public void Save(string dataDirectory)
 {
  if(RetentionDays is not (0 or 30 or 60 or 90))throw new InvalidOperationException("Retenção inválida.");
  string path=Path.Combine(dataDirectory,"preferences.json"),temp=path+"."+Guid.NewGuid().ToString("N")+".partial";
  try{File.WriteAllText(temp,JsonSerializer.Serialize(this));File.Move(temp,path,true);}
  finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
