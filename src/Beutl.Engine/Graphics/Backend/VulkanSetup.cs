using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Backend;

internal static class VulkanSetup
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(VulkanSetup));

    private delegate int PutenvDelegate(IntPtr name);

    private static (PutenvDelegate Delegate, IntPtr Library) GetPutenvDelegate()
    {
        if (OperatingSystem.IsMacOS())
        {
            var library = NativeLibrary.Load("libc.dylib");
            NativeLibrary.TryGetExport(library, "putenv", out IntPtr putenvPtr);
            var putenv = Marshal.GetDelegateForFunctionPointer<PutenvDelegate>(putenvPtr);
            return (putenv, library);
        }
        else if (OperatingSystem.IsLinux())
        {
            var library = NativeLibrary.Load("libc.so.6");
            NativeLibrary.TryGetExport(library, "putenv", out IntPtr putenvPtr);
            var putenv = Marshal.GetDelegateForFunctionPointer<PutenvDelegate>(putenvPtr);
            return (putenv, library);
        }
        else
        {
            throw new PlatformNotSupportedException("putenv is only supported on macOS and Linux.");
        }
    }

    private static string? FindFile(string fileName)
    {
        var baseDir = AppContext.BaseDirectory;
        // 最初にruntimes/{arch}/native配下を探す
        var arch = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => null
        };
        var os = OperatingSystem.IsWindows() ? "win" :
            OperatingSystem.IsLinux() ? "linux" :
            OperatingSystem.IsMacOS() ? "osx" : null;
        if (arch != null && os != null)
        {
            var runtimePath = Path.Combine(baseDir, "runtimes", $"{os}-{arch}", "native");
            var runtimeFile = Path.Combine(runtimePath, fileName);
            if (File.Exists(runtimeFile))
            {
                return runtimeFile;
            }
        }

        return Directory.EnumerateFiles(baseDir, "*.*", SearchOption.AllDirectories)
            .FirstOrDefault(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
    }

    private static void UpdateEnv(List<string> prepend, List<string> append, string varName)
    {
        var currentValue = Environment.GetEnvironmentVariable(varName) ?? string.Empty;
        var values = new List<string>();

        values.AddRange(prepend);

        if (!string.IsNullOrEmpty(currentValue))
        {
            values.AddRange(currentValue.Split(Path.PathSeparator));
        }

        values.AddRange(append);

        var newValue = string.Join(Path.PathSeparator.ToString(), values.Distinct());
        if (OperatingSystem.IsWindows())
        {
            Environment.SetEnvironmentVariable(varName, newValue);
        }
        else
        {
            var (putenv, library) = GetPutenvDelegate();
            var envVar = $"{varName}={newValue}";
            var envPtr = Marshal.StringToHGlobalAnsi(envVar);
            try
            {
                putenv(envPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(envPtr);
            }
        }
    }

    public static void Setup()
    {
        try
        {
            var moltenVkIcd = OperatingSystem.IsMacOS() ? FindFile("MoltenVK_icd.json") : null;
            var swiftShaderIcd = FindFile("vk_swiftshader_icd.json");

            var prependPaths = new List<string>();
            var appendPaths = new List<string>();

            if (moltenVkIcd != null)
                appendPaths.Add(moltenVkIcd);

            if (swiftShaderIcd != null)
                prependPaths.Add(swiftShaderIcd);

            if (OperatingSystem.IsMacOS())
            {
                // https://stackoverflow.com/questions/79476650/vulkan-on-macos-with-multiple-drivers
                UpdateEnv(prependPaths, appendPaths, "VK_DRIVER_FILES");
            }
            else
            {
                UpdateEnv(prependPaths, appendPaths, "VK_ADD_DRIVER_FILES");
            }

            s_logger.LogDebug("MoltenVK environment setup completed successfully");
        }
        catch (Exception ex)
        {
            s_logger.LogError("Failed to setup MoltenVK environment: {ExMessage}", ex.Message);
        }
    }
}
