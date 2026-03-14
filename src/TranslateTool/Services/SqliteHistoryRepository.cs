using Microsoft.Data.Sqlite;
using TranslateTool.Models;

namespace TranslateTool.Services;

public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly string _connectionString;

    public SqliteHistoryRepository()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TranslateTool");
        Directory.CreateDirectory(root);

        var dbPath = Path.Combine(root, "history.db");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        EnsureSchema();
    }

    public async Task AddAsync(HistoryItem item, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO history (type, source, result, provider, created_at)
            VALUES ($type, $source, $result, $provider, $createdAt);
            """;

        command.Parameters.AddWithValue("$type", item.Type);
        command.Parameters.AddWithValue("$source", DataProtectionHelper.Protect(item.Source));
        command.Parameters.AddWithValue("$result", DataProtectionHelper.Protect(item.Result));
        command.Parameters.AddWithValue("$provider", item.Provider);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.UtcDateTime.ToString("O"));

        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<HistoryItem>> QueryAsync(HistoryQuery query, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, type, source, result, provider, created_at
            FROM history
            ORDER BY datetime(created_at) DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", Math.Max(1, query.Limit));

        var items = new List<HistoryItem>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var createdRaw = reader.GetString(5);
            var createdAt = DateTimeOffset.TryParse(createdRaw, out var parsed) ? parsed : DateTimeOffset.Now;

            items.Add(new HistoryItem(
                reader.GetInt64(0),
                reader.GetString(1),
                DataProtectionHelper.Unprotect(reader.GetString(2)),
                DataProtectionHelper.Unprotect(reader.GetString(3)),
                reader.GetString(4),
                createdAt));
        }

        return items;
    }

    public async Task DeleteOlderThanAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history WHERE datetime(created_at) < datetime($cutoff);";
        command.Parameters.AddWithValue("$cutoff", cutoff.UtcDateTime.ToString("O"));
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM history;";
        _ = await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private void EnsureSchema()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS history (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              type TEXT NOT NULL,
              source TEXT NOT NULL,
              result TEXT NOT NULL,
              provider TEXT NOT NULL,
              created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_history_created_at ON history(created_at DESC);
            """;

        _ = command.ExecuteNonQuery();
    }
}
