using ERPFlow.Application.Abstractions;
using ERPFlow.Application.SalesOrders;
using ERPFlow.Infrastructure.Persistence;
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
        services.AddScoped<DevelopmentDataSeeder>();
        return services;
    }
}
