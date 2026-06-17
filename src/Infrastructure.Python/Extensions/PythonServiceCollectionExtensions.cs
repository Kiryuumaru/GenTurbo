using Application.Jobs.Interfaces.Outbound;
using CSnakes.Runtime;
using Infrastructure.Python.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python.Extensions;

internal static class PythonServiceCollectionExtensions
{
    private static string ResolvePythonModulesPath()
    {
        var configured = Environment.GetEnvironmentVariable("GEN_TURBO_PYTHON_MODULES");
        if (!string.IsNullOrEmpty(configured))
            return configured;

        var baseDir = AppContext.BaseDirectory;
        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Infrastructure.Python", "PythonModules"));

        if (Directory.Exists(devPath))
            return devPath;

        return Path.Combine(baseDir, "PythonModules");
    }

    internal static IServiceCollection AddPythonServices(this IServiceCollection services)
    {
        var modulesPath = ResolvePythonModulesPath();

        services.WithPython()
            .FromFolder(modulesPath, "3.12");

        services.AddScoped<IPythonInferenceProvider, CSnakesInferenceAdapter>();
        return services;
    }
}
