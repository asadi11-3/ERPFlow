using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class DevelopmentDataSeeder(ERPFlowDbContext context)
{
    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        var existingEmails = await context.AppUsers
            .Select(user => user.Email)
            .ToHashSetAsync(cancellationToken);
        var hasher = new PasswordHasher<object>();
        var users = new[]
        {
            CreateUser("ERP Administrator", "admin@erpflow.local", UserRole.Administrator, password, hasher),
            CreateUser("Sales User", "sales@erpflow.local", UserRole.Sales, password, hasher),
            CreateUser("Order Approver", "approver@erpflow.local", UserRole.Approver, password, hasher)
        };
        context.AppUsers.AddRange(users.Where(user => !existingEmails.Contains(user.Email)));

        if (!await context.Customers.AnyAsync(cancellationToken))
        {
            context.Customers.Add(new Customer("Contoso Retail", "orders@contoso.local"));
        }

        if (!await context.Products.AnyAsync(cancellationToken))
        {
            context.Products.AddRange(
                new Product("Warehouse Scanner", "SCAN-100", 349.00m, 25),
                new Product("Shipping Label Roll", "LABEL-500", 19.95m, 200));
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static AppUser CreateUser(
        string name,
        string email,
        UserRole role,
        string password,
        PasswordHasher<object> hasher) =>
        new(name, email, role, hasher.HashPassword(new object(), password));
}
