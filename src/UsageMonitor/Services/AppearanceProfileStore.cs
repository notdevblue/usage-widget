using System;
using System.IO;
using System.Security;
using System.Text.Json;

namespace UsageMonitor.Services;

public sealed class AppearanceProfileStore
{
    private const int MaximumFileSize = 256 * 1024;

    public bool TryExport(string path, AppearanceProfile profile)
    {
        var temporaryPath = "";
        try
        {
            var fullPath = Path.GetFullPath(path);
            var json = JsonSerializer.SerializeToUtf8Bytes(AppearanceProfile.Normalize(profile), ProfileJson.Options);
            if (json.Length > MaximumFileSize)
                return false;
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? "");
            temporaryPath = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(true);
            }

            File.Move(temporaryPath, fullPath, true);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            if (temporaryPath.Length > 0)
                TryDelete(temporaryPath);
        }
    }

    public bool TryImport(string path, out AppearanceProfile profile)
    {
        profile = new();
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (stream.Length == 0 || stream.Length > MaximumFileSize)
                return false;
            using var document = JsonDocument.Parse(stream);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return false;
            var imported = document.RootElement.Deserialize<AppearanceProfile>(ProfileJson.Options) ?? new();
            if (imported.Version != 1)
                return false;
            profile = AppearanceProfile.Normalize(imported);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException)
        {
        }
    }
}
