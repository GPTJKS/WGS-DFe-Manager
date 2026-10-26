using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class HistoricalRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoricalRecoveryBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    PaidCallsReserved = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalRecoveryBatches", x => x.Id);
                    table.UniqueConstraint("AK_HistoricalRecoveryBatches_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_HistoricalRecoveryBatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricalRecoveryBatches_ProcessingJobs_OrganizationId_JobId",
                        columns: x => new { x.OrganizationId, x.JobId },
                        principalTable: "ProcessingJobs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HistoricalRecoveryItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceLine = table.Column<int>(type: "INTEGER", nullable: false),
                    Input = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccessKey = table.Column<string>(type: "TEXT", maxLength: 44, nullable: true),
                    Model = table.Column<int>(type: "INTEGER", nullable: true),
                    IssuerTaxId = table.Column<string>(type: "TEXT", nullable: true),
                    Year = table.Column<int>(type: "INTEGER", nullable: true),
                    Month = table.Column<int>(type: "INTEGER", nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Availability = table.Column<int>(type: "INTEGER", nullable: false),
                    ResultCode = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", nullable: true),
                    ExternalJobId = table.Column<string>(type: "TEXT", nullable: true),
                    Attempts = table.Column<int>(type: "INTEGER", nullable: false),
                    NextAllowedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ResponseBytes = table.Column<byte[]>(type: "BLOB", nullable: true),
                    ResponseHash = table.Column<string>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoricalRecoveryItems", x => x.Id);
                    table.UniqueConstraint("AK_HistoricalRecoveryItems_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_HistoricalItem_KeyLength", "AccessKey IS NULL OR length(AccessKey) = 44");
                    table.ForeignKey(
                        name: "FK_HistoricalRecoveryItems_Companies_OrganizationId_CompanyId",
                        columns: x => new { x.OrganizationId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricalRecoveryItems_HistoricalRecoveryBatches_OrganizationId_BatchId",
                        columns: x => new { x.OrganizationId, x.BatchId },
                        principalTable: "HistoricalRecoveryBatches",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoricalRecoveryItems_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalRecoveryBatches_OrganizationId_JobId",
                table: "HistoricalRecoveryBatches",
                columns: new[] { "OrganizationId", "JobId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalRecoveryItems_OrganizationId_BatchId_AccessKey",
                table: "HistoricalRecoveryItems",
                columns: new[] { "OrganizationId", "BatchId", "AccessKey" });

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalRecoveryItems_OrganizationId_BatchId_SourceLine",
                table: "HistoricalRecoveryItems",
                columns: new[] { "OrganizationId", "BatchId", "SourceLine" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HistoricalRecoveryItems_OrganizationId_CompanyId",
                table: "HistoricalRecoveryItems",
                columns: new[] { "OrganizationId", "CompanyId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoricalRecoveryItems");

            migrationBuilder.DropTable(
                name: "HistoricalRecoveryBatches");
        }
    }
}
