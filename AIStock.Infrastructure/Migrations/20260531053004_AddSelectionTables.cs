using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSelectionTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_market_snapshot",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    close = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    change_percent = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    turnover_rate = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    volume_ratio = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    amplitude = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    is_limit_up = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    total_market_cap = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    pe_ttm = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    main_net_inflow = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    rise20d = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ma5 = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ma10 = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    ma20 = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    macd_dif = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    macd_dea = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    macd_golden_cross = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    rsi = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_market_snapshot", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "dragon_tiger",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    reason = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    net_buy_amount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    buy_amount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    sell_amount = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    has_institution = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    buy_seats_json = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    sell_seats_json = table.Column<string>(type: "text", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_dragon_tiger", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_daily_market_snapshot_code_date",
                table: "daily_market_snapshot",
                columns: new[] { "code", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_market_snapshot_date",
                table: "daily_market_snapshot",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "IX_dragon_tiger_code_date",
                table: "dragon_tiger",
                columns: new[] { "code", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_dragon_tiger_date",
                table: "dragon_tiger",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_market_snapshot");

            migrationBuilder.DropTable(
                name: "dragon_tiger");
        }
    }
}
