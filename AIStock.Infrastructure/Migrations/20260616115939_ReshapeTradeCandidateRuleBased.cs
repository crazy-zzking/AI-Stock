using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ReshapeTradeCandidateRuleBased : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trade_candidate_trading_date_strategy_code",
                table: "trade_candidate");

            migrationBuilder.DropColumn(
                name: "recommendation",
                table: "trade_candidate");

            migrationBuilder.RenameColumn(
                name: "strategy_name",
                table: "trade_candidate",
                newName: "top_strategy_name");

            migrationBuilder.RenameColumn(
                name: "strategy",
                table: "trade_candidate",
                newName: "top_strategy");

            migrationBuilder.RenameColumn(
                name: "risk_flags",
                table: "trade_candidate",
                newName: "tags");

            migrationBuilder.RenameColumn(
                name: "confidence",
                table: "trade_candidate",
                newName: "rating_stars");

            migrationBuilder.AddColumn<int>(
                name: "hit_count",
                table: "trade_candidate",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "hit_strategies",
                table: "trade_candidate",
                type: "longtext",
                nullable: false)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "score",
                table: "trade_candidate",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_trade_candidate_trading_date_code",
                table: "trade_candidate",
                columns: new[] { "trading_date", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_trade_candidate_trading_date_code",
                table: "trade_candidate");

            migrationBuilder.DropColumn(
                name: "hit_count",
                table: "trade_candidate");

            migrationBuilder.DropColumn(
                name: "hit_strategies",
                table: "trade_candidate");

            migrationBuilder.DropColumn(
                name: "score",
                table: "trade_candidate");

            migrationBuilder.RenameColumn(
                name: "top_strategy_name",
                table: "trade_candidate",
                newName: "strategy_name");

            migrationBuilder.RenameColumn(
                name: "top_strategy",
                table: "trade_candidate",
                newName: "strategy");

            migrationBuilder.RenameColumn(
                name: "tags",
                table: "trade_candidate",
                newName: "risk_flags");

            migrationBuilder.RenameColumn(
                name: "rating_stars",
                table: "trade_candidate",
                newName: "confidence");

            migrationBuilder.AddColumn<string>(
                name: "recommendation",
                table: "trade_candidate",
                type: "varchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_trade_candidate_trading_date_strategy_code",
                table: "trade_candidate",
                columns: new[] { "trading_date", "strategy", "code" },
                unique: true);
        }
    }
}
