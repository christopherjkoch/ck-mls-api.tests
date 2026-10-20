using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using ck_mls_api.Services.Search;

namespace ck_mls_api.tests;

/// <summary>End-to-end tests through HTTP against the seeded sample dataset.</summary>
public class ApiTests(WebApplicationFactory<Program> factory) : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Search_Defaults_ReturnsFirstPageOfSeededListingsRanked()
    {
        var result = await _client.GetFromJsonAsync<SearchResponse>("/api/listings/search");

        Assert.NotNull(result);
        Assert.Equal(12, result.TotalCount);
        Assert.Equal(10, result.Items.Count);
        Assert.Equal(2, result.TotalPages);
        Assert.Equal(result.Items.OrderByDescending(i => i.RelevanceScore).Select(i => i.RelevanceScore),
                     result.Items.Select(i => i.RelevanceScore));
    }

    [Theory]
    [InlineData("minPrice=600000&maxPrice=500000", "minPrice")]
    [InlineData("pageSize=0", "pageSize")]
    [InlineData("page=0", "page")]
    [InlineData("page=99", "page")]
    [InlineData("city=Atlantis", "city")]
    [InlineData("targetBudget=-1", "targetBudget")]
    public async Task Search_InvalidInput_Returns400ProblemDetails(string query, string errorKey)
    {
        var response = await _client.GetAsync($"/api/listings/search?{query}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("errors").TryGetProperty(errorKey, out _));
    }

    [Fact]
    public async Task Cities_ReturnsSeededCities()
    {
        var cities = await _client.GetFromJsonAsync<string[]>("/api/listings/cities");
        Assert.Equal(["Chantilly", "Fairfax", "Manassas", "Reston", "Springfield", "Vienna"], cities!);
    }

    [Fact]
    public async Task Cors_AllowsConfiguredFrontendOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/listings/cities");
        request.Headers.Add("Origin", "http://localhost:5173");

        var response = await _client.SendAsync(request);

        Assert.Equal("http://localhost:5173", Assert.Single(response.Headers.GetValues("Access-Control-Allow-Origin")));
    }

    [Fact]
    public async Task Cors_DoesNotAllowUnknownOrigin()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/listings/cities");
        request.Headers.Add("Origin", "http://evil.example");

        var response = await _client.SendAsync(request);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }
}
