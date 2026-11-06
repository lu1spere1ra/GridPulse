using Dapper;
using GridPulse.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace GridPulse.IntegrationTests.Database;

public sealed class SchemaTests(CommunityDatabase community, ApacheDatabase apache) : DatabaseTestBase(community, apache)
{
    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Readings_is_a_hypertable_and_rollups_exist(TimescaleEdition edition)
    {
        await using var connection = await Db(edition).DataSource.OpenConnectionAsync(Ct);

        var hypertables = await connection.QueryAsync<string>("SELECT hypertable_name FROM timescaledb_information.hypertables");
        hypertables.ShouldContain("readings");

        foreach (var relation in new[] { "readings_1m", "readings_15m", "readings_1h" })
        {
            (await connection.ExecuteScalarAsync<bool>("SELECT to_regclass(@relation) IS NOT NULL", new { relation }))
                .ShouldBeTrue(relation);
        }
    }

    [Fact]
    public async Task Community_edition_uses_continuous_aggregates_with_policies()
    {
        await using var connection = await Db(TimescaleEdition.Community).DataSource.OpenConnectionAsync(Ct);

        var aggregates = await connection.QueryAsync<string>("SELECT view_name FROM timescaledb_information.continuous_aggregates");
        aggregates.ShouldBe(["readings_1m", "readings_15m", "readings_1h"], ignoreOrder: true);

        var jobs = await connection.QueryAsync<string>("SELECT proc_name FROM timescaledb_information.jobs WHERE hypertable_name IS NOT NULL OR proc_name LIKE 'policy%'");
        jobs.ShouldContain("policy_compression");
        jobs.ShouldContain("policy_retention");
        jobs.Count(j => j == "policy_refresh_continuous_aggregate").ShouldBe(3);
    }

    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Timescale_setup_is_idempotent(TimescaleEdition edition)
    {
        var detected = await TimescaleSetup.ApplyAsync(Db(edition).DataSource, NullLogger.Instance, Ct);

        detected.ShouldBe(edition);
    }

    [Theory]
    [InlineData(TimescaleEdition.Community)]
    [InlineData(TimescaleEdition.Apache)]
    public async Task Default_alert_rules_are_seeded(TimescaleEdition edition)
    {
        await using var db = Db(edition).CreateDbContext();

        db.AlertRules.Count().ShouldBe(2);
    }
}
