using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace UsageMonitor.Services;

public sealed record ExecutableLocation
{
    public bool Found { get; init; }
    public string Path { get; init; } = "";
    public string Source { get; init; } = "";
}

public sealed class CodexExecutableLocator
{
    public ExecutableLocation Find(string preferredPath = "")
    {
        var preferred = FindExecutable(preferredPath, "Saved Codex path");
        if (preferred.Found)
        {
            return preferred;
        }

        var pathDirectories = GetPathDirectories();
        foreach (var directory in pathDirectories)
        {
            var native = FindExecutable(Path.Combine(directory, "codex.exe"), "Codex CLI (PATH)");
            if (native.Found)
            {
                return native;
            }
        }

        var packageDirectories = pathDirectories.Concat(GetCommonDirectories()).Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in packageDirectories)
        {
            var native = FindExecutable(Path.Combine(directory, "codex.exe"), "Codex CLI");
            if (native.Found)
            {
                return native;
            }

            foreach (var candidate in GetNpmCandidates(directory))
            {
                var npm = FindExecutable(candidate, "Codex CLI (npm)");
                if (npm.Found)
                {
                    return npm;
                }
            }
        }

        foreach (var extensionDirectory in GetExtensionDirectories())
        {
            foreach (var candidate in GetExtensionCandidates(extensionDirectory))
            {
                var extension = FindExecutable(candidate, "Codex VS Code extension");
                if (extension.Found)
                {
                    return extension;
                }
            }
        }

        return new();
    }

    private static ExecutableLocation FindExecutable(string candidate, string source)
    {
        var fullPath = TryGetFullPath(candidate);
        if (fullPath.Length == 0 || !string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return new();
        }

        try
        {
            if (!File.Exists(fullPath))
            {
                return new();
            }

            using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new() { Found = true, Path = fullPath, Source = source };
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            return new();
        }
    }

    private static string[] GetPathDirectories()
    {
        return (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(TryGetFullPath)
            .Where(path => path.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] GetCommonDirectories()
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
        {
            Path.Combine(roaming, "npm"),
            Path.Combine(local, "hermes", "node"),
            Path.Combine(profile, ".local", "bin")
        };
    }

    private static IEnumerable<string> GetNpmCandidates(string directory)
    {
        var modulesDirectories = new List<string> { Path.Combine(directory, "node_modules") };
        if (string.Equals(Path.GetFileName(directory), "node_modules", StringComparison.OrdinalIgnoreCase))
        {
            modulesDirectories.Add(directory);
        }
        else if (string.Equals(Path.GetFileName(directory), ".bin", StringComparison.OrdinalIgnoreCase))
        {
            modulesDirectories.Add(Path.GetDirectoryName(directory) ?? directory);
        }

        foreach (var modulesDirectory in modulesDirectories)
        {
            var codexPackage = Path.Combine(modulesDirectory, "@openai", "codex");
            foreach (var platform in GetPlatforms())
            {
                var nestedPackage = Path.Combine(codexPackage, "node_modules", "@openai", platform.Package);
                var hoistedPackage = Path.Combine(modulesDirectory, "@openai", platform.Package);
                foreach (var platformPackage in new[] { nestedPackage, hoistedPackage, codexPackage })
                {
                    yield return Path.Combine(platformPackage, "vendor", platform.Triple, "bin", "codex.exe");
                    yield return Path.Combine(platformPackage, "vendor", platform.Triple, "codex", "codex.exe");
                    yield return Path.Combine(platformPackage, "vendor", platform.Triple, "codex.exe");
                }
            }
        }
    }

    private static string[] GetExtensionDirectories()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var directories = new List<string>();
        foreach (var application in new[] { ".vscode", ".vscode-insiders", ".cursor" })
        {
            var extensionRoot = Path.Combine(profile, application, "extensions");
            try
            {
                if (Directory.Exists(extensionRoot))
                {
                    directories.AddRange(Directory.GetDirectories(extensionRoot, "openai.chatgpt-*", SearchOption.TopDirectoryOnly));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
            {
            }
        }

        return directories.OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> GetExtensionCandidates(string directory)
    {
        var architectures = RuntimeInformation.OSArchitecture == Architecture.Arm64
            ? new[] { "windows-aarch64", "win32-arm64", "windows-x86_64", "win32-x64" }
            : new[] { "windows-x86_64", "win32-x64" };
        foreach (var architecture in architectures)
        {
            yield return Path.Combine(directory, "bin", architecture, "codex.exe");
        }

        foreach (var platform in GetPlatforms())
        {
            yield return Path.Combine(directory, "bin", platform.Triple, "codex.exe");
        }
    }

    private static (string Package, string Triple)[] GetPlatforms()
    {
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            return [("codex-win32-arm64", "aarch64-pc-windows-msvc"), ("codex-win32-x64", "x86_64-pc-windows-msvc")];
        }

        return [("codex-win32-x64", "x86_64-pc-windows-msvc")];
    }

    private static string TryGetFullPath(string path)
    {
        try
        {
            var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            return expandedPath.Length == 0 ? "" : Path.GetFullPath(expandedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or NotSupportedException or SecurityException)
        {
            return "";
        }
    }
}
