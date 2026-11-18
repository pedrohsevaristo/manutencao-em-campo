using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ManutencaoEmCampo.Web.Data;

internal static class SqlServerPolicy
{
    public static string Connection(string? value, bool runtime)
    {
        var name = runtime ? "SqlServer" : "SqlServerMigrations";
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Configure ConnectionStrings__{name} para SQL Server.");

        SqlConnectionStringBuilder connection;
        try
        {
            connection = new SqlConnectionStringBuilder(value);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"ConnectionStrings__{name} tem formato inválido.");
        }

        if (connection.DataSource.Length == 0 || connection.InitialCatalog.Length == 0)
            throw new InvalidOperationException($"ConnectionStrings__{name} deve indicar servidor e banco.");
        if (connection.TrustServerCertificate || connection.Encrypt == SqlConnectionEncryptOption.Optional)
            throw new InvalidOperationException("SQL Server exige criptografia e validação do certificado.");
        if (runtime && connection.UserID.Trim().Equals("sa", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("A conta sa não pode executar a aplicação.");

        connection.PersistSecurityInfo = false;
        return connection.ConnectionString;
    }
}

public sealed class SqlServerMigrationsFactory : IDesignTimeDbContextFactory<WorkDbContext>
{
    public WorkDbContext CreateDbContext(string[] args)
    {
        var connection = SqlServerPolicy.Connection(
            Environment.GetEnvironmentVariable("ConnectionStrings__SqlServerMigrations"), runtime: false);
        return new WorkDbContext(new DbContextOptionsBuilder<WorkDbContext>().UseSqlServer(connection).Options);
    }
}

internal sealed class RuntimeSqlPrivileges : DbConnectionInterceptor
{
    private bool validated;

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (Volatile.Read(ref validated)) return;
        using var command = CreateCheck(connection);
        Validate(connection, command.ExecuteScalar());
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref validated)) return;
        await using var command = CreateCheck(connection);
        Validate(connection, await command.ExecuteScalarAsync(cancellationToken));
    }

    private void Validate(DbConnection connection, object? result)
    {
        if (result is not 0)
        {
            connection.Close();
            throw new InvalidOperationException("A identidade SQL de execução possui privilégios administrativos.");
        }

        Volatile.Write(ref validated, true);
    }

    private static DbCommand CreateCheck(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT CASE WHEN USER_NAME() = 'dbo'
                OR IS_SRVROLEMEMBER('sysadmin') = 1
                OR HAS_PERMS_BY_NAME(NULL, NULL, 'CONTROL SERVER') = 1
                OR IS_MEMBER('db_ddladmin') = 1 OR IS_MEMBER('db_securityadmin') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CONTROL') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'CREATE TABLE') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY SCHEMA') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY USER') = 1
                OR HAS_PERMS_BY_NAME(DB_NAME(), 'DATABASE', 'ALTER ANY ROLE') = 1
                THEN 1 ELSE 0 END;
            """;
        return command;
    }
}
