using GridPulse.Core.Alerts;
using GridPulse.Core.Contracts;
using GridPulse.Web.Components.Pages;

namespace GridPulse.Web.Tests;

public sealed class AlertRulesPageTests : GridPulseTestContext
{
    public AlertRulesPageTests() =>
        Api.Respond("/api/v1/alert-rules", new List<AlertRuleDto>
        {
            new(1, "High consumption", AlertRuleType.Threshold, null, 7_000, ThresholdAggregate.Average, null, true),
            new(2, "Offline", AlertRuleType.Offline, 12, null, ThresholdAggregate.Average, 5, false),
        });

    [Fact]
    public void Lists_rules_in_plain_language()
    {
        var page = Render<AlertRules>();

        page.WaitForAssertion(() =>
        {
            page.Markup.ShouldContain("1-minute average above 7.00 kW");
            page.Markup.ShouldContain("No readings for 5 min");
            page.Markup.ShouldContain("Meter 12");
            page.Markup.ShouldContain("All meters");
        });
    }

    [Fact]
    public void A_new_rule_needs_a_name()
    {
        var page = Render<AlertRules>();
        page.WaitForAssertion(() => page.Markup.ShouldContain("High consumption"));

        page.FindAll("button").First(b => b.TextContent.Contains("New rule")).Click();
        page.Find("form").Submit();

        page.WaitForAssertion(() => page.Markup.ShouldContain("Give the rule a name."));
        Api.Requests.ShouldNotContain(r => r.StartsWith("POST", StringComparison.Ordinal));
    }
}
