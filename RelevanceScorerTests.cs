using ck_mls_api.Services.Search;

namespace ck_mls_api.tests;

public class RelevanceScorerTests
{
    private static readonly DateOnly Today = new(2026, 10, 20);

    [Theory]
    [InlineData(500_000, 100)] // exact match
    [InlineData(450_000, 90)]  // 10% under
    [InlineData(550_000, 90)]  // 10% over: same penalty as under
    [InlineData(375_000, 75)]  // 25% under
    public void BudgetScore_LosesOnePointPerPercentAway(decimal price, double expected) =>
        Assert.Equal(expected, RelevanceScorer.BudgetScore(price, 500_000), 6);

    [Theory]
    [InlineData(1_000_000)] // 100% over
    [InlineData(5_000_000)]
    [InlineData(0)]         // 100% under
    public void BudgetScore_FloorsAtZero(decimal price) =>
        Assert.Equal(0, RelevanceScorer.BudgetScore(price, 500_000), 6);

    [Fact]
    public void BudgetScore_NonPositiveTarget_Throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => RelevanceScorer.BudgetScore(1, 0));

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 99)]
    [InlineData(30, 70)]
    [InlineData(100, 0)]
    [InlineData(365, 0)]
    public void RecencyScore_LosesOnePointPerDay(int daysOld, double expected) =>
        Assert.Equal(expected, RelevanceScorer.RecencyScore(Today.AddDays(-daysOld), Today));

    [Fact]
    public void RecencyScore_FutureDate_CountsAsToday() =>
        Assert.Equal(100, RelevanceScorer.RecencyScore(Today.AddDays(5), Today));

    [Fact]
    public void Score_WithoutBudget_IsRecencyOnly()
    {
        var score = RelevanceScorer.Score(999_999, Today.AddDays(-30), null, Today);

        Assert.Null(score.Budget);
        Assert.Equal(70, score.Recency);
        Assert.Equal(70, score.Total);
    }

    [Fact]
    public void Score_WithBudget_IsTheAverageOfBothScores()
    {
        // 10% under budget => 90; listed 30 days ago => 70; average => 80
        var score = RelevanceScorer.Score(450_000, Today.AddDays(-30), 500_000, Today);

        Assert.Equal(90, score.Budget);
        Assert.Equal(70, score.Recency);
        Assert.Equal(80, score.Total);
    }

    [Fact]
    public void Score_IsRoundedToTwoDecimals()
    {
        var score = RelevanceScorer.Score(333_333, Today, 500_000, Today);
        Assert.Equal(Math.Round(score.Total, 2), score.Total);
    }
}
