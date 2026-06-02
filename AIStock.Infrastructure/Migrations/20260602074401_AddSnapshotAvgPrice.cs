using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSnapshotAvgPrice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "avg_price",
                table: "daily_market_snapshot",
                type: "decimal(65,30)",
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "avg_price",
                table: "daily_market_snapshot");
        }
    }
}
