using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WgsDfe.Database.Migrations
{
    /// <inheritdoc />
    public partial class AutomaticSynchronization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SyncSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CompanyId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Environment = table.Column<int>(type: "INTEGER", nullable: false),
                    Hour = table.Column<int>(type: "INTEGER", nullable: false),
                    Minute = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Actor = table.Column<int>(type: "INTEGER", nullable: false),
                    PayloadMode = table.Column<int>(type: "INTEGER", nullable: false),
                    Models = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPages = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastStartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastCode = table.Column<string>(type: "TEXT", nullable: true),
                    Running = table.Column<bool>(type: "INTEGER", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncSchedules", x => x.Id);
                    table.UniqueConstraint("AK_SyncSchedules_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.CheckConstraint("CK_Schedule_Time", "Hour >= 0 AND Hour <= 23 AND Minute >= 0 AND Minute <= 59 AND MaxPages >= 1 AND MaxPages <= 100");
                    table.ForeignKey(
                        name: "FK_SyncSchedules_Companies_OrganizationId_CompanyId",
                        columns: x => new { x.OrganizationId, x.CompanyId },
                        principalTable: "Companies",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncSchedules_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SyncScheduleRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScheduleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncScheduleRuns", x => x.Id);
                    table.UniqueConstraint("AK_SyncScheduleRuns_OrganizationId_Id", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_SyncScheduleRuns_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncScheduleRuns_ProcessingJobs_OrganizationId_JobId",
                        columns: x => new { x.OrganizationId, x.JobId },
                        principalTable: "ProcessingJobs",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SyncScheduleRuns_SyncSchedules_OrganizationId_ScheduleId",
                        columns: x => new { x.OrganizationId, x.ScheduleId },
                        principalTable: "SyncSchedules",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SyncScheduleRuns_OrganizationId_JobId",
                table: "SyncScheduleRuns",
                columns: new[] { "OrganizationId", "JobId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncScheduleRuns_OrganizationId_ScheduleId",
                table: "SyncScheduleRuns",
                columns: new[] { "OrganizationId", "ScheduleId" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncScheduleRuns_OrganizationId_StartedAtUtc",
                table: "SyncScheduleRuns",
                columns: new[] { "OrganizationId", "StartedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SyncSchedules_OrganizationId_CompanyId_ProviderCode_Environment_Hour_Minute_TimeZoneId",
                table: "SyncSchedules",
                columns: new[] { "OrganizationId", "CompanyId", "ProviderCode", "Environment", "Hour", "Minute", "TimeZoneId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncSchedules_OrganizationId_Enabled_NextRunAtUtc",
                table: "SyncSchedules",
                columns: new[] { "OrganizationId", "Enabled", "NextRunAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SyncScheduleRuns");

            migrationBuilder.DropTable(
                name: "SyncSchedules");
        }
    }
}
