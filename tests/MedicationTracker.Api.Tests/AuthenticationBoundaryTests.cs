using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace MedicationTracker.Api.Tests;

public sealed class AuthenticationBoundaryTests
{
    [Fact]
    public async Task Supplying_an_account_id_never_authenticates_a_request()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Account-Id", Guid.NewGuid().ToString());
        var household = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/households/{household}/workspace")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"/api/households/{household}/today")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Medication-Client", "1");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync($"/api/households/{household}/people", new { name = "Synthetic" })).StatusCode);
    }

    [Fact]
    public async Task Cross_origin_simple_mutations_are_rejected_before_credentials_are_read()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "https://attacker.example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/login", new { email = "synthetic@example.invalid", password = "synthetic-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/auth/logout", new { })).StatusCode);
    }
}
