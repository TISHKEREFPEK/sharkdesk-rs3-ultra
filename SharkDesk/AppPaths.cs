using System;
using System.IO;

namespace SharkDesk;

public static class AppPaths
{
    public static string Data { get; } = Create();
    private static string Create()
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SharkDesk-RS3-Ultra");
        Directory.CreateDirectory(path);
        return path;
    }
}
