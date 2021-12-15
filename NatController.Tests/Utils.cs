using System.IO;

namespace NatController.Tests;

public static class Utils
{
    public static Stream GetContentFile(string fileName)
    {
        return File.Open(Path.Combine("Content", fileName), FileMode.Open, FileAccess.ReadWrite);
    }
}