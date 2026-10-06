using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Beutl.Extensions.FFmpeg.Properties;
using Microsoft.Extensions.Logging;

namespace Beutl.Extensions.FFmpeg;

public partial class FFmpegInstallService
{
    [SupportedOSPlatform("macos")]
    private async Task InstallWithHomebrewAsync(HttpClient client)
    {
        CancellationToken ct = _cts.Token;
        ProgressTextChanged?.Invoke(Strings.Installing_FFmpeg_via_Homebrew);
        IndeterminateChanged?.Invoke(true);

        _logger.LogInformation("Installing FFmpeg via Homebrew");

        // Check if brew is available
        string? brewPath = GetBrewPath();
        if (brewPath == null)
        {
            _logger.LogInformation("Homebrew not found, attempting to install");
            ProgressTextChanged?.Invoke(Strings.Homebrew_not_found_installing);

            bool homebrewInstalled = await InstallHomebrewAsync(client);
            if (!homebrewInstalled)
            {
                Completed?.Invoke(false);
                return;
            }

            brewPath = GetBrewPath();
            if (brewPath == null)
            {
                _logger.LogError("Homebrew still not found after installation");
                ProgressTextChanged?.Invoke(Strings.Homebrew_not_found);
                Completed?.Invoke(false);
                return;
            }
        }

        ProgressTextChanged?.Invoke(Strings.Installing_FFmpeg_via_Homebrew);

        // Get askpass script path for brew install
        string askPassPath = GetAskPassScriptPath();

        // Run: brew install ffmpeg@8
        ProcessStartInfo psi = new(brewPath)
        {
            ArgumentList = { "install", "ffmpeg@8" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Environment =
            {
                ["NONINTERACTIVE"] = "1", // Set SUDO_ASKPASS for brew install (may need sudo for some operations)
                ["SUDO_ASKPASS"] = askPassPath
            }
        };

        try
        {
            using Process? process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start brew process");
            }

            // Read output asynchronously
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Homebrew installation failed: {Error}", error);
                ProgressTextChanged?.Invoke(string.Format(Strings.Installation_failed, error));
                Completed?.Invoke(false);
                return;
            }

            _logger.LogInformation("Homebrew installation completed: {Output}", output);

            // Verify FFmpeg installation
            await VerifyAndCompleteAsync();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to run Homebrew");
            ProgressTextChanged?.Invoke(string.Format(Strings.Failed_to_run_Homebrew, ex.Message));
            Completed?.Invoke(false);
        }
    }

    private static string? GetBrewPath()
    {
        // Check common Homebrew locations
        string[] paths = RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => ["/opt/homebrew/bin/brew"],
            _ => ["/usr/local/bin/brew"]
        };

        foreach (string path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        // Try to find brew in PATH
        try
        {
            ProcessStartInfo psi = new("which")
            {
                Arguments = "brew",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using Process? process = Process.Start(psi);
            if (process != null)
            {
                string result = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode == 0 && File.Exists(result))
                    return result;
            }
        }
        catch
        {
            // Ignore
        }

        return null;
    }

    [SupportedOSPlatform("macos")]
    private static string GetAskPassScriptPath()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Beutl");
        Directory.CreateDirectory(tempDir);
        string scriptPath = Path.Combine(tempDir, "askpass.js");

        // Extract embedded askpass.js to temp file
        using Stream? resourceStream = typeof(FFmpegInstallService).Assembly
            .GetManifestResourceStream("askpass.js");

        if (resourceStream != null)
        {
            using FileStream fileStream = File.Create(scriptPath);
            resourceStream.CopyTo(fileStream);
        }

        // Make the script executable
        try
        {
            AddExecutablePermission(scriptPath);
        }
        catch
        {
            // Ignore chmod errors
        }

        return scriptPath;
    }

    [SupportedOSPlatform("macos")]
    private static void AddExecutablePermission(string filePath)
    {
        File.SetUnixFileMode(
            filePath,
            File.GetUnixFileMode(filePath)
            | UnixFileMode.UserExecute
            | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute);
    }

    [SupportedOSPlatform("macos")]
    private async Task<bool> InstallHomebrewAsync(HttpClient client)
    {
        CancellationToken ct = _cts.Token;
        ProgressTextChanged?.Invoke(Strings.Installing_Homebrew);
        IndeterminateChanged?.Invoke(true);

        _logger.LogInformation("Installing Homebrew");

        string askPassPath = GetAskPassScriptPath();

        try
        {
            // Download the Homebrew install script
            string installScript = await client.GetStringAsync(
                "https://raw.githubusercontent.com/Homebrew/install/HEAD/install.sh", ct);

            // Save install script to temp file
            string tempScriptPath = Path.Combine(Path.GetTempPath(), "Beutl", "brew_install.sh");
            await File.WriteAllTextAsync(tempScriptPath, installScript, ct);
            AddExecutablePermission(tempScriptPath);

            // Run the Homebrew install script with SUDO_ASKPASS and NONINTERACTIVE
            ProcessStartInfo psi = new("/bin/bash")
            {
                Arguments = $"-c \"{tempScriptPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment =
                {
                    // Set environment variables for non-interactive install with GUI password prompt
                    ["NONINTERACTIVE"] = "1",
                    ["SUDO_ASKPASS"] = askPassPath
                }
            };

            using Process? process = Process.Start(psi);
            if (process == null)
            {
                throw new InvalidOperationException("Failed to start Homebrew install process");
            }

            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

            await process.WaitForExitAsync(ct);

            string output = await outputTask;
            string error = await errorTask;

            if (process.ExitCode != 0)
            {
                _logger.LogError("Homebrew installation failed: {Error}", error);
                ProgressTextChanged?.Invoke(string.Format(Strings.Homebrew_installation_failed, error));
                return false;
            }

            _logger.LogInformation("Homebrew installation completed: {Output}", output);

            // Verify Homebrew was installed
            string? brewPath = GetBrewPath();
            if (brewPath == null)
            {
                _logger.LogError("Homebrew not found after installation");
                ProgressTextChanged?.Invoke(Strings.Homebrew_not_found_after_installation);
                return false;
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to install Homebrew");
            ProgressTextChanged?.Invoke(string.Format(Strings.Failed_to_install_Homebrew, ex.Message));
            return false;
        }
    }
}
