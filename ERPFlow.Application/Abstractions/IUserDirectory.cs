using ERPFlow.Domain.Entities;

namespace ERPFlow.Application.Abstractions;

public interface IUserDirectory
{
    Task<AppUser?> FindByEmailAsync(string email, CancellationToken cancellationToken = default);

    bool VerifyPassword(AppUser user, string password);
}
