using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectionReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "review_status",
                table: "selection_result",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "pending")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "reviewed_at",
                table: "selection_result",
                type: "datetime(6)",
                nullable: true);

            // 历史已存在批次不回溯复评，统一标记为 skipped
            migrationBuilder.Sql("UPDATE `selection_result` SET `review_status` = 'skipped';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "review_status",
                table: "selection_result");

            migrationBuilder.DropColumn(
                name: "reviewed_at",
                table: "selection_result");
        }
    }
}
