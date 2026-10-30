using System.Text.Json;
using Microsoft.Data.Sqlite;
using WidgetPlanner.Core;

namespace WidgetPlanner.Infrastructure;

// A versioned document in a transactional SQLite database keeps the first local milestone small.
// Relational integration tables can be introduced with a schema migration later.
public sealed class SqlitePlannerStore : IPlannerStore
{
    private readonly string connectionString;
    public SqlitePlannerStore(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        connectionString = new SqliteConnectionStringBuilder { DataSource = path }.ToString();
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS planner (id INTEGER PRIMARY KEY CHECK(id=1), version INTEGER NOT NULL, document TEXT NOT NULL)";
        command.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var c = new SqliteConnection(connectionString); c.Open(); return c; }
    public PlannerSnapshot Load()
    {
        using var c = Open(); using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT version, document FROM planner WHERE id=1";
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return new([], []);
        if (reader.GetInt32(0) != 1) throw new InvalidDataException("Unsupported planner database version. Your data has not been changed.");
        return JsonSerializer.Deserialize<PlannerSnapshot>(reader.GetString(1)) ?? throw new InvalidDataException("Invalid planner data.");
    }
    public void Save(PlannerSnapshot snapshot)
    {
        using var c = Open(); using var transaction = c.BeginTransaction(); using var cmd = c.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "INSERT INTO planner VALUES(1,1,$data) ON CONFLICT(id) DO UPDATE SET document=$data";
        cmd.Parameters.AddWithValue("$data", JsonSerializer.Serialize(snapshot));
        cmd.ExecuteNonQuery(); transaction.Commit();
    }
}
