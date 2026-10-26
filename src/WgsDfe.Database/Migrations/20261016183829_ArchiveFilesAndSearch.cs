using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class ArchiveFilesAndSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DeclaredStatus",
                table: "FiscalDocuments",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "IssueDateOriginal",
                table: "FiscalDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IssueLocalDate",
                table: "FiscalDocuments",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseCode",
                table: "FiscalDocuments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "FiscalDocuments",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<long>(
                name: "TotalCents",
                table: "FiscalDocuments",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EventDateOriginal",
                table: "FiscalDocumentEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Model",
                table: "FiscalDocumentEvents",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReceiptDateUtc",
                table: "FiscalDocumentEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ResponseCode",
                table: "FiscalDocumentEvents",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ArchiveFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FiscalDocumentId = table.Column<Guid>(type: "TEXT", nullable: true),
                    FiscalDocumentEventId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", nullable: false),
                    ByteLength = table.Column<long>(type: "INTEGER", nullable: false),
                    IsConflict = table.Column<bool>(type: "INTEGER", nullable: false),
                    ResponseCode = table.Column<int>(type: "INTEGER", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchiveFiles", x => x.Id);
                    table.UniqueConstraint("AK_ArchiveFiles_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_ArchiveFile_Owner", "(FiscalDocumentId IS NOT NULL AND FiscalDocumentEventId IS NULL) OR (FiscalDocumentId IS NULL AND FiscalDocumentEventId IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_ArchiveFiles_FiscalDocumentEvents_OrganizationId_FiscalDocumentEventId",
                        columns: x => new { x.OrganizationId, x.FiscalDocumentEventId },
                        principalTable: "FiscalDocumentEvents",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArchiveFiles_FiscalDocuments_OrganizationId_FiscalDocumentId",
                        columns: x => new { x.OrganizationId, x.FiscalDocumentId },
                        principalTable: "FiscalDocuments",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ArchiveFiles_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiscalDocuments_OrganizationId_IssueLocalDate_TotalCents",
                table: "FiscalDocuments",
                columns: new[] { "OrganizationId", "IssueLocalDate", "TotalCents" });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveFiles_OrganizationId_FiscalDocumentEventId",
                table: "ArchiveFiles",
                columns: new[] { "OrganizationId", "FiscalDocumentEventId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveFiles_OrganizationId_FiscalDocumentId",
                table: "ArchiveFiles",
                columns: new[] { "OrganizationId", "FiscalDocumentId" });

            migrationBuilder.CreateIndex(
                name: "IX_ArchiveFiles_OrganizationId_Hash",
                table: "ArchiveFiles",
                columns: new[] { "OrganizationId", "Hash" },
                unique: true);
            migrationBuilder.Sql("UPDATE FiscalDocuments SET DeclaredStatus = Status;");
            migrationBuilder.Sql("UPDATE FiscalDocumentEvents SET Model = CAST(substr(AccessKey,21,2) AS INTEGER) WHERE substr(AccessKey,21,2) IN ('55','57','62','65','67');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchiveFiles");

            migrationBuilder.DropIndex(
                name: "IX_FiscalDocuments_OrganizationId_IssueLocalDate_TotalCents",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "DeclaredStatus",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "IssueDateOriginal",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "IssueLocalDate",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "ResponseCode",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "TotalCents",
                table: "FiscalDocuments");

            migrationBuilder.DropColumn(
                name: "EventDateOriginal",
                table: "FiscalDocumentEvents");

            migrationBuilder.DropColumn(
                name: "Model",
                table: "FiscalDocumentEvents");

            migrationBuilder.DropColumn(
                name: "ReceiptDateUtc",
                table: "FiscalDocumentEvents");

            migrationBuilder.DropColumn(
                name: "ResponseCode",
                table: "FiscalDocumentEvents");
        }
    }
}
