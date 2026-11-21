using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddGridDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "grid_definitions",
                schema: "scada",
                columns: table => new
                {
                    version = table.Column<int>(type: "integer", nullable: false),
                    definition = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    comment = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_grid_definitions", x => x.version);
                });

            migrationBuilder.CreateIndex(
                name: "IX_grid_definitions_active",
                schema: "scada",
                table: "grid_definitions",
                column: "active",
                unique: true,
                filter: "active = TRUE");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "grid_definitions",
                schema: "scada");
        }
    }
}
