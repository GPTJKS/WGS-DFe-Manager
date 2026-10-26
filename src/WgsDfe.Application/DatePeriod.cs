using System.Globalization;
namespace WgsDfe.Application;
public static class DatePeriod
{
 public static bool TryParse(string? text,out DateOnly? date)
 {
  date=null;if(string.IsNullOrWhiteSpace(text))return true;
  if(text.Length!=10||text.Where((c,i)=>i is 2 or 5?c!='/':!char.IsAsciiDigit(c)).Any())return false;
  if(!DateOnly.TryParseExact(text,"dd/MM/yyyy",CultureInfo.InvariantCulture,DateTimeStyles.None,out var value))return false;
  date=value;return true;
 }
}
