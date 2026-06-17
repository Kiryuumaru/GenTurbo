using Infrastructure.Sqlite;
using Infrastructure.Python;
using Infrastructure.Python.ZImage;
using Infrastructure.FileSystem;
using Presentation.Worker.Commands;

return await ApplicationBuilderHelpers.ApplicationBuilder.Create()
    .AddApplication<Domain.Domain>()
    .AddApplication<Application.Application>()
    .AddApplication<SqliteInfrastructure>()
    .AddApplication<PythonInfrastructure>()
    .AddApplication<ZImageInfrastructure>()
    .AddApplication<FileSystemInfrastructure>()
    .AddCommand<MainCommand>()
    .AddCommand<WorkerCommand>()
    .RunAsync(args);
