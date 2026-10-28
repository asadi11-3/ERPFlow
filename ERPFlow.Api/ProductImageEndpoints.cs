using ERPFlow.Application.ProductImages;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace ERPFlow.Api;

public sealed record ProductImageEndpointMetadata(bool Upload = false);

public static class ProductImageEndpoints
{
    public const string WritePolicy = "ProductImageAdministratorBearer";

    public static void MapProductImageEndpoints(this RouteGroupBuilder api)
    {
        var images = api.MapGroup("/products/{id:guid}/image").WithTags("Product Images");
        images.MapPut("", PutAsync)
            .WithMetadata(new ProductImageEndpointMetadata(Upload: true))
            .RequireAuthorization(WritePolicy)
            .DisableAntiforgery()
            .WithDescription("Bearer-only administrator upload. Cookie authentication is not accepted, so this endpoint does not use antiforgery tokens. Exactly one multipart file field named file; maximum 5 MiB file and 6 MiB request.")
            .Accepts<IFormFile>("multipart/form-data")
            .Produces<ProductImageMetadata>(201).Produces<ProductImageMetadata>()
            .ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404)
            .ProducesProblem(412).ProducesProblem(413).ProducesProblem(415).ProducesProblem(428)
            .ProducesProblem(503)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, OpenApiMediaType>
                    {
                        ["multipart/form-data"] = new()
                        {
                            Schema = new OpenApiSchema
                            {
                                Type = JsonSchemaType.Object, Required = new HashSet<string> { "file" },
                                AdditionalPropertiesAllowed = false,
                                Properties = new Dictionary<string, IOpenApiSchema>
                                {
                                    ["file"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" }
                                }
                            }
                        }
                    }
                };
                WriteParameters(operation, delete: false);
                ResponseHeaders(operation);
                return Task.CompletedTask;
            });
        images.MapDelete("", DeleteAsync)
            .WithMetadata(new ProductImageEndpointMetadata())
            .RequireAuthorization(WritePolicy)
            .Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403)
            .ProducesProblem(404).ProducesProblem(412).ProducesProblem(428)
            .ProducesProblem(503)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                WriteParameters(operation, delete: true);
                ResponseHeaders(operation);
                return Task.CompletedTask;
            });
        images.MapGet("", GetAsync)
            .WithMetadata(new ProductImageEndpointMetadata())
            .RequireAuthorization()
            .Produces(200, contentType: "image/jpeg", additionalContentTypes: ["image/png", "image/webp"])
            .Produces(304).ProducesProblem(400).ProducesProblem(401).ProducesProblem(404)
            .ProducesProblem(503)
            .AddOpenApiOperationTransformer((operation, _, _) =>
            {
                operation.Parameters ??= [];
                operation.Parameters.Add(new OpenApiParameter
                {
                    Name = "If-None-Match", In = ParameterLocation.Header,
                    Description = "A matching image ETag (weak comparison) or * returns 304.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                });
                if (operation.Responses is not null && operation.Responses.TryGetValue("200", out var response) && response.Content is not null)
                    foreach (var media in response.Content.Values)
                        media.Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" };
                ResponseHeaders(operation);
                return Task.CompletedTask;
            });
    }

    private static async Task<IResult> PutAsync(Guid id, IFormFile file, HttpContext http,
        IProductImageWorkflow workflow, CancellationToken ct)
    {
        var condition = WriteCondition(http.Request, delete: false);
        await using var source = file.OpenReadStream();
        var result = await workflow.PutAsync(id, source, file.FileName, file.ContentType, condition, ct);
        http.Response.Headers.ETag = result.Image.Version;
        return result.Created ? Results.Created($"/api/products/{id}/image", result.Image) : Results.Ok(result.Image);
    }

    private static async Task<IResult> DeleteAsync(Guid id, HttpContext http, IProductImageWorkflow workflow, CancellationToken ct)
    {
        await workflow.DeleteAsync(id, WriteCondition(http.Request, delete: true), ct);
        return Results.NoContent();
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, IProductImageWorkflow workflow, CancellationToken ct)
    {
        IList<EntityTagHeaderValue>? validators = null;
        if (http.Request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var raw) &&
            (!EntityTagHeaderValue.TryParseStrictList(raw.Select(value => value ?? "").ToArray(), out validators) || validators.Count == 0))
            throw new ProductImageException(ProductImageError.InvalidImage, "Invalid If-None-Match header.");
        var download = await workflow.GetAsync(id, ct);
        http.Response.Headers.ETag = download.Image.Version;
        var current = EntityTagHeaderValue.Parse(download.Image.Version);
        if (validators is not null && validators.Any(value => value == EntityTagHeaderValue.Any || value.Compare(current, useStrongComparison: false)))
            return Results.StatusCode(StatusCodes.Status304NotModified);
        return Results.File(download.Bytes, download.Image.ContentType);
    }

    internal static ProductImageCondition WriteCondition(HttpRequest request, bool delete)
    {
        var hasMatch = request.Headers.TryGetValue(HeaderNames.IfMatch, out var match);
        var hasNone = request.Headers.TryGetValue(HeaderNames.IfNoneMatch, out var none);
        if ((hasMatch && hasNone) || (hasMatch && (match.Count != 1 || string.IsNullOrWhiteSpace(match[0]))) ||
            (hasNone && (delete || none.Count != 1 || none[0]?.Trim() != "*")))
            throw new ProductImageException(ProductImageError.InvalidImage, "Invalid or conflicting image preconditions.");
        var condition = new ProductImageCondition(hasMatch ? match[0]!.Trim() : null, hasNone);
        ProductImageVersions.Validate(condition);
        return condition;
    }

    private static void WriteParameters(OpenApiOperation operation, bool delete)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-Match", In = ParameterLocation.Header, Required = delete,
            Description = "Exact strong image ETag. Required when replacing or deleting an existing image; stale versions return 412, omitted versions return 428.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });
        if (!delete) operation.Parameters.Add(new OpenApiParameter
        {
            Name = "If-None-Match", In = ParameterLocation.Header,
            Description = "Only * is accepted, for create-only upload. Cannot be combined with If-Match.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String, Enum = [System.Text.Json.Nodes.JsonValue.Create("*")!] }
        });
    }

    private static void ResponseHeaders(OpenApiOperation operation)
    {
        if (operation.Responses is null) return;
        foreach (var response in operation.Responses)
        {
            if (response.Value is not OpenApiResponse concrete) continue;
            concrete.Headers ??= new Dictionary<string, IOpenApiHeader>();
            concrete.Headers["Cache-Control"] = Header("private, no-cache");
            concrete.Headers["X-Content-Type-Options"] = Header("nosniff");
            if (response.Key is "200" or "201" or "304") concrete.Headers["ETag"] = Header("Strong opaque image rowversion validator.");
            if (response.Key == "201") concrete.Headers["Location"] = Header("Authenticated product image URL.");
        }
    }

    private static OpenApiHeader Header(string description) => new()
    {
        Description = description, Schema = new OpenApiSchema { Type = JsonSchemaType.String }
    };
}
