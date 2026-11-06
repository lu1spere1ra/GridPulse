using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GridPulse.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS timescaledb;");

            migrationBuilder.CreateTable(
                name: "meters",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    serial = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_meters", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "readings",
                columns: table => new
                {
                    time = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    meter_id = table.Column<int>(type: "integer", nullable: false),
                    power_w = table.Column<float>(type: "real", nullable: false),
                    energy_wh = table.Column<long>(type: "bigint", nullable: false),
                    voltage_v = table.Column<float>(type: "real", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_readings", x => new { x.meter_id, x.time });
                });

            // Hypertables are part of TimescaleDB's Apache-2 edition, so this runs everywhere
            // (including Azure Flexible Server). Edition-specific objects (continuous aggregates,
            // compression, retention) are applied by TimescaleSetup after migrations.
            migrationBuilder.Sql("SELECT create_hypertable('readings', by_range('time', INTERVAL '1 hour'));");

            migrationBuilder.CreateTable(
                name: "alert_rules",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    type = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    meter_id = table.Column<int>(type: "integer", nullable: true),
                    threshold_w = table.Column<double>(type: "double precision", nullable: true),
                    aggregate = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    offline_minutes = table.Column<int>(type: "integer", nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alert_rules", x => x.id);
                    table.ForeignKey(
                        name: "fk_alert_rules_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "alerts",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    rule_id = table.Column<int>(type: "integer", nullable: false),
                    meter_id = table.Column<int>(type: "integer", nullable: false),
                    state = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    acknowledged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    message = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                    table.ForeignKey(
                        name: "fk_alerts_alert_rules_rule_id",
                        column: x => x.rule_id,
                        principalTable: "alert_rules",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_alerts_meters_meter_id",
                        column: x => x.meter_id,
                        principalTable: "meters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alert_rules_meter_id",
                table: "alert_rules",
                column: "meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_meter_id",
                table: "alerts",
                column: "meter_id");

            migrationBuilder.CreateIndex(
                name: "ix_alerts_raised_at",
                table: "alerts",
                column: "raised_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ix_alerts_rule_id_meter_id",
                table: "alerts",
                columns: new[] { "rule_id", "meter_id" },
                unique: true,
                filter: "state = 'Firing'");

            migrationBuilder.CreateIndex(
                name: "ix_meters_serial",
                table: "meters",
                column: "serial",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts");

            migrationBuilder.DropTable(
                name: "readings");

            migrationBuilder.DropTable(
                name: "alert_rules");

            migrationBuilder.DropTable(
                name: "meters");
        }
    }
}
