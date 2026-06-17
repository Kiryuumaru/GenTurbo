using Domain.Workers.Entities;
using Domain.Workers.Enums;
using Domain.Workers.Interfaces;
using Domain.Workers.ValueObjects;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Infrastructure.Sqlite.Repositories;

internal sealed class SqliteWorkerRepository : IWorkerRepository
{
    private readonly SqliteConnection _connection;

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS workers (
            id TEXT PRIMARY KEY,
            name TEXT NOT NULL,
            host TEXT NOT NULL,
            status TEXT DEFAULT 'Online',
            last_heartbeat TEXT,
            registered_at TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS worker_models (
            worker_id TEXT REFERENCES workers(id) ON DELETE CASCADE,
            model_id TEXT NOT NULL,
            type TEXT NOT NULL,
            vram_required_gb INTEGER,
            param_schema TEXT DEFAULT '{}',
            PRIMARY KEY (worker_id, model_id)
        );
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqliteWorkerRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task InitializeAsync()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = CreateTableSql;
        await cmd.ExecuteNonQueryAsync();
    }

    public void Add(WorkerEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO workers (id, name, host, status, last_heartbeat, registered_at)
            VALUES (@id, @name, @host, @status, @last_heartbeat, @registered_at)
            """;
        cmd.Parameters.AddWithValue("@id", entity.WorkerId);
        cmd.Parameters.AddWithValue("@name", entity.Name);
        cmd.Parameters.AddWithValue("@host", entity.Host);
        cmd.Parameters.AddWithValue("@status", entity.Status.ToString());
        cmd.Parameters.AddWithValue("@last_heartbeat", entity.LastHeartbeat.ToString("O"));
        cmd.Parameters.AddWithValue("@registered_at", entity.RegisteredAt.ToString("O"));
        cmd.ExecuteNonQuery();

        InsertModels(entity);
    }

    public async Task<WorkerEntity?> GetByIdAsync(string workerId, CancellationToken cancellationToken = default)
    {
        WorkerEntity? entity = null;
        using (var cmd = _connection.CreateCommand())
        {
            cmd.CommandText = "SELECT * FROM workers WHERE id = @id";
            cmd.Parameters.AddWithValue("@id", workerId);
            using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
                entity = MapWorkerFromReader(reader);
        }

        if (entity is not null)
            entity.UpdateModels(await GetModelsForWorkerAsync(workerId, cancellationToken));

        return entity;
    }

    public async Task<IReadOnlyList<WorkerEntity>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workers ORDER BY name";
        return await ReadWorkersWithModelsAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkerEntity>> GetOnlineWorkersAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workers WHERE status = 'Online'";
        return await ReadWorkersWithModelsAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkerEntity>> GetStaleWorkersAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM workers WHERE last_heartbeat < @cutoff AND status = 'Online'";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        return await ReadWorkersWithModelsAsync(cmd, cancellationToken);
    }

    public void Update(WorkerEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE workers SET name = @name, host = @host, status = @status,
                   last_heartbeat = @last_heartbeat
            WHERE id = @id
            """;
        cmd.Parameters.AddWithValue("@id", entity.WorkerId);
        cmd.Parameters.AddWithValue("@name", entity.Name);
        cmd.Parameters.AddWithValue("@host", entity.Host);
        cmd.Parameters.AddWithValue("@status", entity.Status.ToString());
        cmd.Parameters.AddWithValue("@last_heartbeat", entity.LastHeartbeat.ToString("O"));
        cmd.ExecuteNonQuery();

        using var deleteCmd = _connection.CreateCommand();
        deleteCmd.CommandText = "DELETE FROM worker_models WHERE worker_id = @worker_id";
        deleteCmd.Parameters.AddWithValue("@worker_id", entity.WorkerId);
        deleteCmd.ExecuteNonQuery();

        InsertModels(entity);
    }

