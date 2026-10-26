using System.Globalization;
using System.Text;
namespace WgsDfe.Domain;
public enum AccessKeyError { Empty, Length, Characters, State, Month, Model, Issuer, Number, EmissionType, CheckDigit }
public sealed class AccessKeyException(AccessKeyError code):Exception(code switch
{
 AccessKeyError.Empty=>"Informe a chave de acesso.",AccessKeyError.Length=>"A chave deve conter 44 posições.",
 AccessKeyError.Characters=>"A chave contém caracteres não permitidos.",AccessKeyError.State=>"Código da UF inválido.",
 AccessKeyError.Month=>"Mês da chave inválido.",AccessKeyError.Model=>"Modelo não suportado. Use 55, 57, 62, 65 ou 67.",
 AccessKeyError.Issuer=>"CNPJ/CPF do emitente inválido para a série informada.",AccessKeyError.Number=>"O número do documento deve ser maior que zero.",
 AccessKeyError.EmissionType=>"Tipo de emissão inválido.",_=>"Dígito verificador da chave inválido."
}){public AccessKeyError Code{get;}=code;}
public sealed record DfeAccessKey
{
 private DfeAccessKey(string value){Value=value;}
 public string Value{get;}
 public string StateCode=>Value[..2];
 public string State=>States[StateCode];
 public int Year=>2000+int.Parse(Value.AsSpan(2,2),CultureInfo.InvariantCulture);
 public int Month=>int.Parse(Value.AsSpan(4,2),CultureInfo.InvariantCulture);
 public string IssuerField=>Value.Substring(6,14);
 public bool IsCpfIssuer=>(Model==DocumentModel.NFe&&SeriesNumber is >=910 and <=969)||(Model==DocumentModel.NFCe&&SeriesNumber is >=920 and <=969);
 public string IssuerTaxId=>IsCpfIssuer?IssuerField[3..]:IssuerField;
 public DocumentModel Model=>(DocumentModel)int.Parse(Value.AsSpan(20,2),CultureInfo.InvariantCulture);
 public string ModelName=>Model switch{DocumentModel.NFe=>"NF-e",DocumentModel.NFCe=>"NFC-e",DocumentModel.CTe=>"CT-e",DocumentModel.CTeOS=>"CT-e OS",_=>"NFCom"};
 public string Series=>Value.Substring(22,3);
 public int SeriesNumber=>int.Parse(Series,CultureInfo.InvariantCulture);
 public string Number=>Value.Substring(25,9);
 public int EmissionType=>Value[34]-'0';
 public int? AuthorizerSite=>Model==DocumentModel.NFCom?Value[35]-'0':null;
 public string NumericCode=>Model==DocumentModel.NFCom?Value.Substring(36,7):Value.Substring(35,8);
 public int CheckDigit=>Value[43]-'0';
 public static DfeAccessKey Parse(string? input)
 {
  if(string.IsNullOrEmpty(input))throw new AccessKeyException(AccessKeyError.Empty);
  if(input.Length>4096)throw new AccessKeyException(AccessKeyError.Length);
  var buffer=new StringBuilder(44);
  foreach(char c in input){if(c is ' ' or '\t' or '\r' or '\n')continue;buffer.Append(c is >= 'a' and <= 'z'?(char)(c-32):c);}
  var value=buffer.ToString();if(value.Length==0)throw new AccessKeyException(AccessKeyError.Empty);
  if(value.Length!=44)throw new AccessKeyException(AccessKeyError.Length);
  for(int i=0;i<44;i++)if(!char.IsAsciiDigit(value[i])&&!(i is >=6 and <18&&value[i] is >= 'A' and <= 'Z'))throw new AccessKeyException(AccessKeyError.Characters);
  var key=new DfeAccessKey(value);
  if(!States.ContainsKey(key.StateCode))throw new AccessKeyException(AccessKeyError.State);
  if(key.Month is <1 or >12)throw new AccessKeyException(AccessKeyError.Month);
  if(!Enum.IsDefined(key.Model))throw new AccessKeyException(AccessKeyError.Model);
  if(key.IsCpfIssuer?!(key.IssuerField.StartsWith("000",StringComparison.Ordinal)&&Cpf.IsValid(key.IssuerTaxId)):!Cnpj.TryNormalize(key.IssuerField,out _))throw new AccessKeyException(AccessKeyError.Issuer);
  if(key.Number.All(c=>c=='0'))throw new AccessKeyException(AccessKeyError.Number);
  if(key.EmissionType==0)throw new AccessKeyException(AccessKeyError.EmissionType);
  if(Digit(value.AsSpan(0,43))!=key.CheckDigit)throw new AccessKeyException(AccessKeyError.CheckDigit);
  return key;
 }
 // NT Conjunta DF-e 2025.001, Anexo II: cada caractere ASCII - 48, pesos 2..9 da direita.
 private static int Digit(ReadOnlySpan<char> value)
 {int sum=0,weight=2;for(int i=value.Length-1;i>=0;i--){sum+=(value[i]-48)*weight;weight=weight==9?2:weight+1;}int dv=11-sum%11;return dv>=10?0:dv;}
 public override string ToString()=>Value;
 private static readonly IReadOnlyDictionary<string,string> States=new Dictionary<string,string>
 {{"11","RO"},{"12","AC"},{"13","AM"},{"14","RR"},{"15","PA"},{"16","AP"},{"17","TO"},{"21","MA"},{"22","PI"},{"23","CE"},{"24","RN"},{"25","PB"},{"26","PE"},{"27","AL"},{"28","SE"},{"29","BA"},{"31","MG"},{"32","ES"},{"33","RJ"},{"35","SP"},{"41","PR"},{"42","SC"},{"43","RS"},{"50","MS"},{"51","MT"},{"52","GO"},{"53","DF"}};
}
public static class Cpf
{
 public static bool IsValid(string? cpf)
 {
  if(cpf is null||cpf.Length!=11||cpf.Any(c=>!char.IsAsciiDigit(c))||cpf.Distinct().Count()==1)return false;
  for(int n=9;n<=10;n++){int sum=0;for(int i=0;i<n;i++)sum+=(cpf[i]-'0')*(n+1-i);int digit=sum*10%11;if(digit==10)digit=0;if(cpf[n]-'0'!=digit)return false;}return true;
 }
}
