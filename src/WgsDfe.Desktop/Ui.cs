using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace WgsDfe.Desktop;
public static class Ui
{
 public static readonly DependencyProperty IconProperty=DependencyProperty.RegisterAttached("Icon",typeof(string),typeof(Ui),new PropertyMetadata(""));
 public static readonly DependencyProperty PlaceholderProperty=DependencyProperty.RegisterAttached("Placeholder",typeof(string),typeof(Ui),new PropertyMetadata(""));
 public static readonly DependencyProperty SearchProperty=DependencyProperty.RegisterAttached("Search",typeof(bool),typeof(Ui),new PropertyMetadata(false));
 public static string GetIcon(DependencyObject target)=>(string)target.GetValue(IconProperty);
 public static void SetIcon(DependencyObject target,string value)=>target.SetValue(IconProperty,value);
 public static string GetPlaceholder(DependencyObject target)=>(string)target.GetValue(PlaceholderProperty);
 public static void SetPlaceholder(DependencyObject target,string value)=>target.SetValue(PlaceholderProperty,value);
 public static bool GetSearch(DependencyObject target)=>(bool)target.GetValue(SearchProperty);
 public static void SetSearch(DependencyObject target,bool value)=>target.SetValue(SearchProperty,value);
 public static RoutedUICommand ClearText {get;}=new("Limpar busca","ClearText",typeof(Ui));
 static Ui(){CommandManager.RegisterClassCommandBinding(typeof(TextBox),new CommandBinding(ClearText,(_,e)=>{if(e.Parameter is TextBox box){box.Clear();box.Focus();}},(_,e)=>e.CanExecute=e.Parameter is TextBox box&&box.Text.Length>0));}
 public static void Decorate(Button button)
 {
  if(button.Content is not string label||GetIcon(button).Length>0)return;
  string icon=label switch
  {
   "+  Nova empresa" or "Adicionar certificado A1"=>"\uE710",
   "Editar cadastro"=>"\uE70F","Modelo de importação"=>"\uE896",
   "Importar XLSX / CSV / TXT"=>"\uE8B5","Atualizar lotes"=>"\uE72C",
   "Conferir / Retomar lote"=>"\uE768","Associar empresa às não associadas"=>"\uE71B",
   "Salvar relatório CSV"=>"\uE74E","Ver diagnóstico"=>"\uE946",
   "Pesquisar"=>"\uE721","Consultar e baixar XMLs"=>"\uE896",
   "Exportar XMLs locais" or "Exportar XMLs do período" or "Exportar para o destino"=>"\uE78C",
   "Histórico de exportações" or "Preparar restauração"=>"\uE81C",
   "Salvar empresa" or "Validar e adicionar certificado" or "Validar e substituir certificado"=>"\uE74E",
   "Abrir nota" or "Detalhes / versões / XML"=>"\uE890",
   "Criar backup"=>"\uE753","Certificados" or "Gerenciar vínculos"=>"\uE83D",
   "Atualizar espaço ocupado" or "Atualizar / limpar pendências" or "Atualizar vínculos com empresas"=>"\uE72C",
   "Abrir pasta de dados" or "Escolher arquivo PFX / P12" or "Escolher destino"=>"\uE8B7",
   "Cancelar" or "Cancelar operação"=>"\uE711","Copiar diagnóstico"=>"\uE8C8",
   "Recuperação histórica"=>"\uE81C","Inspecionar XML"=>"\uE943",
   "Automações opcionais"=>"\uE713","Pasta padrão de exportação"=>"\uE8B7",
   "Avaliar e limpar cópias locais"=>"\uE74D","Selecionar todas"=>"\uE762",
   _=>""
  };
  SetIcon(button,icon);
 }
}
