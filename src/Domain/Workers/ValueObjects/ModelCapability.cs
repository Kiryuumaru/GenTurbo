using Domain.Jobs.Enums;

namespace Domain.Workers.ValueObjects;

public class ModelCapability
{
    public string ModelId { get; }

    public MediaType Type { get; }

    public int VramRequiredGb { get; }

    public IReadOnlyDictionary<string, object?> ParamSchema { get; }

    protected ModelCapability(string modelId, MediaType type, int vramRequiredGb, IReadOnlyDictionary<string, object?> paramSchema)
    {
        ModelId = modelId;
        Type = type;
        VramRequiredGb = vramRequiredGb;
        ParamSchema = paramSchema;
    }

    public static ModelCapability Create(string modelId, MediaType type, int vramRequiredGb, IReadOnlyDictionary<string, object?>? paramSchema = null)
    {
        ArgumentNullException.ThrowIfNull(modelId);

        return new ModelCapability(modelId, type, vramRequiredGb, paramSchema ?? new Dictionary<string, object?>());
    }
}
