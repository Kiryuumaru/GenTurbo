namespace Application.Workers.Interfaces.Outbound;

public interface IFileStorage
{
    string Save(string fileName, byte[] data);

    bool Delete(string url);

    bool Exists(string url);

    string ResolvePath(string url);
}
