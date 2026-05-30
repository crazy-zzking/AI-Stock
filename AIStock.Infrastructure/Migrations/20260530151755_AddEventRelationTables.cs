using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AIStock.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEventRelationTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "event_concept_relation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    concept_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_concept_relation", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_concept_relation_event_record_event_id",
                        column: x => x.event_id,
                        principalTable: "event_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "event_stock_relation",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    event_id = table.Column<long>(type: "bigint", nullable: false),
                    stock_code = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    created_at = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_event_stock_relation", x => x.id);
                    table.ForeignKey(
                        name: "FK_event_stock_relation_event_record_event_id",
                        column: x => x.event_id,
                        principalTable: "event_record",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_event_concept_relation_concept_name",
                table: "event_concept_relation",
                column: "concept_name");

            migrationBuilder.CreateIndex(
                name: "IX_event_concept_relation_event_id_concept_name",
                table: "event_concept_relation",
                columns: new[] { "event_id", "concept_name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_stock_relation_event_id_stock_code",
                table: "event_stock_relation",
                columns: new[] { "event_id", "stock_code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_event_stock_relation_stock_code",
                table: "event_stock_relation",
                column: "stock_code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_concept_relation");

            migrationBuilder.DropTable(
                name: "event_stock_relation");
        }
    }
}
