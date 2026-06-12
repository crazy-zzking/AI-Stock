using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerJobRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "worker_job_run",
                columns: table => new
                {
                    job_name = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    is_running = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    last_start = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_end = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_duration_ms = table.Column<long>(type: "bigint", nullable: true),
                    last_trigger = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    last_success = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    last_error = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    run_requested = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    requested_at = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_job_run", x => x.job_name);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_job_run");
        }
    }
}
