using Infrastructure.Sqlite;
using Infrastructure.Python.ZImage;
using Infrastructure.FileSystem;
using Presentation.Server.Commands;

return await ApplicationBuilderHelpers.ApplicationBuilder.Create()
    .AddApplication<Domain.Domain>()
    .AddApplication<Application.Application>()
    .AddApplication<SqliteInfrastructure>()
    .AddApplication<ZImageInfrastructure>()
    .AddApplication<FileSystemInfrastructure>()
    .AddCommand<MainCommand>()
    .RunAsync(args);