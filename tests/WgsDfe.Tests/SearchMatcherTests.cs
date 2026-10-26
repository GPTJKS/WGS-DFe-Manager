using WgsDfe.Domain;
using Xunit;
namespace WgsDfe.Tests;
public class SearchMatcherTests
{
 [Theory]
 [InlineData("  São--José  ","SAO JOSE")]
 [InlineData("Açúcar & café","ACUCAR CAFE")]
 public void Normalizes_names(string value,string expected)=>Assert.Equal(expected,SearchMatcher.Normalize(value));
 [Theory]
 [InlineData("sorvetes frutaroma")]
 [InlineData("frutarma")]
 [InlineData("frutaaroma")]
 [InlineData("frutarona")]
 [InlineData("fruatroma")]
 public void Finds_names_with_tokens_and_bounded_typos(string query)=>Assert.NotNull(SearchMatcher.Rank(query,["Frutaroma Indústria de Sorvetes"],[]));
 [Fact]
 public void Ranks_exact_before_prefix_substring_tokens_and_typo()
 {
  int Score(string name)=>SearchMatcher.Rank("frutaroma",[name],[])!.Value;
  Assert.True(Score("Frutaroma")<Score("Frutaroma sorvetes"));
  Assert.True(Score("Frutaroma sorvetes")<Score("Sorvetes Frutaroma"));
  Assert.True(Score("Sorvetes Frutaroma")<Score("Frutarona"));
 }
 [Fact]
 public void Document_number_search_is_exact_by_numeric_value()
 {
  const string key="42261005486315000104650010000011031000011030";
  string[] ids=[key,"05486315000104","12345678000195"];
  Assert.Equal(0,SearchMatcher.RankFiscal("1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("000001103",["Loja"],ids,["1103"]));
  // 1103 appears inside the access key and in other numbers, but they must not match.
  Assert.Null(SearchMatcher.RankFiscal("1103",["Loja"],ids,["000011030"]));
  Assert.Null(SearchMatcher.RankFiscal("1103",["Loja"],ids,["000001104"]));
  Assert.Null(SearchMatcher.RankFiscal("110",["Loja"],ids,["000001103"]));
 }
 [Fact]
 public void Document_number_search_with_fiscal_prefixes()
 {
  const string key="42261005486315000104650010000011031000011030";
  string[] ids=[key,"05486315000104"];
  Assert.Equal(0,SearchMatcher.RankFiscal("NFC 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("nfc-e 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("NF 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("nfe 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("nota 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("nº 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("N° 1103",["Loja"],ids,["000001103"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("num 1103",["Loja"],ids,["000001103"]));
  Assert.Null(SearchMatcher.RankFiscal("NFC 1103",["Loja"],ids,["000001104"]));
 }
 [Fact]
 public void Combined_company_name_and_document_number_search()
 {
  const string key="42261005486315000104650010000011031000011030";
  string[] ids=[key,"05486315000104"];
  Assert.NotNull(SearchMatcher.RankFiscal("Frutaroma 1103",["Frutaroma Indústria de Alimentos"],ids,["000001103"]));
  Assert.Null(SearchMatcher.RankFiscal("Frutaroma 1103",["Outra Empresa"],ids,["000001103"]));
  Assert.Null(SearchMatcher.RankFiscal("Frutaroma 1103",["Frutaroma Indústria de Alimentos"],ids,["000001104"]));
 }
 [Fact]
 public void Generic_rank_does_not_match_access_key_as_short_substring()
 {
  const string key="42261005486315000104650010000011031000011030";
  Assert.Null(SearchMatcher.Rank("1103",["Loja Comercial"], [key]));
 }
 [Fact]
 public void Fiscal_search_keeps_cnpj_root_full_key_and_names()
 {
  const string key="42261005486315000104650010000011031000011030";
  string[] ids=[key,"05486315000104"];
  Assert.NotNull(SearchMatcher.RankFiscal("05.486.315",[],ids,["000000001"]));
  Assert.Equal(0,SearchMatcher.RankFiscal("05.486.315/0001-04",[],ids,["000000001"]));
  Assert.Equal(0,SearchMatcher.RankFiscal(key,[],ids,["000000001"]));
  Assert.NotNull(SearchMatcher.RankFiscal("frutaroma",["Frutaroma Indústria"],ids,["000000001"]));
 }
 [Fact]
 public void Fiscal_identifiers_never_accept_typos()
 {
  Assert.Equal(0,SearchMatcher.Rank("21.014.243/0001-59",[],["21014243000159"]));
  Assert.Null(SearchMatcher.Rank("21014243000158",[],["21014243000159"]));
  Assert.Null(SearchMatcher.Rank("ABCDXFGH",[],["ABCDEFGH"]));
  Assert.Null(SearchMatcher.Rank("completamente diferente",["Frutaroma"],[]));
 }
 [Theory]
 [InlineData("STJ","S.T.J Ferramentaria")]
 [InlineData("stj","S. T. J. Ferramentaria")]
 [InlineData("  sao   jose ","São José")]
 [InlineData("SAOJOSE","São José")]
 [InlineData("A.C.M.E","Acme Serviços")]
 public void Finds_company_ignoring_spacing_accents_and_punctuation(string query,string name)
  =>Assert.NotNull(SearchMatcher.Rank(query,[name],[]));
 [Fact]
 public void Finds_masked_tax_identifier()=>Assert.NotNull(SearchMatcher.Rank("12.345.678/0001-95",[],["12345678000195"]));}
