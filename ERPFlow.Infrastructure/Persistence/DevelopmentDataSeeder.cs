using System.Data;
using System.Security.Cryptography;
using System.Text;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Transfers;
using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Enums;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Transfers;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class DevelopmentDataSeeder(
    ERPFlowDbContext context,
    ILogger<DevelopmentDataSeeder> logger)
{
    public const string DefaultPassword = "Demo#Passw0rd!2026";

    public async Task SeedAsync(string environmentName, string password = DefaultPassword, bool allowProduction = false, CancellationToken cancellationToken = default)
    {
        if ((!string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase) && !allowProduction) || string.IsNullOrWhiteSpace(password))
        {
            return;
        }

        if (await context.InventoryAuthorities.AsNoTracking().AnyAsync(x => x.Mode == ERPFlow.Domain.Inventory.InventoryAuthorityMode.Warehouse, cancellationToken))
            throw new InvalidOperationException("Legacy Product seeding is prohibited after inventory authority cutover.");

        await using var transaction = await context.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        var users = await context.AppUsers.ToListAsync(cancellationToken);
        var hasher = new PasswordHasher<object>();
        var definitions = new (string Name, string Email, UserRole Role, string Password)[]
        {
            ("ERP Administrator", "admin@erpflow.local", UserRole.Administrator, DefaultPassword),
            ("Maya Haddad", "sales@erpflow.local", UserRole.Sales, DefaultPassword),
            ("Order Approver", "approver@erpflow.local", UserRole.Approver, DefaultPassword),
            ("Omar Nasser", "sales2@erpflow.local", UserRole.Sales, DefaultPassword),
            ("Leila Mansour", "sales3@erpflow.local", UserRole.Sales, DefaultPassword),
            ("Tariq Al-Mansoor", "tech@erpflow.local", UserRole.Technician, DefaultPassword),
            ("Nadia Faris", "purchasing@erpflow.local", UserRole.Purchasing, DefaultPassword),
            ("Samir Khalil", "warehouse@erpflow.local", UserRole.WarehouseOperator, DefaultPassword)
        };

        foreach (var (name, email, role, defaultAccountPassword) in definitions)
        {
            if (users.Any(user => string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var accountPassword = string.IsNullOrWhiteSpace(password) ? defaultAccountPassword : password;
            var user = new AppUser(name, email, role, hasher.HashPassword(new object(), accountPassword));
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
        var activeServiceReservedTotals = await context.JobcardItems
            .Join(
                context.Jobcards.Where(jobcard =>
                    jobcard.Status == JobcardStatus.Open ||
                    jobcard.Status == JobcardStatus.PartsRequested ||
                    jobcard.Status == JobcardStatus.InProgress),
                item => item.JobcardId,
                jobcard => jobcard.Id,
                (item, _) => item)
            .GroupBy(item => item.ProductId)
            .Select(group => new { ProductId = group.Key, Quantity = group.Sum(item => item.Quantity) })
            .ToDictionaryAsync(total => total.ProductId, total => total.Quantity, cancellationToken);
        foreach (var (productId, quantity) in activeServiceReservedTotals)
        {
            reservedTotals[productId] = reservedTotals.GetValueOrDefault(productId) + quantity;
        }

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
                    order.Dispatch(selected);

                    var seedKey = id.ToString("N")[..12].ToUpperInvariant();
                    context.DeliveryNotes.Add(new DeliveryNote(
                        order.Id,
                        order.CustomerId,
                        $"DN-DEMO-{seedKey}",
                        "Express Fleet Logistics",
                        $"TRK-{seedKey}"));

                    var assetIndex = 1;
                    foreach (var item in order.Items)
                    {
                        for (var unit = 0; unit < item.Quantity; unit++)
                        {
                            context.MachineAssets.Add(new MachineAsset(
                                $"TAG-{seedKey}-{assetIndex:D3}",
                                $"SN-{seedKey}-{assetIndex:D3}",
                                item.ProductId,
                                order.CustomerId,
                                order.Id,
                                DateTime.UtcNow.AddMonths(12),
                                1250 * assetIndex));
                            assetIndex++;
                        }
                    }
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
                var spareProduct = products.FirstOrDefault(product =>
                    (product.Sku == "PRINT-200" || product.Sku == "SCAN-100") &&
                    product.AvailableStock >= 1 &&
                    !inconsistentProducts.Contains(product.Id));
                if (spareProduct is not null)
                {
                    spareProduct.ReserveQuantity(1);
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
                j2.Complete("Cleaned optical scanner, lubricated drive gears, recalibrated touch sensor. All diagnostics passed.", firstAsset.MeterReading);
                context.Jobcards.Add(j2);

                await context.SaveChangesAsync(cancellationToken);
            }
        }

        await SeedProcurementAsync(users, products, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        await transaction.DisposeAsync();
        await SeedTransfersAsync(users, products, cancellationToken);
    }

    private async Task SeedProcurementAsync(List<AppUser> users, List<Product> products, CancellationToken ct)
    {
        await InventoryAuthoritySession.EnterAsync(context, false, ct);
        foreach (var (code, name) in new[] { ("DEMO-MAIN", "Main distribution warehouse"), ("DEMO-SERVICE", "Service parts warehouse"), ("DEMO-ARCHIVE", "Archived demo warehouse") })
        {
            if (await context.Warehouses.AnyAsync(w => w.Code == code, ct)) continue;
            var warehouse = new Warehouse(code, name);
            if (code == "DEMO-ARCHIVE") warehouse.SetActive(false);
            context.Warehouses.Add(warehouse);
        }
        foreach (var (code, name) in new[] { ("DEMO-EQUIP", "Cedar Equipment Supply"), ("DEMO-PACK", "Atlas Packaging Supply"), ("DEMO-RETIRED", "Retired demo supplier") })
        {
            if (await context.Suppliers.AnyAsync(s => s.SupplierCode == code, ct)) continue;
            var supplier = new Supplier(code, name, email: $"orders@{code.ToLowerInvariant()}.example");
            if (code == "DEMO-RETIRED") supplier.SetActive(false);
            context.Suppliers.Add(supplier);
        }
        await context.SaveChangesAsync(ct);
        var owner = users.Single(u => u.Email == "purchasing@erpflow.local");
        var approver = users.Single(u => u.Email == "approver@erpflow.local");
        var warehouseId = await context.Warehouses.Where(w => w.Code == "DEMO-MAIN").Select(w => w.Id).SingleAsync(ct);
        var supplierId = await context.Suppliers.Where(s => s.SupplierCode == "DEMO-EQUIP").Select(s => s.Id).SingleAsync(ct);
        var persistence = new EfProcurementPersistence(context);
        for (var i = 0; i < 10; i++)
        {
            var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"ERPFlow.Development.Purchase.v1.{i}"))[..16]);
            if (await context.PurchaseOrders.AnyAsync(o => o.Id == id, ct)) continue;
            var product = products.Single(p => p.Sku == (i % 2 == 0 ? "SCAN-100" : "LABEL-500"));
            var order = new PurchaseOrder(supplierId, warehouseId, owner.Id,
                new[] { (product.Id, 5 + i, decimal.Round(product.UnitPrice * 0.7m, 4)) },
                "Development replenishment scenario. Receiving and posting require warehouse inventory activation.");
            context.Entry(order).Property(o => o.Id).CurrentValue = id;
            context.PurchaseOrders.Add(order);
            await context.SaveChangesAsync(ct);
            if (i < 2) continue;
            var version = order.RowVersion.ToArray();
            await persistence.TransitionAsync(order, order.Submit(owner.Id), version, ct);
            if (i < 4) continue;
            version = order.RowVersion.ToArray();
            var history = i < 6 ? order.Approve(approver.Id, "Reviewed quantities and supplier quotation.")
                : i < 8 ? order.Reject(approver.Id, "Supplier quotation needs revision.")
                : order.Cancel(owner.Id, "Replenishment request withdrawn before receiving.");
            await persistence.TransitionAsync(order, history, version, ct);
        }
    }

    private async Task SeedTransfersAsync(List<AppUser> users, List<Product> products, CancellationToken ct)
    {
        var source = await context.Warehouses.Where(w => w.Code == "DEMO-MAIN").Select(w => w.Id).SingleAsync(ct);
        var destination = await context.Warehouses.Where(w => w.Code == "DEMO-SERVICE").Select(w => w.Id).SingleAsync(ct);
        var current = new SeedUser(users.Single(u => u.Email == "warehouse@erpflow.local").Id);
        var gate = new ProductAuthorityWriteGate();
        var workflow = new StockTransferWorkflow(new EfStockTransferPersistence(context), current, gate,
            new EfInventoryPosting(context, gate, current));
        for (var i = 0; i < 3; i++)
        {
            var result = await workflow.CreateAsync($"development-transfer-v1-{i}",
                new CreateStockTransferCommand(source, destination,
                    new[] { new StockTransferLineCommand(products.Single(p => p.Sku == "SCAN-100").Id, i + 1) }), ct);
            if (i != 2) continue;
            var transfer = await context.StockTransfers.AsNoTracking().SingleAsync(t => t.Id == result.Id, ct);
            if (transfer.Status == StockTransferStatus.Draft)
                await workflow.CancelAsync(transfer.Id, new CancelStockTransferCommand(transfer.RowVersion,
                    "Demo transfer withdrawn before posting."), ct);
        }
    }

    private sealed record SeedUser(Guid UserId) : ICurrentUser;
}
