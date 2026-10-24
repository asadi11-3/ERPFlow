using ERPFlow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace ERPFlow.Infrastructure.Persistence;

public sealed class ERPFlowDbContext(DbContextOptions<ERPFlowDbContext> options) : DbContext(options)
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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ERPFlowDbContext).Assembly);
    }
}
