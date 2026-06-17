using CSnakes.Runtime;
using CSnakes.Runtime.Python;

namespace Infrastructure.Python.Adapters;

/// <summary>
/// Generic CSnakes module runner. Imports a Python module and provides helpers
/// for calling functions and extracting values from dict results.
/// Model-specific adapters use this to bridge C# ↔ Python.
/// </summary>
public sealed class PythonModuleRunner : IDisposable
{
    private readonly PyObject _module;

    public PythonModuleRunner(IPythonEnvironment env, string moduleName)
    {
        using (GIL.Acquire())
        {
            _module = Import.ImportModule(moduleName);
        }
    }

    public void Dispose()
    {
        _module.Dispose();
    }

    /// <summary>Get a Python function by name. Caller must GIL.Acquire().</summary>
    public PyObject GetFunction(string functionName)
    {
        return _module.GetAttr(functionName);
    }

    /// <summary>Get a string value from a dict-like PyObject.</summary>
    public static string GetDictString(PyObject dict, string key)
    {
        using (GIL.Acquire())
        {
            var d = dict.As<IReadOnlyDictionary<string, PyObject>>();
            return d.TryGetValue(key, out var v) ? v?.ToString() ?? string.Empty : string.Empty;
        }
    }

    /// <summary>Get a long value from a dict-like PyObject.</summary>
    public static long GetDictLong(PyObject dict, string key, long defaultValue)
    {
        using (GIL.Acquire())
        {
            var d = dict.As<IReadOnlyDictionary<string, PyObject>>();
            return d.TryGetValue(key, out var v) && v is not null ? v.As<long>() : defaultValue;
        }
    }
}
