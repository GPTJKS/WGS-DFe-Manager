using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class CteDistributionInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Participation",
                table: "FiscalDocumentCompanies",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CteDistributionReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedNsu = table.Column<string>(type: "TEXT", nullable: false),
                    SpecificNsu = table.Column<string>(type: "TEXT", nullable: true),
                    ResponseBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ResponseHash = table.Column<string>(type: "TEXT", nullable: false),
                    Applied = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseCode = table.Column<int>(type: "INTEGER", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CteDistributionReceipts", x => x.Id);
                    table.UniqueConstraint("AK_CteDistributionReceipts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_CteDistributionReceipts_Companies_OrganizationId_CompanyId",
                        columns: x => new { x.OrganizationId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CteDistributionReceipts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CteDistributionItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ReceiptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Nsu = table.Column<string>(type: "TEXT", nullable: false),
                    Schema = table.Column<string>(type: "TEXT", nullable: false),
                    AccessKey = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<int>(type: "INTEGER", nullable: true),
                    XmlBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    XmlHash = table.Column<string>(type: "TEXT", nullable: false),
                    Archived = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResultCode = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CteDistributionItems", x => x.Id);
                    table.UniqueConstraint("AK_CteDistributionItems_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_CteDistributionItems_Companies_OrganizationId_CompanyId",
                        columns: x => new { x.OrganizationId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CteDistributionItems_CteDistributionReceipts_OrganizationId_ReceiptId",
                        columns: x => new { x.OrganizationId, x.ReceiptId },
                        principalTable: "CteDistributionReceipts",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CteDistributionItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CteDistributionItems_OrganizationId_CompanyId_Environment_Archived",
                table: "CteDistributionItems",
                columns: new[] { "OrganizationId", "CompanyId", "Environment", "Archived" });

            migrationBuilder.CreateIndex(
                name: "IX_CteDistributionItems_OrganizationId_ReceiptId_Nsu",
                table: "CteDistributionItems",
                columns: new[] { "OrganizationId", "ReceiptId", "Nsu" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CteDistributionReceipts_OrganizationId_CompanyId_Environment_Applied",
                table: "CteDistributionReceipts",
                columns: new[] { "OrganizationId", "CompanyId", "Environment", "Applied" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CteDistributionItems");

            migrationBuilder.DropTable(
                name: "CteDistributionReceipts");

            migrationBuilder.DropColumn(
                name: "Participation",
                table: "FiscalDocumentCompanies");
        }
    }
}
