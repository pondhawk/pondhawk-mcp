namespace Pondhawk.Persistence.Core.Ddl;

public static class DdlGeneratorFactory
{
    public static IDdlGenerator Create(string provider) => provider.ToLowerInvariant() switch
    {
        "sqlserver" => new SqlServerDdlGenerator(),
        "postgresql" => new PostgreSqlDdlGenerator(),
        "mysql" or "mariadb" => new MySqlDdlGenerator(),
        "sqlite" => new SqliteDdlGenerator(),
        _ => throw new ArgumentException($"Invalid provider '{provider}'. Valid values: sqlserver, postgresql, mysql, sqlite")
    };
}
