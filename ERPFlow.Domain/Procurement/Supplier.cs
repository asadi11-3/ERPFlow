using System.Net.Mail;

namespace ERPFlow.Domain.Procurement;

public sealed class Supplier
{
    private Supplier() { }

    public Supplier(string supplierCode, string name, string? contactName = null, string? email = null,
        string? phone = null, string? address = null)
    {
        SupplierCode = NormalizeCode(supplierCode);
        Edit(name, contactName, email, phone, address);
        Id = Guid.NewGuid();
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }
    public string SupplierCode { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public string? ContactName { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Edit(string name, string? contactName, string? email, string? phone, string? address)
    {
        var normalizedName = Text(name, 200, true)!;
        var contact = Text(contactName, 150);
        var normalizedEmail = Text(email, 320);
        var normalizedPhone = Text(phone, 50);
        var normalizedAddress = Text(address, 1000);
        if (normalizedEmail is not null && (!MailAddress.TryCreate(normalizedEmail, out var parsed) || parsed.Address != normalizedEmail))
            throw new ArgumentException("Supplier email is invalid.", nameof(email));
        (Name, ContactName, Email, Phone, Address) = (normalizedName, contact, normalizedEmail, normalizedPhone, normalizedAddress);
    }

    public void SetActive(bool active) => IsActive = active;

    public static string NormalizeCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length > 32 ||
            code.Trim().Any(x => !char.IsAsciiLetterOrDigit(x) && x is not ('-' or '_')))
            throw new ArgumentException("Supplier code requires 1-32 ASCII letters, digits, hyphens or underscores.", nameof(code));
        return code.Trim().ToUpperInvariant();
    }

    private static string? Text(string? value, int max, bool required = false)
    {
        if ((required && string.IsNullOrWhiteSpace(value)) || value?.Length > max || value?.Any(char.IsControl) == true)
            throw new ArgumentException($"Supplier text must be valid and at most {max} characters.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
