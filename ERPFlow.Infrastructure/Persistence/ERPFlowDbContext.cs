using ERPFlow.Domain.Entities;
using ERPFlow.Domain.Inventory;
using ERPFlow.Domain.Procurement;
using ERPFlow.Domain.Receiving;
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
    public DbSet<LegacyInventoryImportBatch> LegacyInventoryImportBatches => Set<LegacyInventoryImportBatch>();
    public DbSet<LegacyInventoryOpening> LegacyInventoryOpenings => Set<LegacyInventoryOpening>();
    public DbSet<LegacyInventoryReservationImport> LegacyInventoryReservationImports => Set<LegacyInventoryReservationImport>();
    public DbSet<InventoryAuthority> InventoryAuthorities => Set<InventoryAuthority>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<PurchaseOrder> PurchaseOrders => Set<PurchaseOrder>();
    public DbSet<PurchaseOrderItem> PurchaseOrderItems => Set<PurchaseOrderItem>();
    public DbSet<PurchaseOrderHistory> PurchaseOrderHistories => Set<PurchaseOrderHistory>();
    public DbSet<GoodsReceipt> GoodsReceipts => Set<GoodsReceipt>();
    public DbSet<GoodsReceiptItem> GoodsReceiptItems => Set<GoodsReceiptItem>();
    public DbSet<GoodsReceiptCommand> GoodsReceiptCommands => Set<GoodsReceiptCommand>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ERPFlowDbContext).Assembly);
    }
}
