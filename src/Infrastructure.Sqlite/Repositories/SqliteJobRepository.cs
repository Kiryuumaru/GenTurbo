using Domain.Jobs.Entities;
using Domain.Jobs.Enums;
using Domain.Jobs.Interfaces;
using Domain.Jobs.ValueObjects;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace Infrastructure.Sqlite.Repositories;

internal sealed class SqliteJobRepository : IJobRepository
{
    private readonly SqliteConnection _connection;

    private const string CreateTableSql = """
        CREATE TABLE IF NOT EXISTS jobs (
            id TEXT PRIMARY KEY,
            model TEXT NOT NULL,
            type TEXT NOT NULL,
            status TEXT NOT NULL DEFAULT 'InQueue',
            params TEXT NOT NULL,
            output_url TEXT,
            error TEXT,
            error_type TEXT,
            worker_id TEXT,
            created_at TEXT NOT NULL,
            started_at TEXT,
            completed_at TEXT,
            cancelled_at TEXT,
            inference_time_s REAL,
            priority INTEGER DEFAULT 0
        );
        CREATE INDEX IF NOT EXISTS idx_jobs_status ON jobs(status);
        CREATE INDEX IF NOT EXISTS idx_jobs_model ON jobs(model);
        CREATE INDEX IF NOT EXISTS idx_jobs_type ON jobs(type);
        CREATE INDEX IF NOT EXISTS idx_jobs_created ON jobs(created_at);
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public SqliteJobRepository(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task InitializeAsync()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = CreateTableSql;
        await cmd.ExecuteNonQueryAsync();
    }

    public void Add(JobEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO jobs (id, model, type, status, params, output_url, error, error_type, worker_id,
                              created_at, started_at, completed_at, cancelled_at, inference_time_s, priority)
            VALUES (@id, @model, @type, @status, @params, @output_url, @error, @error_type, @worker_id,
                    @created_at, @started_at, @completed_at, @cancelled_at, @inference_time_s, @priority)
            """;
        BindParameters(cmd, entity);
        cmd.ExecuteNonQuery();
    }

    public async Task<JobEntity?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", id.ToString());
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFromReader(reader) : null;
    }

    public async Task<JobEntity?> GetNextPendingAsync(IReadOnlyList<string> capableModels, CancellationToken cancellationToken = default)
    {
        if (capableModels.Count == 0)
            return null;

        var modelsList = string.Join(",", capableModels.Select(m => $"'{m}'"));

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = $"""
            UPDATE jobs SET status = 'Assigned', worker_id = 'system', started_at = @now
            WHERE id = (
                SELECT id FROM jobs
                WHERE status = 'InQueue' AND model IN ({modelsList})
                ORDER BY priority DESC, created_at ASC LIMIT 1
            )
            """;
        cmd.Parameters.AddWithValue("@now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        using var selectCmd = _connection.CreateCommand();
        selectCmd.CommandText = "SELECT * FROM jobs WHERE worker_id = 'system' AND status = 'Assigned' ORDER BY created_at ASC LIMIT 1";
        using var reader = await selectCmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? MapFromReader(reader) : null;
    }

    public async Task<IReadOnlyList<JobEntity>> GetByStatusAsync(JobStatus status, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM jobs WHERE status = @status ORDER BY created_at DESC";
        cmd.Parameters.AddWithValue("@status", status.ToString());
        return await ReadListAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<JobEntity>> GetExpiredJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM jobs
            WHERE status IN ('Completed', 'Cancelled')
            AND (completed_at < @cutoff OR cancelled_at < @cutoff)
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        return await ReadListAsync(cmd, cancellationToken);
    }

    public async Task<IReadOnlyList<JobEntity>> GetStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT * FROM jobs
            WHERE status IN ('Assigned', 'InProgress')
            AND started_at < @cutoff
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        return await ReadListAsync(cmd, cancellationToken);
    }

    public async Task RequeueStaleJobsAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE jobs SET status = 'InQueue', worker_id = NULL, started_at = NULL
            WHERE status IN ('Assigned', 'InProgress') AND started_at < @cutoff
            """;
        cmd.Parameters.AddWithValue("@cutoff", cutoff.ToString("O"));
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<JobEntity> Jobs, int TotalCount)> ListAsync(
        JobStatus? status,
        string? model,
        MediaType? type,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        if (status.HasValue)
        {
            conditions.Add("status = @status");
        }
        if (model is not null)
        {
            conditions.Add("model = @model");
        }
        if (type.HasValue)
        {
            conditions.Add("type = @type");
        }

        var where = conditions.Count > 0 ? $"WHERE {string.Join(" AND ", conditions)}" : "";

        using var countCmd = _connection.CreateCommand();
        countCmd.CommandText = $"SELECT COUNT(*) FROM jobs {where}";
        AddFilterParams(countCmd, status, model, type);
        var totalCount = Convert.ToInt32(await countCmd.ExecuteScalarAsync(cancellationToken));

        using var listCmd = _connection.CreateCommand();
        listCmd.CommandText = $"SELECT * FROM jobs {where} ORDER BY created_at DESC LIMIT @limit OFFSET @offset";
        AddFilterParams(listCmd, status, model, type);
        listCmd.Parameters.AddWithValue("@limit", limit);
        listCmd.Parameters.AddWithValue("@offset", offset);

        var jobs = await ReadListAsync(listCmd, cancellationToken);
        return (jobs, totalCount);
    }

    public void Update(JobEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE jobs SET model = @model, type = @type, status = @status, params = @params,
                   output_url = @output_url, error = @error, error_type = @error_type,
                   worker_id = @worker_id, started_at = @started_at, completed_at = @completed_at,
                   cancelled_at = @cancelled_at, inference_time_s = @inference_time_s, priority = @priority
            WHERE id = @id
            """;
        BindParameters(cmd, entity);
        cmd.ExecuteNonQuery();
    }

    public void Delete(JobEntity entity)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "DELETE FROM jobs WHERE id = @id";
        cmd.Parameters.AddWithValue("@id", entity.Id.ToString());
        cmd.ExecuteNonQuery();
    }

    private static async Task<IReadOnlyList<JobEntity>> ReadListAsync(SqliteCommand cmd, CancellationToken cancellationToken)
    {
        var results = new List<JobEntity>();
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(MapFromReader(reader));
        }
        return results;
    }

    private static JobEntity MapFromReader(SqliteDataReader reader)
    {
        var id = Guid.Parse(reader.GetString(0));
        var model = reader.GetString(1);
        var type = Enum.Parse<MediaType>(reader.GetString(2));
        var status = Enum.Parse<JobStatus>(reader.GetString(3));
        var paramsJson = reader.GetString(4);
        var paramsDict = JsonSerializer.Deserialize<Dictionary<string, object?>>(paramsJson, JsonOptions) ?? [];
        var generationParams = GenerationParams.Create(paramsDict.AsReadOnly());

        var entity = JobEntity.Rehydrate(
            id,
            model,
            type,
            status,
            generationParams,
            reader.IsDBNull(5) ? null : reader.GetString(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            DateTimeOffset.Parse(reader.GetString(9)),
            reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10)),
            reader.IsDBNull(11) ? null : DateTimeOffset.Parse(reader.GetString(11)),
            reader.IsDBNull(12) ? null : DateTimeOffset.Parse(reader.GetString(12)),
            reader.IsDBNull(13) ? null : reader.GetDouble(13),
            reader.IsDBNull(14) ? 0 : reader.GetInt32(14));

        return entity;
    }

    private static void BindParameters(SqliteCommand cmd, JobEntity entity)
    {
        cmd.Parameters.AddWithValue("@id", entity.Id.ToString());
        cmd.Parameters.AddWithValue("@model", entity.Model);
        cmd.Parameters.AddWithValue("@type", entity.Type.ToString());
        cmd.Parameters.AddWithValue("@status", entity.Status.ToString());
        cmd.Parameters.AddWithValue("@params", JsonSerializer.Serialize(entity.Params.Values, JsonOptions));
        cmd.Parameters.AddWithValue("@output_url", (object?)entity.OutputUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error", (object?)entity.Error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@error_type", (object?)entity.ErrorType ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@worker_id", (object?)entity.WorkerId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@created_at", entity.CreatedAt.ToString("O"));
        cmd.Parameters.AddWithValue("@started_at", (object?)entity.StartedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@completed_at", (object?)entity.CompletedAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cancelled_at", (object?)entity.CancelledAt?.ToString("O") ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@inference_time_s", (object?)entity.InferenceTimeSeconds ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@priority", entity.Priority);
    }

    private static void AddFilterParams(SqliteCommand cmd, JobStatus? status, string? model, MediaType? type)
    {
        if (status.HasValue)
            cmd.Parameters.AddWithValue("@status", status.Value.ToString());
        if (model is not null)
            cmd.Parameters.AddWithValue("@model", model);
        if (type.HasValue)
            cmd.Parameters.AddWithValue("@type", type.Value.ToString());
    }
}
