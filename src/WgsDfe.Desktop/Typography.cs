using System.Windows.Media;
namespace WgsDfe.Desktop;
/// <summary>
/// Configuração tipográfica central do aplicativo. Altere aqui para trocar a fonte em todas as telas.
/// A fonte Inter (SIL Open Font License) é embarcada em Assets/Fonts; Segoe UI é usada como fallback.
/// </summary>
public static class Typography
{
 private static readonly Uri BaseUri = new("pack://application:,,,/WgsDfe.Desktop;component/");
 /// <summary>Fonte padrão da interface (textos, menus, botões, tabelas, formulários e títulos).</summary>
 public static FontFamily Ui { get; } = new(BaseUri, "./Assets/Fonts/#Inter, Segoe UI");
 /// <summary>Fonte monoespaçada para chaves de acesso, códigos e XML.</summary>
 public static FontFamily Mono { get; } = new("Cascadia Mono, Consolas");
 public const double BaseSize = 12.5;
}
