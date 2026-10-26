using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security;
using Microsoft.Win32;

namespace UsageMonitor.Services;

[SupportedOSPlatform("windows")]
public sealed class StartupRegistration
{
    private const string ValueName = "UsageMonitor";
    private const int MaximumCommandLength = 260;
    private readonly string executablePath;
    private readonly string registrySubKey;

    public StartupRegistration(string executablePath = "", string registrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run")
    {
        this.executablePath = executablePath;
        if (executablePath.Length == 0)
            this.executablePath = Environment.ProcessPath ?? "";
        this.registrySubKey = registrySubKey;
    }

    public bool IsEnabled()
    {
        var path = ExecutablePath();
        if (path.Length == 0)
            return false;
        var expected = $"\"{path}\"";
        if (expected.Length > MaximumCommandLength)
            return false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(registrySubKey, false);
            if (key is not RegistryKey current)
                return false;
            if (current.GetValue(ValueName, "") is not string command || command.Length == 0)
                return false;
            return current.GetValueKind(ValueName) == RegistryValueKind.String
                && string.Equals(command, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException or IOException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    public bool TrySetEnabled(bool enabled, out string error)
    {
        error = "";
        try
        {
            if (enabled)
            {
                var path = ExecutablePath();
                if (path.Length == 0)
                {
                    error = "자동 실행에 등록할 Usage Monitor 실행 파일(.exe)을 확인할 수 없어요.";
                    return false;
                }

                var command = $"\"{path}\"";
                if (command.Length > MaximumCommandLength)
                {
                    error = "실행 파일 경로가 너무 길어 자동 실행에 등록할 수 없어요. 더 짧은 폴더 경로로 옮겨주세요.";
                    return false;
                }

                using var key = Registry.CurrentUser.CreateSubKey(registrySubKey, true);
                if (key is not RegistryKey current)
                {
                    error = "Windows 자동 실행 설정을 열지 못했어요.";
                    return false;
                }

                current.SetValue(ValueName, command, RegistryValueKind.String);
            }
            else
            {
                using var key = Registry.CurrentUser.OpenSubKey(registrySubKey, true);
                if (key is RegistryKey current)
                    current.DeleteValue(ValueName, false);
            }

            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or SecurityException)
        {
            error = "Windows 자동 실행 설정을 변경할 권한이 없어요.";
            return false;
        }
        catch (Exception exception) when (exception is IOException or ArgumentException or NotSupportedException)
        {
            error = "Windows 자동 실행 설정을 저장하지 못했어요. 다시 시도해주세요.";
            return false;
        }
    }

    private string ExecutablePath()
    {
        if (string.IsNullOrWhiteSpace(executablePath) || executablePath.Contains('"'))
            return "";
        try
        {
            var path = Path.GetFullPath(executablePath);
            if (string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase) == false
                || string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
                || File.Exists(path) == false)
                return "";
            return path;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SecurityException or ArgumentException or NotSupportedException)
        {
            return "";
        }
    }
}
