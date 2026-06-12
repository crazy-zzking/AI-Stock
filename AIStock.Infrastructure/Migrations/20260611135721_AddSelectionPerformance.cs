using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectionPerformance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "selection_performance",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    trading_date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    strategy = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    strategy_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    selection_result_id = table.Column<long>(type: "bigint", nullable: false),
                    score = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    signal_close = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    entry_date = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    entry_price = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    untradable = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ret1 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ret3 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    ret5 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    excess1 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    excess3 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    excess5 = table.Column<decimal>(type: "decimal(65,30)", nullable: true),
                    status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_selection_performance", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_selection_performance_status",
                table: "selection_performance",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_selection_performance_strategy",
                table: "selection_performance",
                column: "strategy");

            migrationBuilder.CreateIndex(
                name: "IX_selection_performance_trading_date_strategy_code",
                table: "selection_performance",
                columns: new[] { "trading_date", "strategy", "code" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "selection_performance");
        }
    }
}
