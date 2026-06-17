using Microsoft.Data.Sqlite;

namespace Infrastructure.Sqlite.Adapters;

internal sealed class SqliteUnitOfWork : IDisposable, IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction? _transaction;

    public SqliteConnection Connection => _connection;

    public SqliteUnitOfWork(SqliteConnection connection)
    {
        _connection = connection;
    }

    public async Task BeginAsync(CancellationToken cancellationToken = default)
    {
        _transaction = (SqliteTransaction)await _connection.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            await _transaction.CommitAsync(cancellationToken);
            _transaction = null;
        }
    }

    public async Task RollbackAsync()
    {
        if (_transaction is not null)
        {
            await _transaction.RollbackAsync();
            _transaction = null;
        }
    }

    public void Dispose()
    {
        _transaction?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
    }
}
