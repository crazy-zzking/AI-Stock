using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddStockBaseDetailFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "company_name",
                table: "stock_base",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "employee_count",
                table: "stock_base",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "main_business",
                table: "stock_base",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "profile",
                table: "stock_base",
                type: "text",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "province",
                table: "stock_base",
                type: "varchar(50)",
                maxLength: 50,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "reg_capital",
                table: "stock_base",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sub_industry",
                table: "stock_base",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "website",
                table: "stock_base",
                type: "varchar(200)",
                maxLength: 200,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "company_name",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "employee_count",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "main_business",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "profile",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "province",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "reg_capital",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "sub_industry",
                table: "stock_base");

            migrationBuilder.DropColumn(
                name: "website",
                table: "stock_base");
        }
    }
}
