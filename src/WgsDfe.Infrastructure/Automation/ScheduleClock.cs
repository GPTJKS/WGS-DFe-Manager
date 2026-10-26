using WgsDfe.Application.Automation;
namespace WgsDfe.Infrastructure.Automation;
public static class ScheduleClock
{
 // Strictly after the supplied instant; skipped DST minutes move to the first valid minute.
 // Ambiguous times use the later UTC occurrence, once per local day.
 public static DateTime Next(ScheduleInput input,DateTime afterUtc)
 {
  if(afterUtc.Kind!=DateTimeKind.Utc||input.Hour is <0 or >23||input.Minute is <0 or >59)throw new AutomationException("SCHEDULE_INVALID");
  TimeZoneInfo zone;try{zone=TimeZoneInfo.FindSystemTimeZoneById(input.TimeZoneId);}catch{throw new AutomationException("SCHEDULE_INVALID");}
  var date=TimeZoneInfo.ConvertTimeFromUtc(afterUtc,zone).Date;
  for(int day=0;day<370;day++)
  {
   var local=DateTime.SpecifyKind(date.AddDays(day).AddHours(input.Hour).AddMinutes(input.Minute),DateTimeKind.Unspecified);
   int skipped=0;while(zone.IsInvalidTime(local)&&skipped++<180)local=local.AddMinutes(1);
   if(zone.IsInvalidTime(local))continue;
   var utc=zone.IsAmbiguousTime(local)?zone.GetAmbiguousTimeOffsets(local).Select(o=>new DateTimeOffset(local,o).UtcDateTime).Max():TimeZoneInfo.ConvertTimeToUtc(local,zone);
   if(utc>afterUtc)return utc;
  }
  throw new AutomationException("SCHEDULE_INVALID");
 }
}
