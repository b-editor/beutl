using System.Reflection;
using System.Runtime.InteropServices;
using Beutl.Logging;
using Microsoft.Extensions.Logging;

namespace Beutl.Graphics.Backend;

internal static class MacOSVulkanSetup
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(MacOSVulkanSetup));

    private delegate int PutenvDelegate(IntPtr name);

    public static void SetupMoltenVK()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return; // Not on macOS, no setup needed
        }

        try
        {
            // Get the directory where the current assembly is located
            var assemblyLocation = Assembly.GetExecutingAssembly().Location;
            var execDir = Path.GetDirectoryName(assemblyLocation);

            // For tests, we need to look in the test output directory
            if (string.IsNullOrEmpty(execDir))
            {
                execDir = AppDomain.CurrentDomain.BaseDirectory;
            }

            if (string.IsNullOrEmpty(execDir))
            {
                return;
            }

            // Look for MoltenVK library
            var moltenVKPath = Path.Combine(execDir, "runtimes", "osx", "native", "libMoltenVK.dylib");

            s_logger.LogDebug("Looking for MoltenVK at: {MoltenVkPath}", moltenVKPath);

            if (!File.Exists(moltenVKPath))
            {
                s_logger.LogDebug("MoltenVK library not found at expected path.");
                // Try alternative paths
                var altPaths = new[]
                {
                    Path.Combine(execDir, "libMoltenVK.dylib"),
                    "/usr/local/lib/libMoltenVK.dylib",
                    "/opt/homebrew/lib/libMoltenVK.dylib"
                };

                foreach (var altPath in altPaths)
                {
                    if (File.Exists(altPath))
                    {
                        moltenVKPath = altPath;
                        s_logger.LogDebug("Found MoltenVK at alternative path: {MoltenVkPath}", moltenVKPath);
                        break;
                    }
                }

                if (!File.Exists(moltenVKPath))
                {
                    s_logger.LogDebug("MoltenVK library not found in any expected location!");
                    return;
                }
            }

            // Look for existing MoltenVK ICD file in the same directory as the library
            var moltenVKDir = Path.GetDirectoryName(moltenVKPath);
            var moltenVKIcdPath = Path.Combine(moltenVKDir!, "MoltenVK_icd.json");

            if (!File.Exists(moltenVKIcdPath))
            {
                s_logger.LogDebug("MoltenVK ICD file not found at expected location: {MoltenVkIcdPath}", moltenVKIcdPath);

                // Try alternative ICD locations
                var altIcdPaths = new[]
                {
                    Path.Combine(execDir, "vulkan", "icd.d", "MoltenVK_icd.json"),
                    Path.Combine(execDir, "runtimes", "osx", "native", "MoltenVK_icd.json")
                };

                foreach (var altPath in altIcdPaths)
                {
                    if (File.Exists(altPath))
                    {
                        moltenVKIcdPath = altPath;
                        s_logger.LogDebug("Found MoltenVK ICD at alternative path: {MoltenVkIcdPath}", moltenVKIcdPath);
                        break;
                    }
                }

                // If still not found, create one
                if (!File.Exists(moltenVKIcdPath))
                {
                    var icdDir = Path.Combine(execDir, "vulkan", "icd.d");
                    if (!Directory.Exists(icdDir))
                    {
                        Directory.CreateDirectory(icdDir);
                    }
                    moltenVKIcdPath = Path.Combine(icdDir, "MoltenVK_icd.json");
                    CreateMoltenVKIcdFile(moltenVKIcdPath, moltenVKPath);
                }
            }
            else
            {
                s_logger.LogDebug("Found existing MoltenVK ICD file: {MoltenVkIcdPath}", moltenVKIcdPath);
            }

            // Set environment variables for Vulkan loader using native putenv
            // Environment.SetEnvironmentVariable doesn't affect native libraries
            var library = NativeLibrary.Load("libc.dylib");
            NativeLibrary.TryGetExport(library, "putenv", out IntPtr putenvPtr);
            if (putenvPtr == IntPtr.Zero)
            {
                s_logger.LogDebug("Failed to get putenv function from libc.dylib");
                return;
            }
            var putenv = Marshal.GetDelegateForFunctionPointer<PutenvDelegate>(putenvPtr);
            var icdEnvVar = $"VK_ICD_FILENAMES={moltenVKIcdPath}";
            var driverEnvVar = $"VK_DRIVER_FILES={moltenVKIcdPath}";
            var icdEnvPtr = Marshal.StringToHGlobalAnsi(icdEnvVar);
            var driverEnvPtr = Marshal.StringToHGlobalAnsi(driverEnvVar);
            putenv(icdEnvPtr);
            putenv(driverEnvPtr);
            NativeLibrary.Free(library);

            // Set library paths for macOS dynamic linker
            var libDir = Path.GetDirectoryName(moltenVKPath);
            if (!string.IsNullOrEmpty(libDir))
            {
                Environment.SetEnvironmentVariable("DYLD_LIBRARY_PATH", libDir);
                Environment.SetEnvironmentVariable("DYLD_FALLBACK_LIBRARY_PATH", libDir);
            }

            s_logger.LogDebug("MoltenVK environment setup completed successfully");
        }
        catch (Exception ex)
        {
            s_logger.LogError("Failed to setup MoltenVK environment: {ExMessage}", ex.Message);
        }
    }

    // TODO: .app内のディレクトリに書き込まれてしまうと署名が壊れる可能性があるため、適切な場所に変更する
    private static void CreateMoltenVKIcdFile(string icdPath, string libraryPath)
    {
        var icdContent = $$"""
{
    "file_format_version": "1.0.0",
    "ICD": {
        "library_path": "{{libraryPath}}",
        "api_version": "1.3.0",
        "is_portability_driver": true
    }
}
""";

        try
        {
            File.WriteAllText(icdPath, icdContent);
            s_logger.LogDebug("Created MoltenVK ICD file at: {IcdPath}", icdPath);
        }
        catch (Exception ex)
        {
            s_logger.LogDebug("Failed to create MoltenVK ICD file: {ExMessage}", ex.Message);
        }
    }
}
