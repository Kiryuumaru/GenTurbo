using Application.Jobs.Interfaces.Outbound;
using Application.Jobs.Models;
using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Infrastructure.Python.Adapters;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Python.ZImage.Adapters;

internal sealed class ZImageInferenceAdapter : IInferenceProvider, IDisposable
{
    private readonly PythonModuleRunner _runner;
    private readonly ILogger<ZImageInferenceAdapter> _logger;

    public ZImageInferenceAdapter(IPythonEnvironment pythonEnv, ILogger<ZImageInferenceAdapter> logger)
    {
        _logger = logger;
        _logger.LogInformation("Loading Z-Image-Turbo inference engine");
        _runner = new PythonModuleRunner(pythonEnv, "bridge");
    }

    public void Dispose()
    {
        _runner.Dispose();
    }

    public async Task<InferenceResult> RunInferenceAsync(
        string model,
        IReadOnlyDictionary<string, object?> parameters,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting inference for model {Model}", model);

        try
        {
            await Task.Yield();

            var prompt = GetStringParam(parameters, "prompt")
                ?? throw new InvalidOperationException("prompt is required");
            var steps = GetLongParam(parameters, "num_inference_steps", 9L);
            var guidance = GetDoubleParam(parameters, "guidance_scale", 0.0);
            var width = GetLongParam(parameters, "width", 1024L);
            var height = GetLongParam(parameters, "height", 1024L);
            var seed = GetLongParam(parameters, "seed", -1L);

            var lorasJson = "[]";
            if (parameters.TryGetValue("loras", out var lorasValue) && lorasValue is not null)
                lorasJson = JsonSerializer.Serialize(lorasValue);

            cancellationToken.ThrowIfCancellationRequested();

            using var result = CallGenerateImage(prompt, steps, guidance, width, height, seed, lorasJson);

            var filepath = PythonModuleRunner.GetDictString(result, "filepath");
            var actualSeed = PythonModuleRunner.GetDictLong(result, "seed", seed);
            var actualWidth = PythonModuleRunner.GetDictLong(result, "width", width);
            var actualHeight = PythonModuleRunner.GetDictLong(result, "height", height);

            var metadata = new Dictionary<string, object?>
            {
                ["seed"] = (int)actualSeed,
                ["width"] = (int)actualWidth,
                ["height"] = (int)actualHeight,
                ["type"] = "image"
            };

            _logger.LogInformation("Inference completed: {Path} ({Width}x{Height})", filepath, actualWidth, actualHeight);
            return new InferenceResult(filepath, metadata.AsReadOnly(), true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Python inference failed for model {Model}", model);
            return new InferenceResult(
                string.Empty, new Dictionary<string, object?>(), false, ex.Message, ex.GetType().Name);
        }
    }

    private PyObject CallGenerateImage(
        string prompt, long steps, double guidance, long width, long height, long seed, string lorasJson)
    {
        using (GIL.Acquire())
        {
            using var func = _runner.GetFunction("generate_image");
            using var pPrompt = PyObject.From(prompt);
            using var pSteps = PyObject.From(steps);
            using var pGuidance = PyObject.From(guidance);
            using var pWidth = PyObject.From(width);
            using var pHeight = PyObject.From(height);
            using var pSeed = PyObject.From(seed);
            using var pLorasJson = PyObject.From(lorasJson);

            return func.Call(pPrompt, pSteps, pGuidance, pWidth, pHeight, pSeed, pLorasJson);
        }
    }

    private static string? GetStringParam(IReadOnlyDictionary<string, object?> parameters, string key)
    {
        return parameters.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private static long GetLongParam(IReadOnlyDictionary<string, object?> parameters, string key, long defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return defaultValue;
        return value switch
        {
            int i => i, long l => l, double d => (long)d,
            string s when long.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }

    private static double GetDoubleParam(IReadOnlyDictionary<string, object?> parameters, string key, double defaultValue)
    {
        if (!parameters.TryGetValue(key, out var value) || value is null)
            return defaultValue;
        return value switch
        {
            double d => d, float f => f, int i => i,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
