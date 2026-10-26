using System.Text.Json;
using WgsDfe.Application;
namespace WgsDfe.Infrastructure;
// Interface permite só eventos enumerados + GUID. Dados externos não entram no log.
public sealed class SafeFileLog(string directory) : ITechnicalLog
{
 private readonly object gate=new();
 public void Write(TechnicalLevel level,TechnicalEvent eventCode,Guid correlationId)
 {
  var now=DateTime.UtcNow;
  var line=JsonSerializer.Serialize(new{atUtc=now,level=level.ToString(),eventCode=eventCode.ToString(),correlationId});
  lock(gate)
  {
   Directory.CreateDirectory(directory);
   File.AppendAllText(Path.Combine(directory,$"technical-{now:yyyy-MM-dd}.jsonl"),line+Environment.NewLine);
  }
 }
}
