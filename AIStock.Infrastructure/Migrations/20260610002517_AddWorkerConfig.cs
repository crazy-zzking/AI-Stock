using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 注：stock_concept_relation.concept_digest 已由 20260608150000_AddConceptDigest 建过，
            // 但当时未同步更新 ModelSnapshot，导致此处被重复 diff 出来。本迁移只新建 worker_config；
            // 重复的 concept_digest AddColumn 已移除（快照在本迁移一并补齐该字段，修复历史漂移）。
            migrationBuilder.CreateTable(
                name: "worker_config",
                columns: table => new
                {
                    section = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    config_json = table.Column<string>(type: "text", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_worker_config", x => x.section);
                })
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "worker_config");
        }
    }
}
