using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace ERPFlow.Infrastructure.Persistence;

public static class ProfessionalDemoDatabaseGuard
{
    private static readonly Regex DatabaseNamePattern = new("^ERPFlow_Demo_[A-Za-z0-9_]+$", RegexOptions.CultureInvariant);

    public static string BuildLocalConnectionString(string databaseName)
    {
        ValidateDatabaseName(databaseName);
        return new SqlConnectionStringBuilder
        {
            DataSource = @".\SQLEXPRESS",
            InitialCatalog = databaseName,
            IntegratedSecurity = true,
            TrustServerCertificate = true,
            ApplicationName = "ERPFlow Professional Demo Seeder"
        }.ConnectionString;
    }

    public static void ValidateDatabaseName(string databaseName)
    {
        if (string.IsNullOrWhiteSpace(databaseName) || !DatabaseNamePattern.IsMatch(databaseName))
            throw new InvalidOperationException("Demo database must match ERPFlow_Demo_<identifier> and use the local SQL Express instance.");
    }

    public static async Task<bool> ExistsAsync(string connectionString, CancellationToken ct)
    {
        var target = new SqlConnectionStringBuilder(connectionString);
        if (target.DataSource is not @".\SQLEXPRESS" || target.InitialCatalog is null)
            throw new InvalidOperationException("Demo seeding accepts only the local SQL Express target.");

        var master = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = "master" };
        await using var connection = new SqlConnection(master.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT CONVERT(bit, CASE WHEN DB_ID(@name) IS NULL THEN 0 ELSE 1 END)";
        command.Parameters.AddWithValue("@name", target.InitialCatalog);
        return (bool)(await command.ExecuteScalarAsync(ct) ?? false);
    }
}
