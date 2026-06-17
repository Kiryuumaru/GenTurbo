using Domain.Jobs.Interfaces;
using Infrastructure.Sqlite.Adapters;

namespace Infrastructure.Sqlite.Repositories;

internal sealed class SqliteJobUnitOfWork(SqliteUnitOfWork unitOfWork) : IJobUnitOfWork
{
    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
