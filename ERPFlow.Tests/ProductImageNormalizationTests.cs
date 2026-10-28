using ERPFlow.Application.ProductImages;
using ERPFlow.Infrastructure.ProductImages;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageNormalizationTests
{
    private readonly SkiaProductImageNormalizer normalizer = new();

    [Theory]
    [InlineData("jpg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("jpeg", "image/jpeg", SKEncodedImageFormat.Jpeg)]
    [InlineData("png", "image/png", SKEncodedImageFormat.Png)]
    [InlineData("webp", "image/webp", SKEncodedImageFormat.Webp)]
    public async Task ValidImagesAreFullyDecodableInTheirDeclaredFormat(
        string extension, string mime, SKEncodedImageFormat format)
    {
        using var source = new MemoryStream(ProductImageTestData.Image(format));
        var result = await normalizer.NormalizeAsync(source, $"image.{extension}", mime);
        Assert.Equal(mime, result.ContentType);
        Assert.Equal((19, 13), (result.Width, result.Height));
        using var codec = SKCodec.Create(new MemoryStream(result.Bytes));
        Assert.NotNull(codec);
        Assert.Equal(format, codec.EncodedFormat);
        using var pixels = new SKBitmap(codec.Info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(codec.Info, pixels.GetPixels()));
    }

    [Theory]
    [InlineData("image.svg", "image/svg+xml")]
    [InlineData("image.exe", "image/png")]
    [InlineData("image.png", "application/octet-stream")]
    [InlineData("image.png", "image/jpeg")]
    public async Task UnsupportedOrMismatchedDeclarationsAreRejected(string name, string mime)
    {
        using var source = new MemoryStream(ProductImageTestData.Image(SKEncodedImageFormat.Png));
        var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, name, mime));
        Assert.Equal(ProductImageError.UnsupportedMediaType, exception.Error);
    }

    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "jpg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "png", "image/png")]
    [InlineData(SKEncodedImageFormat.Webp, "webp", "image/webp")]
    public async Task TruncatedContainersAreRejected(SKEncodedImageFormat format, string extension, string mime)
    {
        var bytes = ProductImageTestData.Image(format);
        using var source = new MemoryStream(bytes[..^8]);
        var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, $"image.{extension}", mime));
        Assert.Equal(ProductImageError.InvalidImage, exception.Error);
    }

    [Fact]
    public async Task EmptyAndExecutableBytesAreNotImages()
    {
        foreach (var bytes in new[] { Array.Empty<byte>(), "MZ executable content"u8.ToArray() })
        {
            using var source = new MemoryStream(bytes);
            var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, "image.png", "image/png"));
            Assert.Equal(ProductImageError.InvalidImage, exception.Error);
        }
    }

    [Fact]
    public async Task ActualBytesAreLimitedEvenWithoutAnIFormFileLength()
    {
        using var source = new MemoryStream(new byte[ProductImageLimits.MaxFileBytes + 1]);
        var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, "image.png", "image/png"));
        Assert.Equal(ProductImageError.TooLarge, exception.Error);
    }

    [Fact]
    public async Task DimensionsAreCheckedBeforePixelAllocation()
    {
        using var source = new MemoryStream(ProductImageTestData.DimensionBombPng());
        var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, "image.png", "image/png"));
        Assert.Equal(ProductImageError.InvalidImage, exception.Error);
    }

    [Fact]
    public async Task CorruptedPngCriticalChunkIsRejected()
    {
        var bytes = ProductImageTestData.Image(SKEncodedImageFormat.Png);
        bytes[29] ^= 0xff;
        using var source = new MemoryStream(bytes);
        var exception = await Assert.ThrowsAsync<ProductImageException>(() => normalizer.NormalizeAsync(source, "image.png", "image/png"));
        Assert.Equal(ProductImageError.InvalidImage, exception.Error);
    }

    [Fact]
    public async Task NormalizationRemovesOriginalAncillaryMetadata()
    {
        var bytes = ProductImageTestData.PaddedPng(2048);
        Assert.True(bytes.AsSpan().IndexOf("vpAg"u8) >= 0);
        using var source = new MemoryStream(bytes);
        var result = await normalizer.NormalizeAsync(source, "image.png", "image/png");
        Assert.True(result.Bytes.AsSpan().IndexOf("vpAg"u8) < 0);
        Assert.True(result.Bytes.Length < bytes.Length);
    }

    [Fact]
    public async Task JpegOrientationIsAppliedBeforeExifIsStripped()
    {
        var jpeg = ProductImageTestData.Image(SKEncodedImageFormat.Jpeg);
        byte[] exif = [0xff, 0xe1, 0x00, 0x22, 0x45, 0x78, 0x69, 0x66, 0, 0,
            0x49, 0x49, 0x2a, 0, 8, 0, 0, 0, 1, 0, 0x12, 1, 3, 0,
            1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        using var source = new MemoryStream([.. jpeg[..2], .. exif, .. jpeg[2..]]);
        var result = await normalizer.NormalizeAsync(source, "photo.jpg", "image/jpeg");
        Assert.Equal((13, 19), (result.Width, result.Height));
        Assert.True(result.Bytes.AsSpan().IndexOf("Exif"u8) < 0);
    }
}
