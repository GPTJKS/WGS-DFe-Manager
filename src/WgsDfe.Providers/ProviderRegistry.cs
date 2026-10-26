using WgsDfe.Application.Providers;
namespace WgsDfe.Providers;
public sealed class ProviderRegistry : IProviderRegistry
{
 public IReadOnlyCollection<IDocumentProvider> Providers { get; }
 public ProviderRegistry(IEnumerable<IDocumentProvider> providers)
 {
  var list=providers.ToArray();
  if(list.Select(p=>p.Capabilities.Code).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=list.Length)
   throw new ArgumentException("Código de provider duplicado.",nameof(providers));
  Providers=Array.AsReadOnly(list);
 }
}
