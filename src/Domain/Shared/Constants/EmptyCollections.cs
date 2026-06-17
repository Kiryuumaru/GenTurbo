using System.Collections.ObjectModel;

namespace Domain.Shared.Constants;

public static class EmptyCollections
{
    public static IReadOnlyList<string> StringList { get; } = Array.Empty<string>();

    public static IReadOnlyList<Guid> GuidList { get; } = Array.Empty<Guid>();

    public static IReadOnlyDictionary<string, string> StringStringDictionary { get; } =
        new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(0, StringComparer.Ordinal));
}
