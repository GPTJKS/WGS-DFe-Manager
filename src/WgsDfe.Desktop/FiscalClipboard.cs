using System.Windows;
using System.Windows.Controls;
using WgsDfe.Domain;
namespace WgsDfe.Desktop;
internal static class FiscalClipboard
{
 internal static void CopyKey(string? value,Action<string> feedback)
 {
  try
  {
   var key=DfeAccessKey.Parse(value??"").Value;
   if(key.Length!=44||!key.All(char.IsAsciiDigit)){feedback("Chave inválida: são necessários 44 dígitos válidos.");return;}
   Clipboard.SetText(key);feedback("Chave copiada");
  }
  catch(AccessKeyException){feedback("Chave inválida. Confira os 44 dígitos e os dígitos verificadores.");}
  catch(System.Runtime.InteropServices.COMException){feedback("Não foi possível copiar. Tente novamente.");}
 }
}
