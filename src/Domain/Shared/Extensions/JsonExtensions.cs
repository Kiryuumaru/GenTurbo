using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Domain.Shared.Extensions;

/// <summary>
/// Extension methods for navigating and extracting values from JSON documents.
/// </summary>
public static class JsonExtensions
{
    public static JsonNode GetJsonNodeOrThrow(this JsonNode node, params string[] propertyNames)
    {
        JsonNode currentNode = node;
        string currentKey = "@root";

        if (propertyNames is not null)
        {
            foreach (var propertyName in propertyNames)
            {
                if (currentNode.GetValueKind() != JsonValueKind.Object)
                    throw new InvalidOperationException($"Cannot access property '{propertyName}' because '{currentKey}' is not an object but a {currentNode.GetValueKind()}.");

                if (currentNode is JsonObject jsonObj && jsonObj.TryGetPropertyValue(propertyName, out var nextNode) && nextNode is not null)
                {
                    currentNode = nextNode;
                }
                else
                {
                    throw new KeyNotFoundException($"Property '{propertyName}' was not found in the JSON document '{currentKey}'.");
                }
                currentKey = currentKey + "." + propertyName;
            }
        }

        return currentNode;
    }

    public static void ThrowIfNone(this JsonNode node, params string[] propertyNames)
    {
        GetJsonNodeOrThrow(node, propertyNames);
    }

    public static void ThrowIfNone(this JsonElement element, params string[] propertyNames)
    {
        GetJsonElementOrThrow(element, propertyNames);
    }

    public static bool ContainsProperty(this JsonNode node, params string[] propertyNames)
    {
        try
        {
            GetJsonNodeOrThrow(node, propertyNames);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static bool ContainsProperty(this JsonElement element, params string[] propertyNames)
    {
        try
        {
            GetJsonElementOrThrow(element, propertyNames);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static JsonElement GetJsonElementOrThrow(this JsonElement element, params string[] propertyNames)
    {
        JsonElement currentElement = element;
        string currentKey = "@root";

        if (propertyNames is not null)
        {
            foreach (var propertyName in propertyNames)
            {
                if (currentElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException($"Cannot access property '{propertyName}' because '{currentKey}' is not an object but a {currentElement.ValueKind}.");

                if (currentElement.TryGetProperty(propertyName, out var nextElement))
                {
                    currentElement = nextElement;
                }
                else
                {
                    throw new KeyNotFoundException($"Property '{propertyName}' was not found in the JSON document '{currentKey}'.");
                }
                currentKey = currentKey + "." + propertyName;
            }
        }

        return currentElement;
    }

    public static bool TryGetValueOrThrow<TValue>(this JsonNode node, params string[] propertyNames)
    {
        try
        {
            var childNode = GetJsonNodeOrThrow(node, propertyNames);
            var value = childNode.GetValue<TValue>();
            if (value is not null)
                return true;
            return false;
        }
        catch
        {
            return false;
        }
    }
}
