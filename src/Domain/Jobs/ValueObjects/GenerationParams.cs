using Domain.Shared.Models;

namespace Domain.Jobs.ValueObjects;

public class GenerationParams : ValueObject
{
    public IReadOnlyDictionary<string, object?> Values { get; }

    protected GenerationParams(IReadOnlyDictionary<string, object?> values)
    {
        Values = values;
    }

    public static GenerationParams Create(IReadOnlyDictionary<string, object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new GenerationParams(new Dictionary<string, object?>(values));
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Values.Count;
        foreach (var kvp in Values.OrderBy(x => x.Key))
        {
            yield return kvp.Key;
            yield return kvp.Value;
        }
    }
}
