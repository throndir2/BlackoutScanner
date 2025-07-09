using System.IO;

namespace BlackoutScanner.Interfaces
{
    public interface IFileSystem
    {
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        string[] GetFiles(string path, string searchPattern);
        string ReadAllText(string path);
        void WriteAllText(string path, string content);
        bool FileExists(string path);
        void DeleteFile(string path);
        string Combine(params string[] paths);
    }
}
