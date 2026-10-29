using System.Text.Json;
using ERPFlow.Application.Abstractions;
using ERPFlow.Application.Common;
using ERPFlow.Application.Inventory;
using ERPFlow.Domain.Inventory;
using ERPFlow.Infrastructure.Deployment;
using ERPFlow.Infrastructure.Persistence;
using ERPFlow.Infrastructure.Persistence.Inventory;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

try
{
    if (args.Length < 3 || args[1] != "--confirm-database" ||
        args[0] is not ("migrate" or "verify" or "first-admin" or "prepare-cutover" or "complete-cutover"))
    {
        Console.Error.WriteLine("Usage: ERPFlow.Operations <migrate|verify|first-admin|prepare-cutover|complete-cutover> --confirm-database <exact-name> [approved-request.json]");
        return 2;
    }
    var connection = new SqlConnectionStringBuilder(Required("ERPFLOW_OPERATIONS_CONNECTION"));
    if (string.IsNullOrWhiteSpace(connection.DataSource) || string.IsNullOrWhiteSpace(connection.InitialCatalog) ||
        !string.Equals(connection.InitialCatalog, args[2], StringComparison.Ordinal) ||
        new[] { "master", "model", "msdb", "tempdb" }.Contains(connection.InitialCatalog, StringComparer.OrdinalIgnoreCase))
        throw new WorkflowValidationException("An explicit application database confirmation is required.");
    await using var context = new ERPFlowDbContext(new DbContextOptionsBuilder<ERPFlowDbContext>()
        .UseSqlServer(connection.ConnectionString).Options);
    if (args[0] == "migrate")
    {
        if (Required("ERPFLOW_OPERATIONS_APPROVAL") != "BACKUP-VERIFIED-WRITERS-STOPPED") throw new WorkflowValidationException("Maintenance approval is required.");
        await context.Database.MigrateAsync();
        await new DatabaseSchemaVerifier(context).VerifyAsync();
        Console.WriteLine("Migration and schema verification completed.");
        return 0;
    }
    await new DatabaseSchemaVerifier(context).VerifyAsync();
    if (args[0] == "verify")
    {
        await new ProductionInventoryAdmission().InitializeAsync(context);
        Console.WriteLine("Schema and existing authority verified; no database migration or cutover performed.");
    }
    else if (args[0] == "first-admin")
    {
        var password = Required("ERPFLOW_INITIAL_ADMIN_PASSWORD");
        Environment.SetEnvironmentVariable("ERPFLOW_INITIAL_ADMIN_PASSWORD", null);
        var id = await new FirstAdministratorProvisioner(context).ProvisionAsync(
            Required("ERPFLOW_INITIAL_ADMIN_NAME"), Required("ERPFLOW_INITIAL_ADMIN_EMAIL"), password);
        Console.WriteLine($"First Administrator created. User ID: {id}");
    }
    else
    {
        if (Required("ERPFLOW_OPERATIONS_APPROVAL") != "BACKUP-VERIFIED-WRITERS-STOPPED") throw new WorkflowValidationException("Maintenance approval is required.");
        var actor = new Operator(Guid.Parse(Required("ERPFLOW_OPERATOR_USER_ID")));
        if (args[0] == "prepare-cutover")
        {
            var result = await new EfLegacyInventoryImportPreparation(context, actor).PrepareAsync(Guid.NewGuid());
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
        else
        {
            if (args.Length != 4) throw new WorkflowValidationException("An independently reviewed cutover request file is required.");
            var request = JsonSerializer.Deserialize<InventoryCutoverRequest>(await File.ReadAllTextAsync(args[3]))
                ?? throw new WorkflowValidationException("Invalid cutover request.");
            var result = await new EfInventoryCutover(context, actor,
                new OperatorCutoverPermission(connection.InitialCatalog, args[2])).RehearseAsync(request);
            await new ProductionInventoryAdmission().InitializeAsync(context);
            Console.WriteLine(JsonSerializer.Serialize(result));
        }
    }
    return 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Operator command refused or failed. No credentials or exception details are emitted. Check the approved inputs, permissions and isolated rehearsal evidence.");
    return 1;
}

static string Required(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new WorkflowValidationException($"Required operator input is missing: {name}.");

sealed record Operator(Guid UserId) : ICurrentUser;

sealed class OperatorCutoverPermission(string database, string confirmation) : IInventoryCutoverPermission
{
    public void EnsureOperatorExecutionAllowed()
    {
        if (database != confirmation || Environment.GetEnvironmentVariable("ERPFLOW_OPERATIONS_APPROVAL") != "BACKUP-VERIFIED-WRITERS-STOPPED")
            throw new WorkflowAuthorizationException("The separate operator process requires explicit maintenance approval.");
    }
}
