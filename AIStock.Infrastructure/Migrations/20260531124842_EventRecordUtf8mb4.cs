using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class EventRecordUtf8mb4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 历史 event_record 表用 utf8(3字节) 建，存不下 emoji(4字节)；整表转 utf8mb4
            migrationBuilder.Sql(
                "ALTER TABLE event_record CONVERT TO CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE event_record CONVERT TO CHARACTER SET utf8 COLLATE utf8_general_ci;");
        }
    }
}
