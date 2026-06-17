using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBacktestResult : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "backtest_result",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    run_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    backtest_type = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy_key = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    config_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    report_json = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    executed_trades = table.Column<int>(type: "int", nullable: false),
                    win_rate_pct = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    avg_return_pct = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    sharpe_ratio = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    max_drawdown_pct = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    alpha_pct = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    beta = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_backtest_result", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_backtest_result_backtest_type_strategy_key",
                table: "backtest_result",
                columns: new[] { "backtest_type", "strategy_key" });

            migrationBuilder.CreateIndex(
                name: "IX_backtest_result_created_at",
                table: "backtest_result",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_backtest_result_run_at",
                table: "backtest_result",
                column: "run_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "backtest_result");
        }
    }
}
