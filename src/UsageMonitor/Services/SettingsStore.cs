using System;
using System.IO;
using System.Security;
using System.Text.Json;

namespace UsageMonitor.Services;

public sealed class SettingsStore
{
    public SettingsStore(string baseDirectory = "")
    {
        var directory = string.IsNullOrWhiteSpace(baseDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "UsageMonitor")
            : baseDirectory;
        FilePath = Path.Combine(Path.GetFullPath(directory), "settings.json");
    }

    public string FilePath { get; }

    public AppSettings Load()
    {
        try
        {
            var json = File.ReadAllText(FilePath);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, ProfileJson.Options) ?? new();
            return Normalize(settings);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return new();
        }
    }

    public bool TrySave(AppSettings settings)
    {
        var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath) ?? "");
            var json = JsonSerializer.SerializeToUtf8Bytes(Normalize(settings), ProfileJson.Options);
            using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                stream.Write(json);
                stream.Flush(true);
            }

            File.Move(temporaryPath, FilePath, true);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or JsonException or NotSupportedException)
        {
            return false;
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        var finitePosition = double.IsFinite(settings.Left) && double.IsFinite(settings.Top);
        var appearance = AppearanceProfile.Normalize(new()
        {
            Theme = settings.Theme,
            CustomColors = settings.CustomColors,
            Scale = settings.Scale,
            Opacity = settings.Opacity,
            Layout = settings.Layout
        });
        return settings with
        {
            FiveHourDisplay = Enum.IsDefined(settings.FiveHourDisplay) ? settings.FiveHourDisplay : FiveHourDisplay.Automatic,
            Theme = appearance.Theme,
            CustomColors = appearance.CustomColors,
            Scale = appearance.Scale,
            Opacity = appearance.Opacity,
            Layout = appearance.Layout,
            HasPosition = settings.HasPosition && finitePosition,
            Left = finitePosition ? settings.Left : 0,
            Top = finitePosition ? settings.Top : 0,
            RefreshSeconds = Math.Clamp(settings.RefreshSeconds, 30, 600),
            CodexPath = settings.CodexPath ?? "",
            ClaudePath = settings.ClaudePath ?? ""
        };
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
