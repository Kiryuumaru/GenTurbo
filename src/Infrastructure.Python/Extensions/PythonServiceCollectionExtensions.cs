using CSnakes.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure.Python.Extensions;

/// <summary>
/// Generic CSnakes plumbing. Model-specific projects call AddPythonModule
/// to register their environment + install requirements.
/// </summary>
public static class PythonServiceCollectionExtensions
{
    /// <summary>
    /// Register a model's Python environment. The shared lib path is added
    /// to PYTHONPATH so the model can import base.py and lora.py.
    /// </summary>
    public static IServiceCollection AddPythonModule(this IServiceCollection services, string modelPath, string sharedPath)
    {
        var pythonPath = $"{modelPath}{Path.PathSeparator}{sharedPath}";
        Environment.SetEnvironmentVariable("PYTHONPATH", pythonPath);

        services.WithPython()
            .FromFolder(modelPath, "3.12")
            .WithPipInstaller(Path.Combine(modelPath, "requirements.txt"));

        return services;
    }
}
