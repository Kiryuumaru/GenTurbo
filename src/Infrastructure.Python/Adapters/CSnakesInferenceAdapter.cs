using Application.Jobs.Interfaces.Outbound;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace Infrastructure.Python.Adapters;

internal sealed class CSnakesInferenceAdapter(ILogger<CSnakesInferenceAdapter> logger) : IPythonInferenceProvider
{
    public async Task<PythonInferenceResult> RunInferenceAsync(
        string model,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting inference for model {Model}", model);

        try
        {
            var prompt = GetStringParam(parameters, "prompt")
                ?? throw new InvalidOperationException("prompt is required");

            var argsJson = JsonSerializer.Serialize(parameters);
            var psi = new ProcessStartInfo
            {
                FileName = "python3",
                Arguments = $"-m app.worker.csnakes_bridge generate --model {EscapeArg(model)} --params {EscapeArg(argsJson)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = ResolvePythonPath()
            };

            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start Python process");

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                logger.LogError("Python inference failed: {Error}", error);
                return new PythonInferenceResult(
                    string.Empty, new Dictionary<string, object?>(), false, error, "PYTHON_ERROR");
            }

            var result = JsonSerializer.Deserialize<Dictionary<string, object?>>(output);
            if (result is null)
                return new PythonInferenceResult(
                    string.Empty, new Dictionary<string, object?>(), false, "Invalid output", "PARSE_ERROR");

            var filepath = result.TryGetValue("filepath", out var fp) ? fp?.ToString() ?? string.Empty : string.Empty;

            logger.LogInformation("Inference completed: {Path}", filepath);
            return new PythonInferenceResult(filepath, result.AsReadOnly(), true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Python inference failed for model {Model}", model);
            return new PythonInferenceResult(
                string.Empty, new Dictionary<string, object?>(), false, ex.Message, ex.GetType().Name);
        }
    }

    private static string ResolvePythonPath()
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

    private static string EscapeArg(string arg)
    {
        return arg.Replace("\"", "\\\"");
    }

    private static string? GetStringParam(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
