using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;

namespace UsageMonitor.Services;

public sealed class ClaudeExecutableLocator
{
    public ExecutableLocation Find(string preferredPath = "")
    {
        var preferred = FindExecutable(preferredPath, "Saved Claude path");
        if (preferred.Found)
            return preferred;

        var pathDirectories = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TryGetFullPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in pathDirectories)
        {
            var location = FindExecutable(Path.Combine(directory, "claude.exe"), "Claude Code (PATH)");
            if (location.Found)
                return location;
        }

        foreach (var directory in GetInstallDirectories())
        {
            var location = FindExecutable(Path.Combine(directory, "claude.exe"), "Claude Code");
            if (location.Found)
                return location;
        }

        return new();
    }

    private static ExecutableLocation FindExecutable(string candidate, string source)
    {
        var fullPath = TryGetFullPath(candidate);
        if (fullPath.Length == 0 || !string.Equals(Path.GetFileName(fullPath), "claude.exe", StringComparison.OrdinalIgnoreCase))
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

    private static IEnumerable<string> GetInstallDirectories()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(profile, ".local", "bin");
        yield return Path.Combine(local, "Microsoft", "WinGet", "Links");
        yield return Path.Combine(programFiles, "WinGet", "Links");

        foreach (var root in new[] { Path.Combine(local, "Microsoft", "WinGet", "Packages"), Path.Combine(programFiles, "WinGet", "Packages") })
        {
            foreach (var package in GetPackageDirectories(root))
                yield return package;
        }
    }

    private static string[] GetPackageDirectories(string root)
    {
        try
        {
            return Directory.Exists(root)
                ? Directory.GetDirectories(root, "Anthropic.ClaudeCode_*", SearchOption.TopDirectoryOnly)
                : [];
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
            return [];
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
