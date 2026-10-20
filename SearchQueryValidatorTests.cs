using ck_mls_api.Services.Search;

namespace ck_mls_api.tests;

public class SearchQueryValidatorTests
{
    [Fact]
    public void Defaults_AreValid() =>
        Assert.Empty(SearchQueryValidator.Validate(new ListingSearchQuery()));

    [Fact]
    public void FullyPopulatedValidQuery_IsValid() =>
        Assert.Empty(SearchQueryValidator.Validate(new ListingSearchQuery
        {
            MinPrice = 100_000, MaxPrice = 100_000, MinBedrooms = 0, City = "Fairfax",
            Keyword = "pool", TargetBudget = 1, Page = 3, PageSize = SearchQueryValidator.MaxPageSize,
        }));

    [Fact]
    public void MinPriceGreaterThanMaxPrice_IsRejected()
    {
        var errors = SearchQueryValidator.Validate(new ListingSearchQuery { MinPrice = 600_000, MaxPrice = 500_000 });
        Assert.Contains("cannot be greater than maxPrice", Assert.Single(errors["minPrice"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(SearchQueryValidator.MaxPageSize + 1)]
    public void PageSizeOutOfRange_IsRejected(int pageSize) =>
        Assert.True(SearchQueryValidator.Validate(new ListingSearchQuery { PageSize = pageSize }).ContainsKey("pageSize"));

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void PageBelowOne_IsRejected(int page) =>
        Assert.True(SearchQueryValidator.Validate(new ListingSearchQuery { Page = page }).ContainsKey("page"));

    [Fact]
    public void NegativeValues_AreRejected()
    {
        var errors = SearchQueryValidator.Validate(new ListingSearchQuery { MinPrice = -1, MaxPrice = -1, MinBedrooms = -1 });

        Assert.True(errors.ContainsKey("minPrice"));
        Assert.True(errors.ContainsKey("maxPrice"));
        Assert.True(errors.ContainsKey("minBedrooms"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void NonPositiveTargetBudget_IsRejected(decimal budget) =>
        Assert.True(SearchQueryValidator.Validate(new ListingSearchQuery { TargetBudget = budget }).ContainsKey("targetBudget"));

    [Fact]
    public void OverlongText_IsRejected()
    {
        var tooLong = new string('x', SearchQueryValidator.MaxTextLength + 1);
        var errors = SearchQueryValidator.Validate(new ListingSearchQuery { City = tooLong, Keyword = tooLong });

        Assert.True(errors.ContainsKey("city"));
        Assert.True(errors.ContainsKey("keyword"));
    }

    [Fact]
    public void MultipleProblems_AreAllReported()
    {
        var errors = SearchQueryValidator.Validate(new ListingSearchQuery { MinPrice = 10, MaxPrice = 5, PageSize = 0, Page = 0 });
        Assert.Equal(["minPrice", "page", "pageSize"], errors.Keys.Order());
    }
}
