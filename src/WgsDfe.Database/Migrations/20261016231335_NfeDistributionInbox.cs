using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class NfeDistributionInbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NfeDistributionReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Actor = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadMode = table.Column<int>(type: "INTEGER", nullable: false),
                    AccessKey = table.Column<string>(type: "TEXT", nullable: true),
                    RequestedNsu = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseBytes = table.Column<byte[]>(type: "BLOB", nullable: false),
                    ResponseHash = table.Column<string>(type: "TEXT", nullable: false),
                    Applied = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseCode = table.Column<int>(type: "INTEGER", nullable: true),
                    ResultCode = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NfeDistributionReceipts", x => x.Id);
                    table.UniqueConstraint("AK_NfeDistributionReceipts_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_NfeDistributionReceipts_Companies_OrganizationId_CompanyId",
                        columns: x => new { x.OrganizationId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_NfeDistributionReceipts_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NfeDistributionReceipts_OrganizationId_CompanyId_ProviderCode_Applied",
                table: "NfeDistributionReceipts",
                columns: new[] { "OrganizationId", "CompanyId", "ProviderCode", "Applied" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NfeDistributionReceipts");
        }
    }
}
