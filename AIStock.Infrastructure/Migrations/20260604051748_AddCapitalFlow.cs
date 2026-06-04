using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCapitalFlow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_capital_flow",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    date = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    main_net_inflow = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    super_large_net = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    large_net = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    medium_net = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    small_net = table.Column<decimal>(type: "decimal(65,30)", nullable: false),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_daily_capital_flow", x => x.id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_daily_capital_flow_code_date",
                table: "daily_capital_flow",
                columns: new[] { "code", "date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_daily_capital_flow_date",
                table: "daily_capital_flow",
                column: "date");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "daily_capital_flow");
        }
    }
}
