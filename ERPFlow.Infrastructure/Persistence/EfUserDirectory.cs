using ERPFlow.Application.Abstractions;
using ERPFlow.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class EfUserDirectory(ERPFlowDbContext context) : IUserDirectory
{
    private readonly PasswordHasher<AppUser> passwordHasher = new();

    public Task<AppUser?> FindByEmailAsync(
        string email,
        CancellationToken cancellationToken = default)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        return context.AppUsers
            .AsNoTracking()
            .SingleOrDefaultAsync(user => user.Email == normalizedEmail, cancellationToken);
    }

    public bool VerifyPassword(AppUser user, string password)
    {
        try
        {
            return passwordHasher.VerifyHashedPassword(user, user.PasswordHash, password)
                != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
