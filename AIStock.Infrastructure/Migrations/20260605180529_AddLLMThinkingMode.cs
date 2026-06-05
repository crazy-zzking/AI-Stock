using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLLMThinkingMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "enable_thinking",
                table: "llm_model_config",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "thinking_budget_tokens",
                table: "llm_model_config",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "enable_thinking",
                table: "llm_model_config");

            migrationBuilder.DropColumn(
                name: "thinking_budget_tokens",
                table: "llm_model_config");
        }
    }
}
