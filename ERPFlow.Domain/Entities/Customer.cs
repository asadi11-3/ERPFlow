namespace ERPFlow.Domain.Entities;

public sealed class Customer
{
    public Customer(string name, string email)
    {
        Id = Guid.NewGuid();
        Name = RequireText(name, nameof(name));
        Email = RequireText(email, nameof(email));
        IsActive = true;
        CreatedAt = BusinessClock.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public void Activate() => IsActive = true;

    public void Deactivate() => IsActive = false;

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
