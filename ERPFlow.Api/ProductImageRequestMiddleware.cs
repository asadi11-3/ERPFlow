using ERPFlow.Application.ProductImages;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace ERPFlow.Api;

public sealed class ProductImageRequestMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext http)
    {
        var metadata = http.GetEndpoint()?.Metadata.GetMetadata<ProductImageEndpointMetadata>();
        if (metadata is null) { await next(http); return; }
        http.Response.OnStarting(() =>
        {
            http.Response.Headers.CacheControl = "private, no-cache";
            http.Response.Headers.XContentTypeOptions = "nosniff";
            http.Response.Headers.Vary = "Authorization";
            return Task.CompletedTask;
        });
        if (!metadata.Upload) { await next(http); return; }
        ProductImageEndpoints.WriteCondition(http.Request, delete: false);
        if (http.Request.ContentLength > ProductImageLimits.MaxRequestBytes) throw TooLarge();
        if (!MediaTypeHeaderValue.TryParse(http.Request.ContentType, out var media) ||
            !string.Equals(media.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
            throw new ProductImageException(ProductImageError.UnsupportedMediaType, "A multipart/form-data upload is required.");
        var sizeFeature = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeFeature is { IsReadOnly: false }) sizeFeature.MaxRequestBodySize = ProductImageLimits.MaxRequestBytes;
        http.Request.Body = new BoundedRequestStream(http.Request.Body, ProductImageLimits.MaxRequestBytes);
        http.Features.Set<IFormFeature>(new FormFeature(http.Request, new FormOptions
        {
            MultipartBodyLengthLimit = ProductImageLimits.MaxRequestBytes,
            MultipartBoundaryLengthLimit = 128,
            MultipartHeadersLengthLimit = 16 * 1024,
            ValueLengthLimit = ProductImageLimits.MaxRequestBytes,
            ValueCountLimit = 16
        }));
        IFormCollection form;
        try { form = await http.Request.ReadFormAsync(http.RequestAborted); }
        catch (InvalidDataException)
        { throw new ProductImageException(ProductImageError.InvalidImage, "Malformed multipart image upload."); }
        if (form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file")
            throw new ProductImageException(ProductImageError.InvalidImage, "Exactly one file field named file is required.");
        if (form.Files[0].Length > ProductImageLimits.MaxFileBytes) throw TooLarge();
        if (form.Files[0].Length == 0)
            throw new ProductImageException(ProductImageError.InvalidImage, "Image file is empty.");
        await next(http);
    }

    private static BadHttpRequestException TooLarge() => new("Image request exceeds the allowed size.", StatusCodes.Status413PayloadTooLarge);

}
