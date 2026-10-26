using System.Net;
using System.Text.Json;
using WgsDfe.Application.Companies;
using WgsDfe.Domain;
namespace WgsDfe.Infrastructure.Companies;
public sealed record PublicCompany(string Cnpj,string LegalName,string? TradeName,string State,string? City,string Source="BrasilAPI",bool Cached=false);
public sealed class CompanyLookup(HttpClient client,TimeProvider? clock=null)
{
 private readonly TimeProvider time=clock??TimeProvider.System;
 private readonly SemaphoreSlim gate=new(1,1);
 private readonly Dictionary<string,(PublicCompany Value,DateTimeOffset Until)> cache=new();
 private readonly Dictionary<string,DateTimeOffset> blocked=new();
 public async Task<PublicCompany> FindAsync(string cnpj,CancellationToken ct=default)
 {
  if(!Cnpj.TryNormalize(cnpj,out var id))throw Error("Informe um CNPJ válido para consultar.");
  if(id.Any(c=>!char.IsAsciiDigit(c)))throw Error("A consulta pública ainda não está habilitada para CNPJ alfanumérico. Preencha os dados manualmente.");
  await gate.WaitAsync(ct);
  try
  {
   if(cache.TryGetValue(id,out var saved)&&saved.Until>time.GetUtcNow())return saved.Value with{Cached=true};
   bool limited=false;
   foreach(var source in new[]{"BrasilAPI","ReceitaWS"})
   {
    if(blocked.TryGetValue(source,out var until)&&until>time.GetUtcNow()){limited=true;continue;}
    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(12));
    try
    {
     // ReceitaWS's public tier allows three calls/minute; space our own calls too.
     if(source=="ReceitaWS")blocked[source]=time.GetUtcNow().AddSeconds(21);
     using var request=new HttpRequestMessage(HttpMethod.Get,source=="BrasilAPI"?"https://brasilapi.com.br/api/cnpj/v1/"+id:"https://receitaws.com.br/v1/cnpj/"+id);
     request.Headers.UserAgent.ParseAdd("WgsDfeManager/0.14.0");request.Headers.Accept.ParseAdd("application/json");
     using var response=await client.SendAsync(request,timeout.Token);
     if(response.StatusCode==HttpStatusCode.TooManyRequests)
     {
      var delay=response.Headers.RetryAfter?.Delta??(response.Headers.RetryAfter?.Date-time.GetUtcNow())??TimeSpan.FromSeconds(60);
      blocked[source]=time.GetUtcNow().AddSeconds(Math.Clamp(delay.TotalSeconds,1,86400));limited=true;continue;
     }
     if(!response.IsSuccessStatusCode)continue;
     var bytes=await response.Content.ReadAsByteArrayAsync(timeout.Token);
     if(bytes.Length>1024*1024)continue;
     var result=Parse(bytes,id,source);
     if(cache.Count>=500)cache.Clear();cache[id]=(result,time.GetUtcNow().AddHours(24));return result;
    }
    catch(OperationCanceledException) when(!ct.IsCancellationRequested){ }
    catch(HttpRequestException){ }
    catch(JsonException){ }
    catch(CompanyException){ }
   }
   throw Error(limited?"Os serviços públicos de CNPJ estão temporariamente limitados. Tentamos uma fonte alternativa e respeitamos o prazo de cada serviço. Você pode preencher os dados manualmente e salvar agora.":"Não foi possível obter o cadastro nas fontes públicas. Confira o CNPJ e a conexão ou preencha os dados manualmente.");
  }
  finally{gate.Release();}
 }
 public static PublicCompany Parse(byte[] bytes,string expected,string source="BrasilAPI")
 {
  using var json=JsonDocument.Parse(bytes);var root=json.RootElement;
  if(root.ValueKind!=JsonValueKind.Object)throw Error("Resposta inválida da consulta.");
  string? Get(string name)=>root.TryGetProperty(name,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()?.Trim():null;
  if(!Cnpj.TryNormalize(Get("cnpj"),out var id)||id!=expected)throw Error("O CNPJ retornado não corresponde ao consultado. Os dados não foram preenchidos.");
  var name=Get(source=="ReceitaWS"?"nome":"razao_social");var state=Get("uf");var trade=Get(source=="ReceitaWS"?"fantasia":"nome_fantasia");var city=Get("municipio");
  if(string.IsNullOrWhiteSpace(name)||name.Length>300||state is null||!CompanyValidation.States.Contains(state)||trade?.Length>300||city?.Length>150)throw Error("A consulta retornou um cadastro incompleto ou inválido. Preencha manualmente.");
  return new(id,name,trade,state,city,source);
 }
 private static CompanyException Error(string text)=>new(CompanyError.InvalidData,text);
}
