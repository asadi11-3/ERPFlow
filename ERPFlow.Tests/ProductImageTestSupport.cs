using System.Buffers.Binary;
using System.Net.Http.Headers;
using ERPFlow.Api;
using ERPFlow.Application.ProductImages;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using Xunit;

namespace ERPFlow.Tests;

public sealed class ProductImageFixture : IAsyncLifetime
{
    private readonly Dictionary<UserRole, Guid> actors = [];
    private readonly Action<IServiceCollection>? configureServices;

    public ProductImageFixture() { }
    internal ProductImageFixture(Action<IServiceCollection> configureServices) => this.configureServices = configureServices;

    internal InventoryTestDatabase Database { get; private set; } = null!;
    internal ProductImageApiFactory Factory { get; private set; } = null!;
    internal HttpClient Client { get; private set; } = null!;
    internal Guid ProductId { get; private set; }
    internal string StorageRoot { get; } = Path.Combine(
        Path.GetTempPath(), "opencode",
        $"ERPFlow_ProductImages_{Guid.NewGuid():N}");

    public async Task InitializeAsync()
    {
        Database = await InventoryTestDatabase.CreateAsync();
        await using (var context = Database.Context())
        {
            foreach (var role in Enum.GetValues<UserRole>())
            {
                var user = new AppUser($"Image {role}", $"image-{role}@example.test", role, "test-hash");
                actors.Add(role, user.Id);
                context.AppUsers.Add(user);
            }

            var product = new Product("Image product", $"IMG-{Guid.NewGuid():N}", 123.45m, 37);
            ProductId = product.Id;
            context.Products.Add(product);
            await context.SaveChangesAsync();
        }

        using var ownedContext = Database.Context();
        Assert.True(InventoryTestDatabase.Owns(ownedContext));
        Directory.CreateDirectory(StorageRoot);
        Factory = new ProductImageApiFactory(ownedContext.Database.GetConnectionString()!, StorageRoot, configureServices);
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await Database.DisposeAsync();
        if (Directory.Exists(StorageRoot))
            Directory.Delete(StorageRoot, recursive: true);
    }

    internal async Task AuthenticateAsync(UserRole role = UserRole.Administrator, HttpClient? client = null)
    {
        await using var context = Database.Context();
        var user = await context.AppUsers.AsNoTracking().SingleAsync(x => x.Id == actors[role]);
        var token = Factory.Services.GetRequiredService<JwtTokenService>().Create(user).AccessToken;
        (client ?? Client).DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    internal HttpClient NewClient() => Factory.CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost")
    });

    internal string[] StoredObjects() => Directory.GetFiles(StorageRoot)
        .Where(path => System.IO.Path.GetFileName(path) is { Length: 32 } name &&
            name.All(char.IsAsciiHexDigitLower)).ToArray();

    internal async Task<int> CleanupAsync()
    {
        await using var scope = Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IProductImageCleanup>().RunOnceAsync();
    }
}

internal sealed class ProductImageApiFactory(string connectionString, string storageRoot,
    Action<IServiceCollection>? configureServices = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:ERPFlowDb"] = connectionString,
                ["DemoUsers:Password"] = "",
                ["Jwt:Issuer"] = "ERPFlow.ProductImage.Tests",
                ["Jwt:Audience"] = "ERPFlow.ProductImage.Tests",
                ["Jwt:SigningKey"] = "fixed-product-image-test-signing-key-at-least-32-characters",
                ["Jwt:ExpirationMinutes"] = "10",
                ["ProductImages:RootPath"] = storageRoot
            }));
        if (configureServices is not null) builder.ConfigureServices(configureServices);
    }
}

internal static class ProductImageTestData
{
    internal static byte[] Image(SKEncodedImageFormat format, int width = 19, int height = 13, int quality = 47)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Opaque);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(new SKColor(28, 91, 167));
        using var paint = new SKPaint { Color = new SKColor(238, 173, 34), IsAntialias = false };
        canvas.DrawRect(3, 2, Math.Max(1, width - 7), Math.Max(1, height - 5), paint);
        using var image = SKImage.FromBitmap(bitmap);
        using var encoded = image.Encode(format, quality);
        return encoded.ToArray();
    }

    internal static MultipartFormDataContent Upload(
        byte[] bytes,
        string fileName = "image.png",
        string mediaType = "image/png",
        string fieldName = "file")
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        content.Add(file, fieldName, fileName);
        return content;
    }

    internal static byte[] PaddedPng(int totalBytes)
    {
        var original = Image(SKEncodedImageFormat.Png);
        var iendOffset = original.Length - 12;
        var payloadLength = totalBytes - original.Length - 12;
        if (payloadLength < 0)
            throw new ArgumentOutOfRangeException(nameof(totalBytes));

        using var output = new MemoryStream(totalBytes);
        output.Write(original, 0, iendOffset);
        WritePngChunk(output, "vpAg"u8, new byte[payloadLength]);
        output.Write(original, iendOffset, 12);
        return output.ToArray();
    }

    internal static byte[] DimensionBombPng()
    {
        var bytes = Image(SKEncodedImageFormat.Png);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), 100_000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), 100_000);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(29, 4), Crc32(bytes.AsSpan(12, 17)));
        return bytes;
    }

    internal static (string Extension, string MediaType, SKEncodedImageFormat Format)[] Formats =>
    [
        ("jpg", "image/jpeg", SKEncodedImageFormat.Jpeg),
        ("png", "image/png", SKEncodedImageFormat.Png),
        ("webp", "image/webp", SKEncodedImageFormat.Webp)
    ];

    private static void WritePngChunk(Stream output, ReadOnlySpan<byte> type, byte[] payload)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, payload.Length);
        output.Write(length);
        output.Write(type);
        output.Write(payload);
        var crcInput = new byte[type.Length + payload.Length];
        type.CopyTo(crcInput);
        payload.CopyTo(crcInput, type.Length);
        Span<byte> crc = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput));
        output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xedb88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}
