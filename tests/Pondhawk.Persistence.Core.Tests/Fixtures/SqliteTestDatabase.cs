using Microsoft.Data.Sqlite;

namespace Pondhawk.Persistence.Core.Tests.Fixtures;

public sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<string> _statements = [];

    public SqliteTestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
    }

    public string ConnectionString => "Data Source=:memory:";
    public SqliteConnection Connection => _connection;

    public SqliteTestDatabase AddTable(string name, string definition)
    {
        _statements.Add($"CREATE TABLE [{name}] ({definition})");
        return this;
    }

    public SqliteTestDatabase AddView(string name, string selectSql)
    {
        _statements.Add($"CREATE VIEW [{name}] AS {selectSql}");
        return this;
    }

    public SqliteTestDatabase AddIndex(string name, string table, string columns, bool unique = false)
    {
        var uniqueStr = unique ? "UNIQUE " : "";
        _statements.Add($"CREATE {uniqueStr}INDEX [{name}] ON [{table}] ({columns})");
        return this;
    }

    public SqliteTestDatabase Build()
    {
        foreach (var sql in _statements)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = sql;
            cmd.ExecuteNonQuery();
        }
        return this;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
