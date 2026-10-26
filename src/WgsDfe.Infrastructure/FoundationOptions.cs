using System.Text.Json;
namespace WgsDfe.Infrastructure;
public sealed class FoundationOptions
{
 public ApplicationOptions Application { get; set; } = new();
 public ProviderOptions Providers { get; set; } = new();
 [System.Text.Json.Serialization.JsonIgnore] public string LockFileName { get; set; } = "instance.lock";
 public static FoundationOptions Load(string path)
 {
  var result=JsonSerializer.Deserialize<FoundationOptions>(File.ReadAllText(path),
   new JsonSerializerOptions{PropertyNameCaseInsensitive=true,UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow})
   ?? throw new InvalidOperationException("Configuração ausente.");
  result.Validate(); return result;
 }
 public void Validate()
 {
  if(Application.OrganizationId==Guid.Empty || string.IsNullOrWhiteSpace(Application.OrganizationName))
   throw new InvalidOperationException("Configure uma organização válida.");
  if(Application.DataDirectory is { } directory && (!Path.IsPathFullyQualified(directory) || string.IsNullOrWhiteSpace(directory)))
   throw new InvalidOperationException("Pasta de dados deve ter caminho absoluto.");
 }
 public string ResolveDataDirectory() => Path.GetFullPath(Application.DataDirectory ??
  Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WgsDfeManager",Application.OrganizationId.ToString("N")));
}
public sealed class ApplicationOptions
{
 public Guid OrganizationId { get; set; }
 public string OrganizationName { get; set; } = "WGS";
 public string? DataDirectory { get; set; }
}
public sealed class ProviderOptions
{
 private int enabled;internal event Action? RequestsDisabled;
 public bool EnableExternalRequests{get=>Volatile.Read(ref enabled)==1;set{Interlocked.Exchange(ref enabled,value?1:0);if(!value)RequestsDisabled?.Invoke();}}
}
