using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERPFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        services.AddDbContext<ERPFlowDbContext>((serviceProvider, options) =>
            options.UseSqlServer(connectionStringFactory(serviceProvider)));
        services.AddScoped<ISalesOrderPersistence, EfSalesOrderPersistence>();
        services.AddScoped<IUserDirectory, EfUserDirectory>();
        services.AddScoped<IReferenceDataQueries, EfReferenceDataQueries>();
        services.AddScoped<ISalesOrderWorkflow, SalesOrderWorkflow>();
        services.AddScoped<IEquipmentPersistence, EfEquipmentPersistence>();
        services.AddScoped<IEquipmentWorkflow, EquipmentWorkflow>();
        services.AddScoped<IServicePersistence, EfServicePersistence>();
        services.AddScoped<IServiceWorkflow, ServiceWorkflow>();
        services.AddScoped<DevelopmentDataSeeder>();
        services.AddSingleton<IInventoryWriteGate, ProductAuthorityWriteGate>();
        services.AddScoped<IInventoryPosting, EfInventoryPosting>();
        services.AddScoped<IWarehouseSourcePolicy, ProductAuthorityWarehouseSourcePolicy>();
        services.AddScoped<ILegacyInventoryImportPreparation, EfLegacyInventoryImportPreparation>();
        services.AddSingleton<IInventoryCutoverPermission, DisabledInventoryCutoverPermission>();
        services.AddScoped<IInventoryCutover, EfInventoryCutover>();
        services.AddScoped<IInventoryReconciliation, EfInventoryReconciliation>();
        services.AddScoped<IWorkflowInventory, EfWorkflowInventory>();
        services.AddScoped<IProcurementPersistence, EfProcurementPersistence>();
        services.AddScoped<IProcurementMasters, ProcurementMasters>();
        services.AddScoped<IPurchaseOrderWorkflow, PurchaseOrderWorkflow>();
        services.AddScoped<IGoodsReceiptPersistence, EfGoodsReceiptPersistence>();
        services.AddScoped<IGoodsReceiptWorkflow, GoodsReceiptWorkflow>();
        return services;
    }
}
