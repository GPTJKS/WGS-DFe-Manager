using System.Globalization;
namespace WgsDfe.Domain;
public readonly record struct Nsu
{
 public ulong Value { get; }
 private Nsu(ulong value) => Value=value;
 public static Nsu Parse(string text)
 {
  if (string.IsNullOrEmpty(text) || text.Length>18 || text.Any(c=>c<'0'||c>'9') ||
      !ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var value))
   throw new ArgumentException("NSU deve conter de 1 a 18 dígitos.",nameof(text));
  return new Nsu(value);
 }
 public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
