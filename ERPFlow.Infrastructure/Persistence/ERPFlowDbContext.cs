using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed partial class ERPFlowDbContext(DbContextOptions<ERPFlowDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Product> Products => Set<Product>();

    public DbSet<SalesOrder> SalesOrders => Set<SalesOrder>();

    public DbSet<SalesOrderItem> SalesOrderItems => Set<SalesOrderItem>();

    public DbSet<StockReservation> StockReservations => Set<StockReservation>();

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    public DbSet<ApprovalHistory> ApprovalHistories => Set<ApprovalHistory>();

    public DbSet<DeliveryNote> DeliveryNotes => Set<DeliveryNote>();

    public DbSet<MachineAsset> MachineAssets => Set<MachineAsset>();

    public DbSet<Jobcard> Jobcards => Set<Jobcard>();

    public DbSet<JobcardItem> JobcardItems => Set<JobcardItem>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<WarehouseStock> WarehouseStocks => Set<WarehouseStock>();
    public DbSet<InventoryReservation> InventoryReservations => Set<InventoryReservation>();
    public DbSet<InventoryPosting> InventoryPostings => Set<InventoryPosting>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ERPFlowDbContext).Assembly);
    }
}
