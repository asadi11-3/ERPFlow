namespace ERPFlow.Domain.Inventory;

public sealed class Warehouse
{
    private Warehouse() { }

    public Warehouse(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32)
            throw new ArgumentException("Warehouse code is required and limited to 32 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200)
            throw new ArgumentException("Warehouse name is required and limited to 200 characters.", nameof(name));

        Id = Guid.NewGuid();
        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string Code { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
}
