using ERPFlow.Application.Abstractions;
using ERPFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ERPFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        services.AddDbContext<ERPFlowDbContext>(options => options.UseSqlServer(connectionString));
        services.AddScoped<ISalesOrderPersistence, EfSalesOrderPersistence>();
        return services;
    }
}