    public void Delete(WorkerEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM workers WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", entity.WorkerId);
        cmd.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<ModelCapability>> GetAllModelsAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT model_id, type, vram_required_gb, param_schema FROM worker_models ORDER BY type, model_id";
        var seen = new HashSet<string>();
        var models = new List<ModelCapability>();

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var modelId = reader.GetString(0);
            if (seen.Add(modelId))
            {
                models.Add(MapModelFromReader(reader));
            }
        }

        return models;
    }

    public async Task<IReadOnlyList<string>> GetWorkerModelIdsAsync(string workerId, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT model_id FROM worker_models WHERE worker_id = @worker_id";
        cmd.Parameters.AddWithValue("@worker_id", workerId);
        var ids = new List<string>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            ids.Add(reader.GetString(0));
        }
        return ids;
    }

    public async Task MarkStaleWorkersOfflineAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "UPDATE workers SET status = 'Offline' WHERE last_heartbeat < @cutoff AND status = 'Online'";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PurgeOfflineWorkersAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM workers WHERE status = 'Offline' AND last_heartbeat < @cutoff";
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<ModelCapability>> GetModelsForWorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT model_id, type, vram_required_gb, param_schema FROM worker_models WHERE worker_id = @worker_id";
        cmd.Parameters.AddWithValue("@worker_id", workerId);
        var models = new List<ModelCapability>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            models.Add(MapModelFromReader(reader));
        }
        return models;
    }

    private async Task<IReadOnlyList<WorkerEntity>> ReadWorkersWithModelsAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var workers = new List<WorkerEntity>();
        using (var reader = await cmd.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                var entity = MapWorkerFromReader(reader);
                workers.Add(entity);
            }
        }

        foreach (var worker in workers)
        {
            worker.UpdateModels(await GetModelsForWorkerAsync(worker.WorkerId, cancellationToken));
        }

        return workers;
    }

    private void InsertModels(WorkerEntity entity)
    {
        foreach (var model in entity.Models)
        {
            using var cmd = _connection.CreateCommand();
            cmd.CommandText = """
                INSERT INTO worker_models (worker_id, model_id, type, vram_required_gb, param_schema)
                VALUES (@worker_id, @model_id, @type, @vram_required_gb, @param_schema)
                """;
            cmd.Parameters.AddWithValue("@worker_id", entity.WorkerId);
            cmd.Parameters.AddWithValue("@model_id", model.ModelId);
            cmd.Parameters.AddWithValue("@type", model.Type.ToString());
            cmd.Parameters.AddWithValue("@vram_required_gb", model.VramRequiredGb);
            cmd.Parameters.AddWithValue("@param_schema", JsonSerializer.Serialize(model.ParamSchema, JsonOptions));
            cmd.ExecuteNonQuery();
        }
    }

    private static WorkerEntity MapWorkerFromReader(SqliteDataReader reader)
    {
        var workerId = reader.GetString(0);
        var name = reader.GetString(1);
        var host = reader.GetString(2);
        var status = Enum.Parse<WorkerStatus>(reader.GetString(3));
        var lastHeartbeat = DateTimeOffset.Parse(reader.GetString(4));
        var registeredAt = DateTimeOffset.Parse(reader.GetString(5));

        return WorkerEntity.Rehydrate(
            workerId, name, host, status, lastHeartbeat, registeredAt,
            new List<ModelCapability>());
    }

    private static ModelCapability MapModelFromReader(SqliteDataReader reader)
    {
        var modelId = reader.GetString(0);
        var type = Enum.Parse<Domain.Jobs.Enums.MediaType>(reader.GetString(1));
        var vramRequiredGb = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
        var paramSchemaJson = reader.IsDBNull(3) ? "{}" : reader.GetString(3);
        var paramSchema = JsonSerializer.Deserialize<Dictionary<string, object?>>(paramSchemaJson, JsonOptions) ?? [];

        return ModelCapability.Create(modelId, type, vramRequiredGb, paramSchema.AsReadOnly());
    }
}
