namespace ERPFlow.Domain.Entities;

public sealed class Customer
{
    public Customer(string name, string email)
    {
        Id = Guid.NewGuid();
        Name = RequireText(name, nameof(name));
        Email = RequireText(email, nameof(email));
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private static string RequireText(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A non-empty value is required.", parameterName);
        }

        return value.Trim();
    }
}
