using ERPFlow.Domain.Enums;

namespace ERPFlow.Domain.Entities;

public sealed class AppUser
{
    public AppUser(string name, string email, UserRole role)
    {
        if (!Enum.IsDefined(role))
        {
            throw new ArgumentOutOfRangeException(nameof(role));
        }

        Id = Guid.NewGuid();
        Name = RequireText(name, nameof(name));
        Email = RequireText(email, nameof(email));
        Role = role;
        CreatedAt = DateTime.UtcNow;
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; }

    public string Email { get; private set; }

    public UserRole Role { get; private set; }

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
