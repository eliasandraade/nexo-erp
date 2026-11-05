using System.Net;
using FluentAssertions;
using Nexo.IntegrationTests.Helpers;

namespace Nexo.IntegrationTests.TaxIntelligence;

/// <summary>
/// Load shape of the screens people open most: each list and the dashboard issue the same number of queries on the tax tables
/// whether the company has 2 or 12 rows (no N+1), and every list is paged by the server.
/// </summary>
[Collection(TaxIsolatedCollection.Name)]
public sealed class TaxQueryShapeTests : TaxIsolatedSuite
{
    public TaxQueryShapeTests(TestWebApplicationFactory factory) : base(factory) { }

    private static readonly string[] Screens =
    [
        "/api/tax/overview",
        "/api/tax/calculations?take=50",
        "/api/tax/opportunities?take=50",
        "/api/tax/credits?take=50",
        "/api/tax/split/expectations?take=50",
    ];

    private static async Task SeedAsync(HttpClient admin, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var calc = await Json(await Post(admin, "/api/tax/calculations", Operation(Unique("Carga"), gross: 3000m + i)));
            await Json(await Post(admin, $"/api/tax/calculations/{calc.GetProperty("id").GetGuid()}/split-expectations", new { procedure = "Standard" }));
            var key = "35261012345678000190550" + string.Concat(Guid.NewGuid().ToByteArray().Take(11).Select(b => (b % 100).ToString("D2")))[..21];
            await Json(await Post(admin, "/api/tax/credits", new
            {
                tax = "Cbs", sourceDocumentType = "NF-e", sourceDocumentKey = key, supplierDocument = (string?)null,
                acquiredOn = "2026-06-01", expiresOn = "2026-12-31", originalAmount = 100m, potentialAmount = 9m, potentialBasis = "Estimativa",
            }));
        }
    }

    private async Task<Dictionary<string, int>> MeasureAsync(HttpClient admin)
    {
        var counts = new Dictionary<string, int>();
        foreach (var url in Screens)
        {
            Factory.TaxQueries.Reset();
            var r = await admin.GetAsync(url);
            r.StatusCode.Should().Be(HttpStatusCode.OK, url);
            counts[url] = Factory.TaxQueries.Count;
        }
        return counts;
    }

    [Fact]
    public async Task Lists_and_the_dashboard_never_grow_their_queries_with_the_number_of_rows()
    {
        var admin = await AdminWithProfileAsync();
        await SeedAsync(admin, 2);
        var few = await MeasureAsync(admin);
        await SeedAsync(admin, 10);
        var many = await MeasureAsync(admin);

        foreach (var url in Screens)
        {
            few[url].Should().BeGreaterThan(0, $"{url} reads the tax tables");
            many[url].Should().Be(few[url], $"{url} must not issue one query per row (N+1)");
        }
    }

    [Fact]
    public async Task Every_list_is_paged_by_the_server_and_a_huge_page_is_capped()
    {
        var admin = await AdminWithProfileAsync();
        await SeedAsync(admin, 3);

        var page = await Json(await admin.GetAsync("/api/tax/calculations?skip=1&take=2"));
        page.GetProperty("items").GetArrayLength().Should().Be(2);
        page.GetProperty("total").GetInt32().Should().BeGreaterThanOrEqualTo(3);
        page.GetProperty("skip").GetInt32().Should().Be(1);

        foreach (var url in Screens.Skip(1))
        {
            var huge = await Json(await admin.GetAsync(url.Replace("take=50", "take=100000")));
            huge.GetProperty("take").GetInt32().Should().BeLessThanOrEqualTo(200, $"{url} caps the page size");
        }
    }
}
