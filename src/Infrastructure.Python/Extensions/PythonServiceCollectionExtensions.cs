using Application.Jobs.Interfaces.Outbound;
using CSnakes.Runtime;
using Infrastructure.Python.Adapters;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python.Extensions;

internal static class PythonServiceCollectionExtensions
{
    private static string ResolvePythonModulesPath()
    {
        var configured = Environment.GetEnvironmentVariable("GEN_TURBO_PYTHON_MODELS");
        if (!string.IsNullOrEmpty(configured))
            return configured;

        var baseDir = AppContext.BaseDirectory;
        var devPath = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "Infrastructure.Python", "PythonModules", "models"));

        if (Directory.Exists(devPath))
            return devPath;

        return Path.Combine(baseDir, "PythonModules", "models");
    }

    internal static IServiceCollection AddPythonServices(this IServiceCollection services)
    {
        var modelsPath = ResolvePythonModulesPath();
        var modelIds = Directory.EnumerateDirectories(modelsPath)
            .Select(Path.GetFileName)
            .Where(d => d != "shared")
            .ToList();

        foreach (var modelId in modelIds)
        {
            var modelPath = Path.Combine(modelsPath, modelId);
            var sharedPath = Path.Combine(modelsPath, "shared");
            var pythonPath = $"{modelPath}{Path.PathSeparator}{sharedPath}";

            Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);

            services.WithPython()
                .FromFolder(modelPath, "3.12")
                .WithPipInstaller(Path.Combine(modelPath, "requirements.txt"));
        }

        services.AddSingleton<IPythonInferenceProvider, CSnakesInferenceAdapter>();
        return services;
    }
}
