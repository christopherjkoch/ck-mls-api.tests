using ck_mls_api.Models;

namespace ck_mls_api.tests;

internal sealed class FixedTimeProvider(DateOnly today) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => new(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero);
}

internal static class TestListings
{
    public static Listing Make(
        string id,
        decimal price = 500_000,
        int bedrooms = 2,
        string city = "Springfield",
        DateOnly? listedDate = null,
        string description = "Nice home.") => new()
    {
        Id = id,
        Source = "MLS_T",
        Address = $"{id} Test St",
        City = city,
        State = "VA",
        Zip = "22150",
        Price = price,
        Bedrooms = bedrooms,
        Bathrooms = 1,
        Sqft = 1000,
        ListedDate = listedDate ?? new DateOnly(2026, 10, 20),
        Status = "active",
        Description = description,
    };
}
