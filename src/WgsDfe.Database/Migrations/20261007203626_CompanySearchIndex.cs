using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class CompanySearchIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "Companies",
                type: "TEXT",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            // Preserva os cadastros da Fase 1 e prepara a pesquisa sem acentos.
            migrationBuilder.Sql("UPDATE Companies SET SearchText = LegalName || ' ' || COALESCE(TradeName, '') || ' ' || Cnpj || ' ' || substr(Cnpj,1,2) || '.' || substr(Cnpj,3,3) || '.' || substr(Cnpj,6,3) || '/' || substr(Cnpj,9,4) || '-' || substr(Cnpj,13,2) || ' ' || COALESCE(City, '');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'á', 'a');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'à', 'a');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'â', 'a');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ã', 'a');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ä', 'a');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'é', 'e');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'è', 'e');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ê', 'e');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ë', 'e');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'í', 'i');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ì', 'i');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'î', 'i');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ï', 'i');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ó', 'o');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ò', 'o');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ô', 'o');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'õ', 'o');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ö', 'o');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ú', 'u');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ù', 'u');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'û', 'u');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ü', 'u');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'ç', 'c');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Á', 'A');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'À', 'A');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Â', 'A');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ã', 'A');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ä', 'A');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'É', 'E');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'È', 'E');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ê', 'E');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ë', 'E');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Í', 'I');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ì', 'I');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Î', 'I');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ï', 'I');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ó', 'O');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ò', 'O');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ô', 'O');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Õ', 'O');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ö', 'O');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ú', 'U');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ù', 'U');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Û', 'U');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ü', 'U');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = replace(SearchText, 'Ç', 'C');");
            migrationBuilder.Sql("UPDATE Companies SET SearchText = trim(upper(SearchText));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "Companies");
        }
    }
}
