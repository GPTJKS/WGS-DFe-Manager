using Microsoft.EntityFrameworkCore.Migrations;
namespace WgsDfe.Database.Migrations;
public partial class CompanySoftDeletion:Migration
{
 protected override void Up(MigrationBuilder migrationBuilder)=>migrationBuilder.AddColumn<bool>(name:"IsDeleted",table:"Companies",type:"INTEGER",nullable:false,defaultValue:false);
 protected override void Down(MigrationBuilder migrationBuilder)=>migrationBuilder.DropColumn(name:"IsDeleted",table:"Companies");
}
