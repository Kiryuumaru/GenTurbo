using Domain.Workers.Interfaces;
using Infrastructure.Sqlite.Adapters;

namespace Infrastructure.Sqlite.Repositories;

internal sealed class SqliteWorkerUnitOfWork(SqliteUnitOfWork unitOfWork) : IWorkerUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
