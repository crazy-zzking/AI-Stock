using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTradeCandidateOrderStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "order_filled_volume",
                table: "trade_candidate",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "order_status_text",
                table: "trade_candidate",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "order_filled_volume",
                table: "trade_candidate");

            migrationBuilder.DropColumn(
                name: "order_status_text",
                table: "trade_candidate");
        }
    }
}
