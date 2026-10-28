using System.Net;
using System.Text.Json;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageOpenApiTests(ProductImageFixture fixture) : IClassFixture<ProductImageFixture>
{
    [Fact]
    public async Task ProductImageOperationsDeclareBearerSecurityAndContractStatuses()
    {
        using var response = await fixture.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var path = document.RootElement.GetProperty("paths").GetProperty("/api/products/{id}/image");

        AssertOperation(path.GetProperty("put"), "200", "201", "400", "401", "403", "404", "412", "413", "415", "428");
        AssertOperation(path.GetProperty("get"), "200", "304", "401", "404");
        AssertOperation(path.GetProperty("delete"), "204", "401", "403", "404", "412", "428");
    }

    [Fact]
    public async Task UploadIsRequiredSingleBinaryMultipartFile()
    {
        using var response = await fixture.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var upload = document.RootElement.GetProperty("paths")
            .GetProperty("/api/products/{id}/image")
            .GetProperty("put")
            .GetProperty("requestBody");

        Assert.True(upload.GetProperty("required").GetBoolean());
        var content = upload.GetProperty("content");
        Assert.Single(content.EnumerateObject());
        var schema = content.GetProperty("multipart/form-data").GetProperty("schema");
        Assert.Equal("object", schema.GetProperty("type").GetString());
        Assert.Equal(["file"], schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()!).ToArray());
        var properties = schema.GetProperty("properties");
        Assert.Single(properties.EnumerateObject());
        var file = properties.GetProperty("file");
        Assert.Equal("string", file.GetProperty("type").GetString());
        Assert.Equal("binary", file.GetProperty("format").GetString());
    }

    private static void AssertOperation(JsonElement operation, params string[] statuses)
    {
        Assert.Contains(operation.GetProperty("security").EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));
        var responses = operation.GetProperty("responses");
        foreach (var status in statuses)
            Assert.True(responses.TryGetProperty(status, out _), $"Expected documented {status} response.");
    }
}
