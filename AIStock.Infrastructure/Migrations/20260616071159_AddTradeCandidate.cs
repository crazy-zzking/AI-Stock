using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeCandidate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "trade_candidate",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    trading_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy_name = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    source_batch_id = table.Column<long>(type: "bigint", nullable: false),
                    recommendation = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    confidence = table.Column<int>(type: "int", nullable: false),
                    risk_flags = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    narrative = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ref_close = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    buy_low = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    buy_high = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    stop_loss = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    take_profit = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    plan_basis = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    status = table.Column<int>(type: "int", nullable: false),
                    order_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    order_price = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    order_volume = table.Column<long>(type: "bigint", nullable: true),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_trade_candidate", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_trade_candidate_status",
                table: "trade_candidate",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_trade_candidate_trading_date",
                table: "trade_candidate",
                column: "trading_date");

            migrationBuilder.CreateIndex(
                name: "IX_trade_candidate_trading_date_strategy_code",
                table: "trade_candidate",
                columns: new[] { "trading_date", "strategy", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "trade_candidate");
        }
    }
}
