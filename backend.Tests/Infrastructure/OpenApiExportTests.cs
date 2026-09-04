using System.Net;
using FluentAssertions;
using MEval.Api.Tests.Infrastructure;
using Xunit;

namespace MEval.Api.Tests.Infrastructure;

public class OpenApiExportTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OpenApiExportTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ExportOpenApiSpecification_ShouldBeValidAndSaveToJsonFile()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().Contain("\"openapi\":");

        // Write to backend/openapi.json
        var backendDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "backend"));
        var outputPath = Path.Combine(backendDir, "openapi.json");
        await File.WriteAllTextAsync(outputPath, json);

        File.Exists(outputPath).Should().BeTrue();
    }

    [Fact]
    public async Task ScalarApiReference_ShouldReturn200Ok()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/scalar/v1");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("scalar");
    }
}
