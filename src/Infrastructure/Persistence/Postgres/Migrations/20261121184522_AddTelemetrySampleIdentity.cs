using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetrySampleIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "sample_id",
                schema: "scada",
                table: "telemetry",
                type: "uuid",
                nullable: false,
                defaultValueSql: "gen_random_uuid()");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_sample_id",
                schema: "scada",
                table: "telemetry",
                column: "sample_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_telemetry_sample_id",
                schema: "scada",
                table: "telemetry");

            migrationBuilder.DropColumn(
                name: "sample_id",
                schema: "scada",
                table: "telemetry");
        }
    }
}
