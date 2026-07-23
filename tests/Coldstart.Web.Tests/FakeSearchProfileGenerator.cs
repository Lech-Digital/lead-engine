using Coldstart.Application.Customers;

namespace Coldstart.Web.Tests;

// Returns a canned profile so invite tests can assert what gets stored, with no HTTP.
public sealed class FakeSearchProfileGenerator : ISearchProfileGenerator
{
    public SearchProfile? Result { get; set; } =
        new("scheduling software", "dentist\nclinic", "hiring\nexpanding", "hospital groups");

    public SearchProfileRequest? LastRequest { get; private set; }

    public Task<SearchProfile?> GenerateAsync(SearchProfileRequest request, CancellationToken ct)
    {
        LastRequest = request;
        return Task.FromResult(Result);
    }
}
