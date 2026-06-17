namespace Application.Shared.Interfaces.Inbound;

public interface IApplicationConstants
{
    string AppName { get; }
    string AppTitle { get; }
    string AppDescription { get; }
    string Version { get; }
    string AppTag { get; }
    string BuildPayload { get; }
}
