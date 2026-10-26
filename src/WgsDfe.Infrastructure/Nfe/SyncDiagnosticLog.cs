using System.Text.Json;
using WgsDfe.Application.Nfe;
namespace WgsDfe.Infrastructure.Nfe;
public sealed class SyncDiagnosticLog(string directory):ISyncDiagnostic
{
 private readonly object gate=new();
 public void Write(SyncDiagnostic entry)
 {
  // Only typed, internally generated metadata. Never serialize requests, XML or exceptions.
  try{lock(gate){Directory.CreateDirectory(directory);File.AppendAllText(Path.Combine(directory,$"sync-{DateTime.UtcNow:yyyy-MM-dd}.jsonl"),JsonSerializer.Serialize(new{atUtc=DateTime.UtcNow,entry})+Environment.NewLine);}}
  catch(IOException){ }catch(UnauthorizedAccessException){ }
 }
}
