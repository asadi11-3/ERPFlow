using System.Data;
using System.Net.Mail;
using ERPFlow.Application.Common;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Deployment;

public sealed class FirstAdministratorProvisioner(ERPFlowDbContext context)
{
    public async Task<Guid> ProvisionAsync(string name, string email, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 200 ||
            string.IsNullOrWhiteSpace(email) || email.Length > 320 || !MailAddress.TryCreate(email, out var address) ||
            address.Address != email || password.Length is < 16 or > 256 || string.IsNullOrWhiteSpace(password))
            throw new WorkflowValidationException("Administrator name, email and a unique password of 16–256 characters are required.");
        await new DatabaseSchemaVerifier(context).VerifyAsync(ct);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        await context.Database.ExecuteSqlRawAsync("""
            DECLARE @result int;
            EXEC @result=sys.sp_getapplock @Resource=N'ERPFlow.FirstAdministrator', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000;
            IF @result<0 THROW 51000, 'Administrator provisioning lock unavailable.', 1;
            """, ct);
        if (await context.AppUsers.AnyAsync(ct))
            throw new WorkflowConflictException("First-administrator provisioning requires a database with no existing users.");
        var administrator = new AppUser(name.Trim(), email.Trim().ToLowerInvariant(), UserRole.Administrator,
            new PasswordHasher<object>().HashPassword(new object(), password));
        context.AppUsers.Add(administrator);
        await context.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return administrator.Id;
    }
}
