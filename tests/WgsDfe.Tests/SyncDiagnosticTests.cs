using System.Text.Json;
using WgsDfe.Application.Nfe;
using WgsDfe.Infrastructure.Nfe;
using Xunit;
namespace WgsDfe.Tests;
public sealed class SyncDiagnosticTests
{
 [Fact]public async Task ConcurrentPreferenceUpdatesPreservePeriodAndExportSettings()
 {
  string folder=Path.Combine(Path.GetTempPath(),"wgs-preferences-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
  try
  {
   await File.WriteAllTextAsync(Path.Combine(folder,"preferences.json"),"{\"ExportDirectory\":\"antigo\",\"RetentionDays\":30}");Assert.False(WgsDfe.Infrastructure.UserPreferences.Load(folder).HasQueryPeriod);
   Assert.Equal(1,WgsDfe.Infrastructure.UserPreferences.Load(folder).InterfaceZoom);
   WgsDfe.Infrastructure.UserPreferences.Update(folder,p=>p with{InterfaceZoom=1.3});
   await Task.WhenAll(Enumerable.Range(0,20).Select(i=>Task.Run(()=>WgsDfe.Infrastructure.UserPreferences.Update(folder,p=>i%2==0?p with{QueryFrom=new DateTime(2026,9,1),QueryTo=new DateTime(2026,9,30),HasQueryPeriod=true}:p with{ExportDirectory="destino atualizado",Zip=true}))));
   var result=WgsDfe.Infrastructure.UserPreferences.Load(folder);Assert.Equal(1.3,result.InterfaceZoom);Assert.Equal(new DateTime(2026,9,1),result.QueryFrom);Assert.Equal(new DateTime(2026,9,30),result.QueryTo);Assert.True(result.HasQueryPeriod);Assert.Equal("destino atualizado",result.ExportDirectory);Assert.True(result.Zip);Assert.Equal(30,result.RetentionDays);
  }
  finally{Directory.Delete(folder,true);}
 }
 [Fact]public void StructuredDiagnosticContainsOnlyWhitelistedMetadata()
 {
  string folder=Path.Combine(Path.GetTempPath(),"wgs-diagnostic-"+Guid.NewGuid().ToString("N"));
  try
  {
   Guid correlation=Guid.NewGuid();new SyncDiagnosticLog(folder).Write(new(correlation,NfeSources.SantaCatarinaNfce,"A19C88F0",115,"5700","5750",118,321,"NFE_PAGE_COMMITTED",46,0));
   using var json=JsonDocument.Parse(File.ReadAllText(Assert.Single(Directory.GetFiles(folder))));var entry=json.RootElement.GetProperty("entry");Assert.Equal(correlation,entry.GetProperty("CorrelationId").GetGuid());Assert.Equal(115,entry.GetProperty("Batch").GetInt32());Assert.Equal("5700",entry.GetProperty("PreviousNsu").GetString());Assert.Equal("5750",entry.GetProperty("NextNsu").GetString());Assert.Equal(11,entry.EnumerateObject().Count());
  }
  finally{if(Directory.Exists(folder))Directory.Delete(folder,true);}
 }
}
