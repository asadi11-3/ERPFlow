using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using ERPFlow.Application.ProductImages;

namespace ERPFlow.Infrastructure.ProductImages;

public sealed class ProductImageOptions
{
    public string? RootPath { get; set; }
    public string[] ForbiddenRoots { get; set; } = [];
}

public sealed class LocalProductImageStorage : IProductImageStorage
{
    private readonly string root;
    private readonly Lazy<string> identity;

    public LocalProductImageStorage(ProductImageOptions options)
    {
        root = Path.GetFullPath(options.RootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ERPFlow", "ProductImages"));
        var forbidden = options.ForbiddenRoots.Concat([AppContext.BaseDirectory, Directory.GetCurrentDirectory()]);
        if (root == Path.GetPathRoot(root) ||
            string.Equals(root.TrimEnd(Path.DirectorySeparatorChar),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            forbidden.Any(path => IsWithin(root, Path.GetFullPath(path))))
            throw new InvalidOperationException("The product image root must be a dedicated private directory outside application content.");
        identity = new Lazy<string>(Initialize);
    }

    public string StoreIdentity => identity.Value;

    public async ValueTask<IAsyncDisposable> AcquireLeaseAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        _ = StoreIdentity;
        var path = Path.Combine(root, $"{productId:N}.lease");
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckPath(path);
            try
            {
                return OpenPrivate(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException exception) when (IsSharingViolation(exception))
            {
                await Task.Delay(25, cancellationToken);
            }
        }
    }

    public async Task WriteAsync(string key, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        if (bytes.Length is 0 or > ProductImageLimits.MaxFileBytes)
            throw new InvalidOperationException("Invalid normalized image size.");
        var path = ObjectPath(key);
        await using var stream = OpenPrivate(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
        stream.Flush(flushToDisk: true);
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        await using var stream = OpenPrivate(ObjectPath(key), FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is 0 or > ProductImageLimits.MaxFileBytes)
            throw new IOException("Stored image size is invalid.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken);
        return bytes;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        File.Delete(ObjectPath(key));
        return Task.CompletedTask;
    }

    private string ObjectPath(string key)
    {
        _ = StoreIdentity;
        if (key.Length != 32 || key.Any(character => !char.IsAsciiHexDigitLower(character)))
            throw new InvalidOperationException("Invalid storage key.");
        var path = Path.Combine(root, key);
        CheckPath(path);
        return path;
    }

    private string Initialize()
    {
        CheckPath(root);
        if (OperatingSystem.IsWindows())
        {
            CreateWindowsDirectory(root);
        }
        else
        {
            Directory.CreateDirectory(root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(root) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                throw new InvalidOperationException("Product image root permissions must be owner-only.");
        }
        CheckPath(root);
        var marker = Path.Combine(root, ".store-id");
        var lockPath = Path.Combine(root, ".store-lock");
        FileStream lease;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            CheckPath(lockPath);
            try { lease = OpenPrivate(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); break; }
            catch (IOException exception) when (IsSharingViolation(exception) && DateTime.UtcNow < deadline) { Thread.Sleep(25); }
        }
        using (lease)
        {
            CheckPath(marker);
            if (!File.Exists(marker))
            {
                using var stream = OpenPrivate(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                stream.Write(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N")));
                stream.Flush(true);
            }
            var markerValue = File.ReadAllText(marker);
            if (!Guid.TryParseExact(markerValue, "N", out _)) throw new IOException("Invalid image store identity.");
            var canonicalRoot = OperatingSystem.IsWindows() ? root.ToUpperInvariant() : root;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{Environment.MachineName}\n{canonicalRoot}\n{markerValue}")));
        }
    }

    private static FileStream OpenPrivate(string path, FileMode mode, FileAccess access, FileShare share)
    {
        var options = new FileStreamOptions { Mode = mode, Access = access, Share = share,
            Options = FileOptions.Asynchronous, BufferSize = 81920 };
        if (!OperatingSystem.IsWindows() && mode is FileMode.CreateNew or FileMode.OpenOrCreate)
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return new FileStream(path, options);
    }

    private static bool IsSharingViolation(IOException exception) => (exception.HResult & 0xffff) is 11 or 32 or 33;

    private static bool IsWithin(string path, string parent) =>
        string.Equals(path.TrimEnd(Path.DirectorySeparatorChar), parent.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static void CheckPath(string path)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
        {
            var info = new FileInfo(current);
            if (info.LinkTarget is not null || (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0) ||
                (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0))
                throw new IOException("Symbolic links and reparse points are not allowed in image storage paths.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static void CreateWindowsDirectory(string path)
    {
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Storage owner is unavailable.");
        var security = new DirectorySecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
        var directory = new DirectoryInfo(path);
        directory.Create(security);
        directory.SetAccessControl(security);
    }
}
