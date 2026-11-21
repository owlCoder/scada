using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Persistence.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "scada");

            migrationBuilder.CreateSequence(
                name: "incident_number_seq",
                schema: "scada");

            migrationBuilder.CreateTable(
                name: "alarm_history",
                schema: "scada",
                columns: table => new
                {
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    alarm_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    source_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    transition = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_alarm_history", x => x.position);
                });

            migrationBuilder.CreateTable(
                name: "detection_rule_settings",
                schema: "scada",
                columns: table => new
                {
                    rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    threshold = table.Column<double>(type: "double precision", nullable: false),
                    window_seconds = table.Column<int>(type: "integer", nullable: false),
                    cooldown_seconds = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detection_rule_settings", x => x.rule_id);
                });

            migrationBuilder.CreateTable(
                name: "detections",
                schema: "scada",
                columns: table => new
                {
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    rule_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    rule_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    scenario_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fingerprint = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_detections", x => x.position);
                });

            migrationBuilder.CreateTable(
                name: "events",
                schema: "scada",
                columns: table => new
                {
                    position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    severity = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    scenario_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_events", x => x.position);
                });

            migrationBuilder.CreateTable(
                name: "incidents",
                schema: "scada",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('scada.incident_number_seq')"),
                    title = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    severity = table.Column<int>(type: "integer", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    assigned_to = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    acknowledged_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution_notes = table.Column<string>(type: "text", nullable: true),
                    device_ids = table.Column<List<string>>(type: "text[]", nullable: false),
                    device_keys = table.Column<List<string>>(type: "text[]", nullable: false),
                    scenario_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    correlation_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incidents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "plant_state",
                schema: "scada",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    is_paused = table.Column<bool>(type: "boolean", nullable: false),
                    plant = table.Column<string>(type: "jsonb", nullable: false),
                    controllers = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plant_state", x => x.id);
                    table.CheckConstraint("ck_plant_state_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "scenario_runs",
                schema: "scada",
                columns: table => new
                {
                    run_id = table.Column<Guid>(type: "uuid", nullable: false),
                    scenario_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    scenario_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    category = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    started_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    simulation_start = table.Column<TimeSpan>(type: "interval", nullable: false),
                    elapsed = table.Column<TimeSpan>(type: "interval", nullable: false),
                    duration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    progress_percent = table.Column<double>(type: "double precision", nullable: false),
                    actions = table.Column<string>(type: "jsonb", nullable: false),
                    measurements = table.Column<string>(type: "jsonb", nullable: false),
                    expected_detections = table.Column<string>(type: "jsonb", nullable: false),
                    observed_detections = table.Column<string>(type: "jsonb", nullable: false),
                    outcome = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    replay_of = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_scenario_runs", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "telemetry",
                schema: "scada",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    simulation_time = table.Column<TimeSpan>(type: "interval", nullable: false),
                    tag_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<double>(type: "double precision", nullable: false),
                    quality = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    hash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_telemetry", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_accounts",
                schema: "scada",
                columns: table => new
                {
                    username_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    username = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    display_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    api_key_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    password_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    locked_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_accounts", x => x.username_key);
                });

            migrationBuilder.CreateTable(
                name: "incident_comments",
                schema: "scada",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_comments", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_comments_incidents_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "scada",
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "incident_timeline",
                schema: "scada",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    incident_id = table.Column<Guid>(type: "uuid", nullable: false),
                    timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    description = table.Column<string>(type: "text", nullable: false),
                    from_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    to_status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_incident_timeline", x => x.id);
                    table.ForeignKey(
                        name: "FK_incident_timeline_incidents_incident_id",
                        column: x => x.incident_id,
                        principalSchema: "scada",
                        principalTable: "incidents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_alarm_history_id",
                schema: "scada",
                table: "alarm_history",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_alarm_history_timestamp",
                schema: "scada",
                table: "alarm_history",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_detections_id",
                schema: "scada",
                table: "detections",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_detections_rule_id",
                schema: "scada",
                table: "detections",
                column: "rule_id");

            migrationBuilder.CreateIndex(
                name: "IX_detections_scenario_run_id",
                schema: "scada",
                table: "detections",
                column: "scenario_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_detections_timestamp",
                schema: "scada",
                table: "detections",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_events_category",
                schema: "scada",
                table: "events",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "IX_events_id",
                schema: "scada",
                table: "events",
                column: "id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_events_scenario_run_id",
                schema: "scada",
                table: "events",
                column: "scenario_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_events_timestamp",
                schema: "scada",
                table: "events",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_incident_comments_incident_id_created_at",
                schema: "scada",
                table: "incident_comments",
                columns: new[] { "incident_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_incident_timeline_incident_id_timestamp",
                schema: "scada",
                table: "incident_timeline",
                columns: new[] { "incident_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_incidents_correlation_key",
                schema: "scada",
                table: "incidents",
                column: "correlation_key");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_created_at",
                schema: "scada",
                table: "incidents",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_number",
                schema: "scada",
                table: "incidents",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_incidents_scenario_run_id",
                schema: "scada",
                table: "incidents",
                column: "scenario_run_id");

            migrationBuilder.CreateIndex(
                name: "IX_incidents_status",
                schema: "scada",
                table: "incidents",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_scenario_runs_started_at",
                schema: "scada",
                table: "scenario_runs",
                column: "started_at");

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_tag_id_timestamp",
                schema: "scada",
                table: "telemetry",
                columns: new[] { "tag_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "IX_telemetry_timestamp",
                schema: "scada",
                table: "telemetry",
                column: "timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_user_accounts_api_key_hash",
                schema: "scada",
                table: "user_accounts",
                column: "api_key_hash");

            migrationBuilder.Sql(LegacyDataImport.Sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alarm_history",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "detection_rule_settings",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "detections",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "events",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "incident_comments",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "incident_timeline",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "plant_state",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "scenario_runs",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "telemetry",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "user_accounts",
                schema: "scada");

            migrationBuilder.DropTable(
                name: "incidents",
                schema: "scada");

            migrationBuilder.DropSequence(
                name: "incident_number_seq",
                schema: "scada");
        }
    }
}
