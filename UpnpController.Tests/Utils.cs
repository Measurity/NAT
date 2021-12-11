using System.IO;

namespace UpnpController.Tests;

public static class Utils
{
    public static Stream GetContentFile(string fileName)
    {
        return File.Open(Path.Combine("Content", fileName), FileMode.Open, FileAccess.ReadWrite);
    }
}