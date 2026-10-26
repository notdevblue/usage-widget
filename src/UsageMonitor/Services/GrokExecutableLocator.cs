using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace UsageMonitor.Services;

public sealed class GrokExecutableLocator
{
    private readonly string profileDirectory;

    public GrokExecutableLocator() : this("")
    {
    }

    public GrokExecutableLocator(string profileDirectory)
    {
        this.profileDirectory = profileDirectory.Length == 0
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : profileDirectory;
    }

    public ExecutableLocation Find(string preferredPath = "")
    {
        var preferred = FindExecutable(preferredPath, "Saved Grok path");
        if (preferred.Found)
            return preferred;

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TryGetFullPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in pathDirectories)
        {
            var location = FindExecutable(Path.Combine(directory, "grok.exe"), "Grok Build (PATH)");
            if (location.Found)
                return location;
        }

        return FindExecutable(Path.Combine(profileDirectory, ".grok", "bin", "grok.exe"), "Grok Build");
    }

    private static ExecutableLocation FindExecutable(string candidate, string source)
    {
        var fullPath = TryGetFullPath(candidate);
        if (fullPath.Length == 0 || !string.Equals(Path.GetFileName(fullPath), "grok.exe", StringComparison.OrdinalIgnoreCase))
            return new();
        try
        {
            if (!File.Exists(fullPath))
                return new();
            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new() { Found = true, Path = fullPath, Source = source };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return new();
        }
    }

    private static string TryGetFullPath(string path)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return expanded.Length == 0 ? "" : Path.GetFullPath(expanded);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            return "";
        }
    }
}
