using Application.Jobs.Interfaces.Outbound;
using Infrastructure.Python.Extensions;
using Infrastructure.Python.ZImage.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python.ZImage.Extensions;

internal static class ZImageServiceCollectionExtensions
{
    internal static IServiceCollection AddZImageServices(this IServiceCollection services)
    {
        var baseDir = AppContext.BaseDirectory;
        var modelPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Infrastructure.Python.ZImage", "PythonModules", "models", "z-image-turbo"));
        var sharedPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Infrastructure.Python", "PythonModules", "models", "shared"));

        if (!Directory.Exists(modelPath))
            modelPath = Path.Combine(baseDir, "PythonModules", "models", "z-image-turbo");
        if (!Directory.Exists(sharedPath))
            sharedPath = Path.Combine(baseDir, "PythonModules", "models", "shared");

        services.AddPythonModule(modelPath, sharedPath);
        services.AddSingleton<IPythonInferenceProvider, ZImageInferenceAdapter>();

        return services;
    }
}
