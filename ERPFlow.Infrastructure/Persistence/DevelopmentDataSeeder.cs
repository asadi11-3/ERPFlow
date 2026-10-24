using System.Data;
using System.Security.Cryptography;
using System.Text;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class DevelopmentDataSeeder(
    ERPFlowDbContext context,
    ILogger<DevelopmentDataSeeder> logger)
{
    public async Task SeedAsync(string environmentName, string password, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var users = await context.AppUsers.ToListAsync(cancellationToken);
        var hasher = new PasswordHasher<object>();
        var definitions = new[]
        {
            ("ERP Administrator", "admin@erpflow.local", UserRole.Administrator),
            ("Maya Haddad", "sales@erpflow.local", UserRole.Sales),
            ("Order Approver", "approver@erpflow.local", UserRole.Approver),
            ("Omar Nasser", "sales2@erpflow.local", UserRole.Sales),
            ("Leila Mansour", "sales3@erpflow.local", UserRole.Sales),
            ("Tariq Al-Mansoor", "tech@erpflow.local", UserRole.Technician)
        };
        foreach (var (name, email, role) in definitions)
        {
            if (users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var user = new AppUser(name, email, role, hasher.HashPassword(new object(), password));
            users.Add(user);
            context.AppUsers.Add(user);
        }

        var customers = await context.Customers.ToListAsync(cancellationToken);
        var customerNames = new[]
        {
            "Contoso Retail", "Cedar Medical Supplies", "Atlas Distribution", "Olive Grove Foods",
            "Summit Office Solutions", "Harbour Logistics", "Petra Electronics", "Northstar Hardware",
            "Legacy Paper Trading", "Closed Coast Imports"
        };
        for (var i = 0; i < customerNames.Length; i++)
        {
            var email = i == 0 ? "orders@contoso.local" : $"orders@demo-customer-{i + 1}.example";
            if (customers.Any(customer => string.Equals(customer.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var customer = new Customer(customerNames[i], email);
            if (i >= 8)
            {
                customer.Deactivate();
            }

            customers.Add(customer);
            context.Customers.Add(customer);
        }

        var catalog = new (string Name, string Sku, decimal Price, int Stock)[]
        {
            ("Warehouse Scanner", "SCAN-100", 349m, 25),
            ("Shipping Label Roll", "LABEL-500", 19.95m, 200),
            ("Thermal Label Printer", "PRINT-200", 279m, 40),
            ("Packing Tape Carton", "TAPE-024", 32.50m, 120),
            ("Heavy Duty Storage Bin", "BIN-060", 24.90m, 180),
            ("Safety Gloves Pack", "GLOVE-012", 18.75m, 250),
            ("Barcode Label Sheets", "SHEET-100", 14.25m, 300),
            ("Digital Shipping Scale", "SCALE-030", 89m, 60),
            ("Pallet Wrap Roll", "WRAP-450", 22.80m, 140),
            ("Steel Shelving Unit", "SHELF-180", 159m, 35),
            ("Hand Truck", "TRUCK-150", 119m, 45),
            ("Warehouse Safety Vest", "VEST-001", 9.50m, 90),
            ("Corrugated Mailing Box", "BOX-040", 2.40m, 800),
            ("Protective Bubble Wrap", "BUBBLE-050", 16.90m, 170),
            ("Document Pouch Pack", "POUCH-100", 11.20m, 220),
            ("Rechargeable Work Light", "LIGHT-020", 45m, 70),
            ("Industrial Marker Set", "MARK-006", 12.60m, 110),
            ("Utility Knife", "KNIFE-010", 8.95m, 55),
            ("Replacement Scanner Battery", "BATT-100", 49m, 0),
            ("Precision Packing Dispenser", "DISP-002", 37.50m, 2)
        };
        var products = await context.Products.ToListAsync(cancellationToken);
        foreach (var (name, sku, price, stock) in catalog)
        {
            if (products.Any(product => string.Equals(product.Sku, sku, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var product = new Product(name, sku, price, stock);
            products.Add(product);
            context.Products.Add(product);
        }

        await context.SaveChangesAsync(cancellationToken);
        var existingOrders = await context.SalesOrders.Select(order => order.Id).ToHashSetAsync(cancellationToken);
        var reservedTotals = await context.StockReservations.Where(reservation => !reservation.IsReleased)
            .GroupBy(reservation => reservation.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(reservation => reservation.Quantity) })
            .ToDictionaryAsync(total => total.ProductId, total => total.Quantity, cancellationToken);
        var inconsistentProducts = products.Where(product =>
                product.ReservedQuantity != reservedTotals.GetValueOrDefault(product.Id))
            .Select(product => product.Id).ToHashSet();
        var salesEmails = new[] { "sales@erpflow.local", "sales2@erpflow.local", "sales3@erpflow.local" };
        for (var i = 0; i < 15; i++)
        {
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"ERPFlow.Development.Order.v1.{i + 1}"))[..16]);
            if (existingOrders.Contains(id))
            {
                continue;
            }

            var owner = users.Single(user => user.Email == salesEmails[i % 3]);
            var decisionActor = users.Single(user => user.Email == (i % 2 == 0
                ? "approver@erpflow.local" : "admin@erpflow.local"));
            var customerEmail = i % 8 == 0 ? "orders@contoso.local" : $"orders@demo-customer-{i % 8 + 1}.example";
            var customer = customers.Single(candidate => string.Equals(candidate.Email, customerEmail, StringComparison.OrdinalIgnoreCase));
            var selected = Enumerable.Range(0, i % 3 + 1)
                .Select(offset => products.Single(product => string.Equals(product.Sku, catalog[(i + offset) % 18].Sku, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            var quantity = i % 3 + 1;
            if (owner.Role != UserRole.Sales ||
                decisionActor.Role is not (UserRole.Approver or UserRole.Administrator) ||
                !customer.IsActive || selected.Any(product => product.AvailableStock < quantity || inconsistentProducts.Contains(product.Id)))
            {
                logger.LogWarning("Skipping development demo order {OrderId}: existing reference data conflicts with its scenario.", id);
                continue;
            }

            var order = new SalesOrder(customer.Id, owner.Id);
            context.Entry(order).Property(entity => entity.Id).CurrentValue = id;
            foreach (var product in selected)
            {
                order.AddItem(product, quantity);
            }

            if (i >= 4)
            {
                order.Submit(selected);
                context.ApprovalHistories.Add(new ApprovalHistory(
                    order.Id, owner.Id, ApprovalAction.Submitted, "Submitted for warehouse replenishment review."));
                if (i >= 9 && i < 12)
                {
                    context.ApprovalHistories.Add(order.Approve(decisionActor.Id,
                        "Stock and customer account checked; approved for dispatch."));
                }
                else if (i >= 12)
                {
                    var reasons = new[]
                    {
                        "Customer requested a revised delivery date; please create a new order.",
                        "Purchase authorization exceeds the customer's approved budget.",
                        "Duplicate replenishment request confirmed with the customer."
                    };
                    context.ApprovalHistories.Add(order.Reject(decisionActor.Id, selected, reasons[i - 12]));
                }
            }

            context.SalesOrders.Add(order);
        }

        await context.SaveChangesAsync(cancellationToken);

        if (!await context.MachineAssets.AnyAsync(cancellationToken))
        {
            var approvedOrders = await context.SalesOrders.Include(o => o.Items).Where(o => o.Status == OrderStatus.Approved).ToListAsync(cancellationToken);
            var tagIdx = 1;
            foreach (var approvedOrder in approvedOrders)
            {
                var delivery = new DeliveryNote(
                    approvedOrder.Id,
                    approvedOrder.CustomerId,
                    $"DN-202610-{tagIdx:D3}",
                    "Express Fleet Logistics",
                    $"TRK-{982310 + tagIdx}");
                context.DeliveryNotes.Add(delivery);

                foreach (var item in approvedOrder.Items)
                {
                    var asset = new MachineAsset(
                        $"TAG-2026-{tagIdx:D3}",
                        $"SN-PRN-{4000 + tagIdx}",
                        item.ProductId,
                        approvedOrder.CustomerId,
                        approvedOrder.Id,
                        DateTime.UtcNow.AddMonths(12),
                        1250 * tagIdx);
                    context.MachineAssets.Add(asset);
                    tagIdx++;
                }
            }

            await context.SaveChangesAsync(cancellationToken);
        }

        if (!await context.Jobcards.AnyAsync(cancellationToken))
        {
            var firstAsset = await context.MachineAssets.FirstOrDefaultAsync(cancellationToken);
            if (firstAsset is not null)
            {
                var j1 = new Jobcard(
                    "SRV-202610-001",
                    firstAsset.CustomerId,
                    firstAsset.Id,
                    "Tariq Al-Mansoor",
                    "Paper jam in roller feeder 2 and faint toner output across right margin.",
                    JobcardPriority.High);
                var spareProduct = products.FirstOrDefault(p => p.Sku == "PRINT-200" || p.Sku == "SCAN-100");
                if (spareProduct is not null)
                {
                    j1.AddPart(spareProduct, 1);
                }
                context.Jobcards.Add(j1);

                var j2 = new Jobcard(
                    "SRV-202610-002",
                    firstAsset.CustomerId,
                    firstAsset.Id,
                    "Zaid Haddad",
                    "Quarterly Preventive Maintenance (PM) and firmware diagnostic inspection.",
                    JobcardPriority.Medium);
                j2.Complete("Cleaned optical scanner, lubricated drive gears, recalibrated touch sensor. All diagnostics passed.", 2410);
                context.Jobcards.Add(j2);

                await context.SaveChangesAsync(cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
