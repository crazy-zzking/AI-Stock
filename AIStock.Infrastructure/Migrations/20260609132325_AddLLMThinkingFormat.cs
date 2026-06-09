using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLLMThinkingFormat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "reasoning_effort",
                table: "llm_model_config",
                type: "varchar(20)",
                maxLength: 20,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "thinking_format",
                table: "llm_model_config",
                type: "varchar(30)",
                maxLength: 30,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            // 历史数据：原先思考模式仅 api.deepseek.com 生效，迁移为 deepseek 格式以保持行为不变
            migrationBuilder.Sql(
                "UPDATE llm_model_config SET thinking_format = 'deepseek' WHERE base_url LIKE '%api.deepseek.com%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reasoning_effort",
                table: "llm_model_config");

            migrationBuilder.DropColumn(
                name: "thinking_format",
                table: "llm_model_config");
        }
    }
}
