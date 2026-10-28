using System.Buffers.Binary;
using ERPFlow.Application.ProductImages;
using SkiaSharp;

namespace ERPFlow.Infrastructure.ProductImages;

public sealed class SkiaProductImageNormalizer : IProductImageNormalizer
{
    private static readonly SemaphoreSlim DecodeSlots = new(2, 2);

    public async Task<NormalizedProductImage> NormalizeAsync(Stream source, string fileName, string contentType,
        CancellationToken cancellationToken = default)
    {
        var format = (Path.GetExtension(fileName).ToLowerInvariant(), contentType.ToLowerInvariant()) switch
        {
            (".jpg" or ".jpeg", "image/jpeg") => SKEncodedImageFormat.Jpeg,
            (".png", "image/png") => SKEncodedImageFormat.Png,
            (".webp", "image/webp") => SKEncodedImageFormat.Webp,
            _ => throw Error(ProductImageError.UnsupportedMediaType, "Use matching JPEG, PNG or WebP content type and extension.")
        };
        await DecodeSlots.WaitAsync(cancellationToken);
        try
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int count;
            while ((count = await source.ReadAsync(chunk, cancellationToken)) != 0)
            {
                if (buffer.Length + count > ProductImageLimits.MaxFileBytes)
                    throw Error(ProductImageError.TooLarge, "Image payload exceeds 5 MiB.");
                buffer.Write(chunk, 0, count);
            }
            var bytes = buffer.ToArray();
            if (bytes.Length == 0) throw Invalid();
            var signature = Signature(bytes);
            if (signature is null) throw Invalid();
            if (signature != format) throw Error(ProductImageError.UnsupportedMediaType, "Image signature does not match its declared format.");
            ValidateContainer(bytes, format);
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null) throw Invalid();
            if (codec.EncodedFormat != format)
                throw Error(ProductImageError.UnsupportedMediaType, "Decoded image format does not match its declared format.");
            var info = codec.Info;
            if (codec.FrameCount > 1 || info.Width <= 0 || info.Height <= 0 ||
                info.Width > ProductImageLimits.MaxEdge || info.Height > ProductImageLimits.MaxEdge ||
                (long)info.Width * info.Height > ProductImageLimits.MaxPixels)
                throw Invalid();
            var pixelInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
            using var pixels = new SKBitmap(pixelInfo);
            if (codec.GetPixels(pixelInfo, pixels.GetPixels()) != SKCodecResult.Success) throw Invalid();
            cancellationToken.ThrowIfCancellationRequested();
            var swap = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var width = swap ? info.Height : info.Width;
            var height = swap ? info.Width : info.Height;
            using var oriented = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
            using (var canvas = new SKCanvas(oriented))
            {
                canvas.Clear(SKColors.Transparent);
                var matrix = Orientation(codec.EncodedOrigin, info.Width, info.Height);
                canvas.SetMatrix(matrix);
                canvas.DrawBitmap(pixels, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            }
            using var image = SKImage.FromBitmap(oriented);
            using var encoded = image.Encode(format, 90);
            if (encoded is null) throw Invalid();
            if (encoded.Size > ProductImageLimits.MaxFileBytes)
                throw Error(ProductImageError.TooLarge, "Normalized image exceeds 5 MiB.");
            return new NormalizedProductImage(encoded.ToArray(), format switch
            {
                SKEncodedImageFormat.Jpeg => "image/jpeg",
                SKEncodedImageFormat.Png => "image/png",
                _ => "image/webp"
            }, width, height);
        }
        finally { DecodeSlots.Release(); }
    }

    private static SKMatrix Orientation(SKEncodedOrigin origin, int width, int height) => origin switch
    {
        SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, width, 0, 1, 0, 0, 0, 1),
        SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, width, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, height, 0, 0, 1),
        SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightTop => new SKMatrix(0, -1, height, 1, 0, 0, 0, 0, 1),
        SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, height, -1, 0, width, 0, 0, 1),
        SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, width, 0, 0, 1),
        _ => SKMatrix.Identity
    };

    private static SKEncodedImageFormat? Signature(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[2] == 0xff) return SKEncodedImageFormat.Jpeg;
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return SKEncodedImageFormat.Png;
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return SKEncodedImageFormat.Webp;
        return null;
    }

    private static void ValidateContainer(ReadOnlySpan<byte> bytes, SKEncodedImageFormat format)
    {
        if (format == SKEncodedImageFormat.Jpeg)
        {
            if (bytes.Length < 4 || bytes[^2] != 0xff || bytes[^1] != 0xd9) throw Invalid();
            return;
        }
        if (format == SKEncodedImageFormat.Png)
        {
            var offset = 8;
            while (offset <= bytes.Length - 12)
            {
                var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
                if (length > bytes.Length - offset - 12) throw Invalid();
                var type = bytes.Slice(offset + 4, 4);
                if (type.SequenceEqual("acTL"u8) || type.SequenceEqual("fcTL"u8) || type.SequenceEqual("fdAT"u8)) throw Invalid();
                offset += checked((int)length + 12);
                if (type.SequenceEqual("IEND"u8))
                {
                    if (length != 0 || offset != bytes.Length) throw Invalid();
                    return;
                }
            }
            throw Invalid();
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4)) != bytes.Length - 8) throw Invalid();
        var position = 12;
        while (position <= bytes.Length - 8)
        {
            var type = bytes.Slice(position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(position + 4, 4));
            if (length > bytes.Length - position - 8) throw Invalid();
            if (type.SequenceEqual("ANIM"u8) || type.SequenceEqual("ANMF"u8) ||
                (type.SequenceEqual("VP8X"u8) && length > 0 && (bytes[position + 8] & 2) != 0)) throw Invalid();
            position += checked(8 + (int)length + (int)(length % 2));
        }
        if (position != bytes.Length) throw Invalid();
    }

    private static ProductImageException Invalid() => Error(ProductImageError.InvalidImage, "Invalid, truncated, animated or oversized-dimension image.");
    private static ProductImageException Error(ProductImageError error, string message) => new(error, message);
}
