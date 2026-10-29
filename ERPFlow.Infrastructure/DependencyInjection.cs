using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Analytics;
using ERPFlow.Application.Equipment;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Application.Service;
using ERPFlow.Application.Inventory;
using ERPFlow.Application.Procurement;
using ERPFlow.Application.Receiving;
using ERPFlow.Application.Transfers;
using ERPFlow.Application.ProductImages;
using ERPFlow.Infrastructure.ProductImages;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ERPFlow.Infrastructure.Deployment;

namespace ERPFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductionInventory(this IServiceCollection services)
    {
        services.AddSingleton<ProductionInventoryAdmission>();
        services.AddScoped<IInventoryWriteGate, ProductionWarehouseWriteGate>();
        return services;
    }

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        Func<IServiceProvider, string> connectionStringFactory)
    {
        services.AddDbContext<ERPFlowDbContext>((serviceProvider, options) =>
            options.UseSqlServer(connectionStringFactory(serviceProvider)));
        services.AddScoped<ISalesOrderPersistence, EfSalesOrderPersistence>();
        services.AddScoped<IUserDirectory, EfUserDirectory>();
        services.AddScoped<IReferenceDataQueries, EfReferenceDataQueries>();
        services.AddScoped<IAnalyticsQueries, EfAnalyticsQueries>();
        services.AddScoped<ISalesOrderWorkflow, SalesOrderWorkflow>();
        services.AddScoped<IEquipmentPersistence, EfEquipmentPersistence>();
        services.AddScoped<IEquipmentWorkflow, EquipmentWorkflow>();
        services.AddScoped<IServicePersistence, EfServicePersistence>();
        services.AddScoped<IServiceWorkflow, ServiceWorkflow>();
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
        services.AddScoped<IStockTransferPersistence, EfStockTransferPersistence>();
        services.AddScoped<IStockTransferWorkflow, StockTransferWorkflow>();
        services.AddScoped<IInventoryQueryPersistence, EfInventoryQueryPersistence>();
        services.AddScoped<IInventoryQueryWorkflow, InventoryQueryWorkflow>();
        services.AddSingleton<IProductImageNormalizer, SkiaProductImageNormalizer>();
        services.AddScoped<EfProductImageWorkflow>();
        services.AddScoped<IProductImageWorkflow>(sp => sp.GetRequiredService<EfProductImageWorkflow>());
        services.AddScoped<IProductImageCleanup>(sp => sp.GetRequiredService<EfProductImageWorkflow>());
        services.AddScoped<ERPFlow.Application.Products.IProductCatalog, EfProductCatalog>();
        return services;
    }
}
