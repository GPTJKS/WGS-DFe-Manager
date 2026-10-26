using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WgsDfe.Application;
namespace WgsDfe.Desktop;
internal static class DateInput
{
 private sealed class State {public bool Attached,Invalid;}
 private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<DatePicker,State> States=new();
 public static void Register()
 {
  EventManager.RegisterClassHandler(typeof(DatePicker),FrameworkElement.LoadedEvent,new RoutedEventHandler((s,_)=>
  {
   var picker=(DatePicker)s;picker.Language=System.Windows.Markup.XmlLanguage.GetLanguage("pt-BR");picker.SelectedDateFormat=DatePickerFormat.Short;
   var state=States.GetOrCreateValue(picker);if(state.Attached)return;state.Attached=true;
   picker.DateValidationError+=(_,args)=>{state.Invalid=true;args.ThrowException=false;};
   picker.SelectedDateChanged+=(_,_)=>state.Invalid=false;
   picker.ToolTip="DD/MM/AAAA — digite uma data válida ou use o calendário. Vazio inclui todo o período.";
   if(picker.Template.FindName("PART_TextBox",picker) is DatePickerTextBox text)
   {
    text.MaxLength=10;
    text.PreviewTextInput+=(_,e)=>
    {
     state.Invalid=false;e.Handled=e.Text.Any(c=>!char.IsAsciiDigit(c)&&c!='/');
     if(!e.Handled&&e.Text.Length==1&&e.Text!="/"&&text.SelectionLength==0&&text.CaretIndex==text.Text.Length&&text.Text.Length is 2 or 5){text.Text+="/";text.CaretIndex=text.Text.Length;}
    };
    text.PreviewKeyDown+=(_,e)=>{if(e.Key is Key.Back or Key.Delete)state.Invalid=false;};
    DataObject.AddPastingHandler(text,(_,e)=>{state.Invalid=false;if(e.DataObject.GetData(typeof(string)) is not string value||value.Any(c=>!char.IsAsciiDigit(c)&&c!='/'))e.CancelCommand();});
   }
  }));
 }
 private static string Raw(DatePicker p)=>p.Template?.FindName("PART_TextBox",p) is DatePickerTextBox t?t.Text:p.Text;
 public static bool Validate(DatePicker from,DatePicker to,out string error)
 {
  error="";
  if(States.GetOrCreateValue(from).Invalid||States.GetOrCreateValue(to).Invalid||!DatePeriod.TryParse(Raw(from),out var f)||!DatePeriod.TryParse(Raw(to),out var t)){error="Informe datas válidas no formato DD/MM/AAAA. Corrija o campo antes de continuar.";return false;}
  if(f.HasValue&&t.HasValue&&f>t){error="A data inicial não pode ser posterior à data final.";return false;}
  from.SelectedDate=f?.ToDateTime(TimeOnly.MinValue);to.SelectedDate=t?.ToDateTime(TimeOnly.MinValue);return true;
 }
}
