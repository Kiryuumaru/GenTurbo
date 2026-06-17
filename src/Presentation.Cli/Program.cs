using Infrastructure.Sqlite;
using Infrastructure.Python;
using Infrastructure.FileSystem;
using Presentation.Cli.Commands;

return await ApplicationBuilderHelpers.ApplicationBuilder.Create()
    .AddApplication<Domain.Domain>()
    .AddApplication<Application.Application>()
    .AddApplication<SqliteInfrastructure>()
    .AddApplication<PythonInfrastructure>()
    .AddApplication<FileSystemInfrastructure>()
    .AddCommand<MainCommand>()
    .AddCommand<WorkerCommand>()
    .RunAsync(args);
