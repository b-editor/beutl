using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using Beutl.Graphics.Backend;
using Beutl.Graphics.Rendering;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace Beutl.GpuPassTargetBenchmarkHarness;

internal sealed class TargetEvidenceFingerprint
{
    public string OsDescription { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsBuild { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string ProcessArchitecture { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string FrameworkDescription { get; init; } = string.Empty;
    public string EnvironmentVersion { get; init; } = string.Empty;
    public string RendererBackend { get; init; } = string.Empty;
    public string SkiaBackend { get; init; } = string.Empty;
    public string DeviceSelection { get; init; } = string.Empty;
    public string VulkanApiVersion { get; init; } = string.Empty;
    public string VulkanVendorId { get; init; } = string.Empty;
    public string VulkanDeviceId { get; init; } = string.Empty;
    public string VulkanDeviceType { get; init; } = string.Empty;
    public string VulkanDeviceName { get; init; } = string.Empty;
    public string VulkanDeviceUuid { get; init; } = string.Empty;
    public string VulkanDriverUuid { get; init; } = string.Empty;
    public string VulkanDriverId { get; init; } = string.Empty;
    public string VulkanDriverName { get; init; } = string.Empty;
    public string VulkanDriverInfo { get; init; } = string.Empty;
    public string VulkanDriverVersionRaw { get; init; } = string.Empty;
    public string VulkanDriverVersionDecoded { get; init; } = string.Empty;
    public string[] VulkanEnabledExtensions { get; init; } = [];
    public string MetalDeviceName { get; init; } = string.Empty;
    public string MetalRegistryId { get; init; } = string.Empty;
    public string MetalFeatureFamily { get; init; } = string.Empty;
    public string MetalDriver { get; init; } = string.Empty;
    public string SkiaSharpManagedVersion { get; init; } = string.Empty;
    public string SkiaSharpNativeVersion { get; init; } = string.Empty;
    public string SilkNetVulkanVersion { get; init; } = string.Empty;
    public string BeutlEngineAssemblyVersion { get; init; } = string.Empty;

    public static unsafe TargetEvidenceFingerprint Capture(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        BindingFlags flags = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo instanceProperty = typeof(GraphicsContextFactory).GetProperty("VulkanInstance", flags)
            ?? throw new MissingMemberException(typeof(GraphicsContextFactory).FullName, "VulkanInstance");
        object instance = instanceProperty.GetValue(null)
            ?? throw new InvalidOperationException("The Vulkan instance was not initialized.");
        Vk vk = (Vk)(instance.GetType().GetProperty("Vk", flags)?.GetValue(instance)
            ?? throw new InvalidOperationException("The Vulkan API handle was unavailable."));

        MethodInfo selectedMethod = typeof(GraphicsContextFactory).GetMethod("GetSelectedGpuDetails", flags)
            ?? throw new MissingMethodException(typeof(GraphicsContextFactory).FullName, "GetSelectedGpuDetails");
        object selected = selectedMethod.Invoke(null, null)
            ?? throw new InvalidOperationException("The selected Vulkan physical device was unavailable.");
        PhysicalDevice physicalDevice = (PhysicalDevice)(selected.GetType().GetProperty("Device", flags)?.GetValue(selected)
            ?? throw new InvalidOperationException("The selected Vulkan device handle was unavailable."));
        var idProperties = new PhysicalDeviceIDProperties
        {
            SType = StructureType.PhysicalDeviceIDProperties,
        };
        var driverProperties = new PhysicalDeviceDriverProperties
        {
            SType = StructureType.PhysicalDeviceDriverProperties,
            PNext = &idProperties,
        };
        var properties2 = new PhysicalDeviceProperties2
        {
            SType = StructureType.PhysicalDeviceProperties2,
            PNext = &driverProperties,
        };
        vk.GetPhysicalDeviceProperties2(physicalDevice, &properties2);

        PhysicalDeviceProperties properties = properties2.Properties;
        string osBuild = OperatingSystem.IsMacOS()
            ? RunProcess("/usr/bin/sw_vers", ["-buildVersion"]).Trim()
            : ReadOsBuild();
        TargetMetalFingerprint metal = CaptureMetalFingerprint();
        string rendererBackend = context.Backend.ToString();
        string skiaBackend = context.GetType().FullName switch
        {
            "Beutl.Graphics.Backend.Composite.CompositeContext" => "Metal",
            "Beutl.Graphics.Backend.Vulkan.VulkanContext" => "Vulkan",
            _ => rendererBackend,
        };
        var result = new TargetEvidenceFingerprint
        {
            OsDescription = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            OsBuild = osBuild,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            EnvironmentVersion = Environment.Version.ToString(),
            RendererBackend = rendererBackend,
            SkiaBackend = skiaBackend,
            DeviceSelection = "automatic-no-preferred-device",
            VulkanApiVersion = DecodeVulkanVersion(properties.ApiVersion),
            VulkanVendorId = $"0x{properties.VendorID:x8}",
            VulkanDeviceId = $"0x{properties.DeviceID:x8}",
            VulkanDeviceType = properties.DeviceType.ToString(),
            VulkanDeviceName = FixedUtf8(properties.DeviceName, Vk.MaxPhysicalDeviceNameSize),
            VulkanDeviceUuid = Hex(idProperties.DeviceUuid, Vk.UuidSize),
            VulkanDriverUuid = Hex(idProperties.DriverUuid, Vk.UuidSize),
            VulkanDriverId = driverProperties.DriverID.ToString(),
            VulkanDriverName = FixedUtf8(driverProperties.DriverName, Vk.MaxDriverNameSize),
            VulkanDriverInfo = FixedUtf8(driverProperties.DriverInfo, Vk.MaxDriverInfoSize),
            VulkanDriverVersionRaw = properties.DriverVersion.ToString(),
            VulkanDriverVersionDecoded = DecodeVulkanVersion(properties.DriverVersion),
            VulkanEnabledExtensions = GraphicsContextFactory.GetEnabledExtensions()
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            MetalDeviceName = metal.DeviceName,
            MetalRegistryId = metal.RegistryId,
            MetalFeatureFamily = metal.FeatureFamily,
            MetalDriver = OperatingSystem.IsMacOS()
                ? $"Apple Metal shipped with macOS build {osBuild}"
                : "not-applicable",
            SkiaSharpManagedVersion = AssemblyVersion(typeof(SKBitmap).Assembly),
            SkiaSharpNativeVersion = SkiaSharpVersion.Native.ToString(),
            SilkNetVulkanVersion = AssemblyVersion(typeof(Vk).Assembly),
            BeutlEngineAssemblyVersion = AssemblyVersion(typeof(RenderNode).Assembly),
        };
        Validate(result);
        return result;
    }

    private static TargetMetalFingerprint CaptureMetalFingerprint()
    {
        if (!OperatingSystem.IsMacOS())
            return new TargetMetalFingerprint("not-applicable", "not-applicable", "not-applicable");
        IntPtr device = MTLCreateSystemDefaultDevice();
        if (device == IntPtr.Zero)
            throw new InvalidOperationException("MTLCreateSystemDefaultDevice returned null.");
        try
        {
            IntPtr nameObject = IntPtr_objc_msgSend(device, sel_registerName("name"));
            IntPtr utf8 = IntPtr_objc_msgSend(nameObject, sel_registerName("UTF8String"));
            string name = Marshal.PtrToStringUTF8(utf8)
                ?? throw new InvalidOperationException("The Metal device name was null.");
            ulong registryId = UInt64_objc_msgSend(device, sel_registerName("registryID"));
            if (registryId == 0)
                throw new InvalidOperationException("The Metal registry ID was zero.");
            using JsonDocument document = JsonDocument.Parse(
                RunProcess("/usr/sbin/system_profiler", ["SPDisplaysDataType", "-json"]));
            JsonElement gpu = document.RootElement.GetProperty("SPDisplaysDataType")[0];
            string family = gpu.TryGetProperty("spdisplays_mtlgpufamilysupport", out JsonElement value)
                ? value.GetString()
                    ?? throw new InvalidOperationException("The Metal feature family was null.")
                : throw new InvalidOperationException("system_profiler did not report the Metal feature family.");
            return new TargetMetalFingerprint(name, $"0x{registryId:x16}", family);
        }
        finally
        {
            objc_release(device);
        }
    }

    private static unsafe string FixedUtf8(byte* value, uint maxLength)
    {
        int length = 0;
        while (length < maxLength && value[length] != 0)
            length++;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value, length));
    }

    private static unsafe string Hex(byte* value, uint length)
        => Convert.ToHexString(new ReadOnlySpan<byte>(value, checked((int)length))).ToLowerInvariant();

    private static string DecodeVulkanVersion(uint value)
        => $"{value >> 22}.{(value >> 12) & 0x3ff}.{value & 0xfff}";

    private static string ReadOsBuild()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/etc/os-release"))
            return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("/etc/os-release"))).ToLowerInvariant();
        return Environment.OSVersion.VersionString;
    }

    private static string RunProcess(string fileName, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
            startInfo.ArgumentList.Add(argument);
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start '{fileName}'.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"'{fileName}' exited with {process.ExitCode}: {stderr.Trim()}");
        return stdout;
    }

    private static string AssemblyVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? assembly.GetName().Version?.ToString()
           ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' has no version.");

    private static void Validate(TargetEvidenceFingerprint fingerprint)
    {
        foreach (PropertyInfo property in typeof(TargetEvidenceFingerprint).GetProperties())
        {
            object? value = property.GetValue(fingerprint);
            if (value is string text
                && (string.IsNullOrWhiteSpace(text)
                    || text.Contains("unknown", StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is missing or unknown.");
            }
            if (value is string[] array
                && (array.Length == 0 || array.Any(string.IsNullOrWhiteSpace)))
            {
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is empty.");
            }
        }
    }

    [DllImport("/System/Library/Frameworks/Metal.framework/Metal")]
    private static extern IntPtr MTLCreateSystemDefaultDevice();

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern IntPtr sel_registerName(string name);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr IntPtr_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern ulong UInt64_objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib")]
    private static extern void objc_release(IntPtr value);
}

internal sealed record TargetMetalFingerprint(string DeviceName, string RegistryId, string FeatureFamily);
