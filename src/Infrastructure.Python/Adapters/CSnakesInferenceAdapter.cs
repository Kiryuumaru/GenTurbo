using Application.Jobs.Interfaces.Outbound;
using CSnakes.Runtime;
using CSnakes.Runtime.Python;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Python.Adapters;

internal sealed class CSnakesInferenceAdapter : IPythonInferenceProvider, IDisposable
{
    private readonly PyObject _module;
    private readonly ILogger<CSnakesInferenceAdapter> _logger;

    public CSnakesInferenceAdapter(IPythonEnvironment pythonEnv, ILogger<CSnakesInferenceAdapter> logger)
    {
        _logger = logger;

        using (GIL.Acquire())
        {
            _logger.LogInformation("Importing Python bridge module: bridge");
            _module = Import.ImportModule("bridge");
        }
    }

    public void Dispose()
    {
        _module.Dispose();
    }

    public async Task<PythonInferenceResult> RunInferenceAsync(
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

            var filepath = GetDictString(result, "filepath");
            var actualSeed = GetDictLong(result, "seed", seed);
            var actualWidth = GetDictLong(result, "width", width);
            var actualHeight = GetDictLong(result, "height", height);

            var metadata = new Dictionary<string, object?>
            {
                ["seed"] = (int)actualSeed,
                ["width"] = (int)actualWidth,
                ["height"] = (int)actualHeight,
                ["type"] = "image"
            };

            _logger.LogInformation("Inference completed: {Path} ({Width}x{Height})", filepath, actualWidth, actualHeight);
            return new PythonInferenceResult(filepath, metadata.AsReadOnly(), true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Python inference failed for model {Model}", model);
            return new PythonInferenceResult(
                string.Empty, new Dictionary<string, object?>(), false, ex.Message, ex.GetType().Name);
        }
    }

    private PyObject CallGenerateImage(
        string prompt, long steps, double guidance, long width, long height, long seed, string lorasJson)
    {
        using (GIL.Acquire())
        {
            using var func = _module.GetAttr("generate_image");
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

    private static string GetDictString(PyObject dict, string key)
    {
        using (GIL.Acquire())
        {
            var d = dict.As<IReadOnlyDictionary<string, PyObject>>();
            return d.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        }
    }

    private static long GetDictLong(PyObject dict, string key, long defaultValue)
    {
        using (GIL.Acquire())
        {
            var d = dict.As<IReadOnlyDictionary<string, PyObject>>();
            return d.TryGetValue(key, out var v) && v is not null ? v.As<long>() : defaultValue;
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
            int i => i,
            long l => l,
            double d => (long)d,
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
            double d => d,
            float f => f,
            int i => i,
            string s when double.TryParse(s, out var parsed) => parsed,
            _ => defaultValue
        };
    }
}
