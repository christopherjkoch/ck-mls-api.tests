using Microsoft.EntityFrameworkCore;
using ck_mls_api.Data;
using ck_mls_api.Models;
using ck_mls_api.Services.Search;
using static ck_mls_api.tests.TestListings;

namespace ck_mls_api.tests;

public class ListingSearchServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    private static ListingSearchService CreateService(params Listing[] listings)
    {
        var options = new DbContextOptionsBuilder<ListingsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new ListingsDbContext(options);
        db.Listings.AddRange(listings);
        db.SaveChanges();
        return new ListingSearchService(db, new FixedTimeProvider(Today));
    }

    private static async Task<SearchResponse> SearchOk(ListingSearchService service, ListingSearchQuery query)
    {
        var outcome = await service.SearchAsync(query);
        Assert.True(outcome.IsValid, string.Join("; ", outcome.Errors?.SelectMany(e => e.Value) ?? []));
        return outcome.Response!;
    }

    private static IEnumerable<string> Ids(SearchResponse r) => r.Items.Select(i => i.Id);

    // ---- Filtering ----

    [Fact]
    public async Task PriceRange_IsInclusiveOnBothEnds()
    {
        var service = CreateService(Make("lo", price: 100), Make("min", price: 200), Make("max", price: 300), Make("hi", price: 400));

        var result = await SearchOk(service, new() { MinPrice = 200, MaxPrice = 300 });

        Assert.Equal(["max", "min"], Ids(result).Order());
    }

    [Fact]
    public async Task MinBedrooms_FiltersFewerBedrooms()
    {
        var service = CreateService(Make("one", bedrooms: 1), Make("three", bedrooms: 3), Make("four", bedrooms: 4));

        var result = await SearchOk(service, new() { MinBedrooms = 3 });

        Assert.Equal(["four", "three"], Ids(result).Order());
    }

    [Fact]
    public async Task City_IsCaseInsensitiveAndTrimmed()
    {
        var service = CreateService(Make("s", city: "Springfield"), Make("f", city: "Fairfax"));

        var result = await SearchOk(service, new() { City = "  fAIRFAX " });

        Assert.Equal(["f"], Ids(result));
    }

    [Fact]
    public async Task Keyword_IsCaseInsensitive_AndRequiresAllWords()
    {
        var service = CreateService(
            Make("both", description: "Bright condo, PET friendly."),
            Make("pet-only", description: "Pet allowed condo."),
            Make("none", description: "Quiet house."));

        var result = await SearchOk(service, new() { Keyword = "pet  FRIENDLY" });

        Assert.Equal(["both"], Ids(result));
    }

    [Fact]
    public async Task AllFilters_CombineWithAnd()
    {
        var service = CreateService(
            Make("match", price: 450_000, bedrooms: 3, city: "Fairfax", description: "has a pool"),
            Make("wrong-city", price: 450_000, bedrooms: 3, city: "Springfield", description: "has a pool"),
            Make("no-pool", price: 450_000, bedrooms: 3, city: "Fairfax", description: "has a deck"),
            Make("too-small", price: 450_000, bedrooms: 1, city: "Fairfax", description: "has a pool"));

        var result = await SearchOk(service, new() { MinPrice = 400_000, MaxPrice = 500_000, MinBedrooms = 2, City = "Fairfax", Keyword = "pool" });

        Assert.Equal(["match"], Ids(result));
    }

    // ---- No matches / invalid input ----

    [Fact]
    public async Task UnknownCity_IsAnErrorNotAnEmptyPage()
    {
        var service = CreateService(Make("a", city: "Springfield"));

        var outcome = await service.SearchAsync(new() { City = "Atlantis" });

        Assert.False(outcome.IsValid);
        Assert.Contains("Atlantis", Assert.Single(outcome.Errors!["city"]));
    }

    [Fact]
    public async Task KnownCity_WithNoMatchesForOtherFilters_ReturnsEmptySuccess()
    {
        var service = CreateService(Make("a", city: "Springfield", price: 300_000));

        var result = await SearchOk(service, new() { City = "Springfield", MinPrice = 1_000_000 });

        Assert.Empty(result.Items);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
        Assert.False(result.HasNext);
        Assert.False(result.HasPrevious);
    }

    [Fact]
    public async Task KeywordWithNoMatches_ReturnsEmptySuccess()
    {
        var service = CreateService(Make("a"), Make("b"));

        var result = await SearchOk(service, new() { Keyword = "helipad" });

        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task EmptyDataset_FirstPageIsValidAndEmpty()
    {
        var result = await SearchOk(CreateService(), new());
        Assert.Equal(0, result.TotalCount);
    }

    [Fact]
    public async Task InvalidQuery_ReturnsValidatorErrors()
    {
        var outcome = await CreateService(Make("a")).SearchAsync(new() { MinPrice = 500, MaxPrice = 100, PageSize = 0 });

        Assert.False(outcome.IsValid);
        Assert.Null(outcome.Response);
        Assert.True(outcome.Errors!.ContainsKey("minPrice"));
        Assert.True(outcome.Errors!.ContainsKey("pageSize"));
    }

    // ---- Ranking ----

    [Fact]
    public async Task WithBudget_CloserPriceRanksHigher()
    {
        var service = CreateService(
            Make("exact", price: 500_000),
            Make("off5", price: 525_000),
            Make("off20", price: 400_000),
            Make("far", price: 900_000));

        var result = await SearchOk(service, new() { TargetBudget = 500_000 });

        Assert.Equal(["exact", "off5", "off20", "far"], Ids(result));
        Assert.True(result.Items.Zip(result.Items.Skip(1)).All(p => p.First.RelevanceScore >= p.Second.RelevanceScore));
    }

    [Fact]
    public async Task WithoutBudget_NewestRanksFirst()
    {
        var service = CreateService(
            Make("old", listedDate: Today.AddDays(-90)),
            Make("new", listedDate: Today),
            Make("mid", listedDate: Today.AddDays(-10)));

        var result = await SearchOk(service, new());

        Assert.Equal(["new", "mid", "old"], Ids(result));
        Assert.All(result.Items, i => Assert.Null(i.BudgetScore));
    }

    [Fact]
    public async Task RecencyCanOutweighASmallBudgetDifference()
    {
        var service = CreateService(
            Make("stale-exact", price: 500_000, listedDate: Today.AddDays(-120)),
            Make("fresh-close", price: 480_000, listedDate: Today));

        var result = await SearchOk(service, new() { TargetBudget = 500_000 });

        Assert.Equal(["fresh-close", "stale-exact"], Ids(result));
    }

    [Fact]
    public async Task TiedScores_SameListing_AreOrderedById()
    {
        var service = CreateService(Make("C"), Make("A"), Make("B"));

        var result = await SearchOk(service, new() { TargetBudget = 500_000 });

        Assert.Single(result.Items.Select(i => i.RelevanceScore).Distinct());
        Assert.Equal(["A", "B", "C"], Ids(result));
    }

    [Fact]
    public async Task TiedScores_NewerListingFirst()
    {
        // exact price, 10 days old => (100 + 90) / 2 = 95
        // 10% off budget, today     => (90 + 100) / 2 = 95
        var service = CreateService(
            Make("older", price: 500_000, listedDate: Today.AddDays(-10)),
            Make("newer", price: 450_000, listedDate: Today));

        var result = await SearchOk(service, new() { TargetBudget = 500_000 });

        Assert.Equal(result.Items[0].RelevanceScore, result.Items[1].RelevanceScore);
        Assert.Equal(["newer", "older"], Ids(result));
    }

    [Fact]
    public async Task Ranking_IsStableAcrossRepeatedQueries()
    {
        var service = CreateService(Enumerable.Range(0, 20).Select(i => Make($"L{i:D2}")).ToArray());
        var query = new ListingSearchQuery { PageSize = 20 };

        var first = Ids(await SearchOk(service, query)).ToList();
        var second = Ids(await SearchOk(service, query)).ToList();

        Assert.Equal(first, second);
    }

    // ---- Pagination ----

    private static ListingSearchService FiveListings() => CreateService(
        Enumerable.Range(1, 5).Select(i => Make($"L{i}", listedDate: Today.AddDays(-i))).ToArray());

    [Fact]
    public async Task FirstPage_HasNextButNoPrevious()
    {
        var result = await SearchOk(FiveListings(), new() { Page = 1, PageSize = 2 });

        Assert.Equal(["L1", "L2"], Ids(result));
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(3, result.TotalPages);
        Assert.False(result.HasPrevious);
        Assert.True(result.HasNext);
    }

    [Fact]
    public async Task LastPage_IsPartialAndHasNoNext()
    {
        var result = await SearchOk(FiveListings(), new() { Page = 3, PageSize = 2 });

        Assert.Equal(["L5"], Ids(result));
        Assert.True(result.HasPrevious);
        Assert.False(result.HasNext);
    }

    [Fact]
    public async Task PageSizeEqualToCount_IsSinglePage()
    {
        var result = await SearchOk(FiveListings(), new() { PageSize = 5 });

        Assert.Equal(1, result.TotalPages);
        Assert.Equal(5, result.Items.Count);
        Assert.False(result.HasNext);
    }

    [Fact]
    public async Task Pages_DoNotOverlap_AndCoverEveryResult()
    {
        var service = FiveListings();
        var seen = new List<string>();
        for (var page = 1; page <= 3; page++)
            seen.AddRange(Ids(await SearchOk(service, new() { Page = page, PageSize = 2 })));

        Assert.Equal(["L1", "L2", "L3", "L4", "L5"], seen);
    }

    [Fact]
    public async Task PageBeyondLast_IsAnError()
    {
        var outcome = await FiveListings().SearchAsync(new() { Page = 4, PageSize = 2 });

        Assert.False(outcome.IsValid);
        Assert.Contains("3 page(s)", Assert.Single(outcome.Errors!["page"]));
    }

    [Fact]
    public async Task HugePageNumber_IsAnErrorNotAnOverflow()
    {
        var outcome = await FiveListings().SearchAsync(new() { Page = int.MaxValue, PageSize = 100 });
        Assert.True(outcome.Errors!.ContainsKey("page"));
    }

    [Fact]
    public async Task PageTwo_OfEmptyResult_IsAnError()
    {
        var outcome = await FiveListings().SearchAsync(new() { Keyword = "nothing-matches", Page = 2 });
        Assert.True(outcome.Errors!.ContainsKey("page"));
    }

    // ---- Cities ----

    [Fact]
    public async Task GetCities_ReturnsDistinctSorted()
    {
        var service = CreateService(Make("1", city: "Fairfax"), Make("2", city: "Arlington"), Make("3", city: "Fairfax"));
        Assert.Equal(["Arlington", "Fairfax"], await service.GetCitiesAsync());
    }
}
