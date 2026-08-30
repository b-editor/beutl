using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using Beutl.Graphics.Backend;
using Beutl.Graphics.Backend.Vulkan;
using Beutl.Graphics.Rendering;

using Silk.NET.Vulkan;

using SkiaSharp;

namespace Beutl.Evidence;

/// <summary>
/// The machine, device, driver, and build identity a rendering evidence run was produced on.
/// </summary>
/// <remarks>
/// <para>
/// Two evidence runs may only be compared when their <see cref="ComparabilityKey"/> matches. The key covers
/// exactly the fields that change what the renderer produces or how fast it produces it: the physical device
/// and driver identity, the enabled Vulkan extensions, the backend pair, <see cref="MaxAttachmentDimension"/>
/// (feature 003's per-buffer clamp reads it, so a smaller device silently renders at a different working
/// density), the OS and process architecture, the runtime, and the Skia / Silk.NET / Beutl.Engine builds.
/// Wall-clock time and the output paths are deliberately outside the key.
/// </para>
/// <para>
/// Capture never starts a child process and never P/Invokes a platform UI framework, because it runs inside
/// NUnit fixtures and inside BenchmarkDotNet setup. On macOS the Vulkan identity is MoltenVK's view of the
/// same Metal device Skia draws with, so the Vulkan block identifies the GPU on every supported platform.
/// </para>
/// </remarks>
public sealed record RenderEvidenceFingerprint
{
    /// <summary>The schema version of the emitted fingerprint object.</summary>
    public const int CurrentSchemaVersion = 1;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = true,
    };

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string OsDescription { get; init; } = string.Empty;
    public string OsVersion { get; init; } = string.Empty;
    public string OsArchitecture { get; init; } = string.Empty;
    public string ProcessArchitecture { get; init; } = string.Empty;
    public string RuntimeIdentifier { get; init; } = string.Empty;
    public string FrameworkDescription { get; init; } = string.Empty;
    public string EnvironmentVersion { get; init; } = string.Empty;
    public string BuildConfiguration { get; init; } = string.Empty;

    public string RendererBackend { get; init; } = string.Empty;
    public string SkiaBackend { get; init; } = string.Empty;
    public string DeviceSelection { get; init; } = string.Empty;

    /// <summary>The device's largest attachable-and-samplable square, in pixels.</summary>
    /// <remarks>
    /// Feature 003's <c>ClampWorkingScaleToBufferBudget</c> reduces the working density at effect boundaries to
    /// keep a buffer inside this limit, so two devices that disagree here can render the same scene at different
    /// densities without any other fingerprint field changing.
    /// </remarks>
    public int MaxAttachmentDimension { get; init; }

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

    public string SkiaSharpManagedVersion { get; init; } = string.Empty;
    public string SkiaSharpNativeVersion { get; init; } = string.Empty;
    public string SilkNetVulkanVersion { get; init; } = string.Empty;
    public string BeutlEngineAssemblyVersion { get; init; } = string.Empty;

    /// <summary>The 40-character source revision the engine assembly was built from, when its version carries one.</summary>
    public string BeutlEngineSourceRevision { get; init; } = string.Empty;

    /// <summary>A SHA-256 over the fields that must match before two runs may be compared.</summary>
    public string ComparabilityKey => ComputeComparabilityKey(this);

    /// <summary>Whether <paramref name="other"/> was produced under conditions that permit a direct comparison.</summary>
    public bool IsComparableTo(RenderEvidenceFingerprint? other)
        => other is not null
           && string.Equals(ComparabilityKey, other.ComparabilityKey, StringComparison.Ordinal);

    /// <summary>
    /// Captures the identity of the machine and device <paramref name="context"/> renders on.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The Vulkan instance or the selected physical device was unavailable, or a captured field was blank or
    /// literally "unknown". A partial fingerprint is worse than none: it reads as a comparable run.
    /// </exception>
    public static unsafe RenderEvidenceFingerprint Capture(IGraphicsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        VulkanInstance instance = GraphicsContextFactory.VulkanInstance
            ?? throw new InvalidOperationException("The Vulkan instance was not initialized.");
        VulkanPhysicalDeviceInfo selected = GraphicsContextFactory.GetSelectedGpuDetails()
            ?? throw new InvalidOperationException("The selected Vulkan physical device was unavailable.");

        Vk vk = instance.Vk;
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
        vk.GetPhysicalDeviceProperties2(selected.Device, &properties2);
        PhysicalDeviceProperties properties = properties2.Properties;

        string rendererBackend = context.Backend.ToString();
        string engineVersion = AssemblyVersion(typeof(RenderNode).Assembly);
        var result = new RenderEvidenceFingerprint
        {
            OsDescription = RuntimeInformation.OSDescription,
            OsVersion = Environment.OSVersion.VersionString,
            OsArchitecture = RuntimeInformation.OSArchitecture.ToString(),
            ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeIdentifier = RuntimeInformation.RuntimeIdentifier,
            FrameworkDescription = RuntimeInformation.FrameworkDescription,
            EnvironmentVersion = Environment.Version.ToString(),
            BuildConfiguration = ReadBuildConfiguration(typeof(RenderNode).Assembly),
            RendererBackend = rendererBackend,
            SkiaBackend = context.GetType().FullName switch
            {
                "Beutl.Graphics.Backend.Composite.CompositeContext" => "Metal",
                "Beutl.Graphics.Backend.Vulkan.VulkanContext" => "Vulkan",
                _ => rendererBackend,
            },
            DeviceSelection = "automatic-no-preferred-device",
            MaxAttachmentDimension = context.MaxAttachmentDimension,
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
            SkiaSharpManagedVersion = AssemblyVersion(typeof(SKBitmap).Assembly),
            SkiaSharpNativeVersion = SkiaSharpVersion.Native.ToString(),
            SilkNetVulkanVersion = AssemblyVersion(typeof(Vk).Assembly),
            BeutlEngineAssemblyVersion = engineVersion,
            BeutlEngineSourceRevision = ExtractSourceRevision(engineVersion),
        };
        Validate(result);
        return result;
    }

    /// <summary>Captures the fingerprint, or returns <see langword="null"/> with the reason capture failed.</summary>
    /// <remarks>
    /// Test fixtures record evidence as a side effect of asserting something else, and must not turn a
    /// fingerprint problem into a red test. The runner scripts refuse a manifest whose fingerprint is null.
    /// </remarks>
    public static RenderEvidenceFingerprint? TryCapture(IGraphicsContext? context, out string? unavailableReason)
    {
        if (context is null)
        {
            unavailableReason = "No graphics context was available.";
            return null;
        }

        try
        {
            unavailableReason = null;
            return Capture(context);
        }
        catch (Exception ex)
        {
            unavailableReason = ex.Message;
            return null;
        }
    }

    /// <summary>The fields that must match before two runs may be compared, in a stable order.</summary>
    internal static IEnumerable<KeyValuePair<string, string>> EnumerateComparabilityFields(
        RenderEvidenceFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        yield return new("schemaVersion", fingerprint.SchemaVersion.ToString());
        yield return new("osDescription", fingerprint.OsDescription);
        yield return new("osArchitecture", fingerprint.OsArchitecture);
        yield return new("processArchitecture", fingerprint.ProcessArchitecture);
        yield return new("runtimeIdentifier", fingerprint.RuntimeIdentifier);
        yield return new("frameworkDescription", fingerprint.FrameworkDescription);
        yield return new("buildConfiguration", fingerprint.BuildConfiguration);
        yield return new("rendererBackend", fingerprint.RendererBackend);
        yield return new("skiaBackend", fingerprint.SkiaBackend);
        yield return new("deviceSelection", fingerprint.DeviceSelection);
        yield return new("maxAttachmentDimension", fingerprint.MaxAttachmentDimension.ToString());
        yield return new("vulkanApiVersion", fingerprint.VulkanApiVersion);
        yield return new("vulkanVendorId", fingerprint.VulkanVendorId);
        yield return new("vulkanDeviceId", fingerprint.VulkanDeviceId);
        yield return new("vulkanDeviceType", fingerprint.VulkanDeviceType);
        yield return new("vulkanDeviceName", fingerprint.VulkanDeviceName);
        yield return new("vulkanDeviceUuid", fingerprint.VulkanDeviceUuid);
        yield return new("vulkanDriverUuid", fingerprint.VulkanDriverUuid);
        yield return new("vulkanDriverId", fingerprint.VulkanDriverId);
        yield return new("vulkanDriverName", fingerprint.VulkanDriverName);
        yield return new("vulkanDriverInfo", fingerprint.VulkanDriverInfo);
        yield return new("vulkanDriverVersionRaw", fingerprint.VulkanDriverVersionRaw);
        yield return new("vulkanEnabledExtensions", string.Join(',', fingerprint.VulkanEnabledExtensions));
        yield return new("skiaSharpManagedVersion", fingerprint.SkiaSharpManagedVersion);
        yield return new("skiaSharpNativeVersion", fingerprint.SkiaSharpNativeVersion);
        yield return new("silkNetVulkanVersion", fingerprint.SilkNetVulkanVersion);
    }

    private static string ComputeComparabilityKey(RenderEvidenceFingerprint fingerprint)
    {
        var payload = new MemoryStream();
        foreach ((string name, string value) in EnumerateComparabilityFields(fingerprint))
        {
            payload.Write(Encoding.UTF8.GetBytes(name));
            payload.WriteByte(0);
            payload.Write(Encoding.UTF8.GetBytes(value));
            payload.WriteByte((byte)'\n');
        }

        return Convert.ToHexString(SHA256.HashData(payload.ToArray())).ToLowerInvariant();
    }

    internal static void Validate(RenderEvidenceFingerprint fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        foreach (PropertyInfo property in typeof(RenderEvidenceFingerprint).GetProperties())
        {
            if (string.Equals(property.Name, nameof(ComparabilityKey), StringComparison.Ordinal))
                continue;

            object? value = property.GetValue(fingerprint);
            if (value is string text
                && (text.Contains("unknown", StringComparison.OrdinalIgnoreCase)
                    || (string.IsNullOrWhiteSpace(text)
                        && !EvidenceFingerprintRules.AllowsBlankValue(
                            property.Name,
                            fingerprint.VulkanDeviceType))))
            {
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is missing or unknown.");
            }

            if (value is string[] array && (array.Length == 0 || array.Any(string.IsNullOrWhiteSpace)))
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is empty.");

            if (value is int number && number <= 0)
                throw new InvalidOperationException($"Fingerprint field '{property.Name}' is not positive.");
        }
    }

    /// <summary>Extracts the 40-character source revision an informational assembly version carries.</summary>
    /// <remarks>Returns "no-source-revision" when the build stamped none, which is a fact rather than a failure.</remarks>
    internal static string ExtractSourceRevision(string assemblyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assemblyVersion);
        for (int start = 0; start <= assemblyVersion.Length - 40; start++)
        {
            ReadOnlySpan<char> candidate = assemblyVersion.AsSpan(start, 40);
            bool hex = true;
            foreach (char item in candidate)
            {
                if (!Uri.IsHexDigit(item))
                {
                    hex = false;
                    break;
                }
            }

            if (hex)
                return candidate.ToString().ToLowerInvariant();
        }

        return "no-source-revision";
    }

    internal static string DecodeVulkanVersion(uint value)
        => $"{value >> 22}.{(value >> 12) & 0x3ff}.{value & 0xfff}";

    private static unsafe string FixedUtf8(byte* value, uint maxLength)
    {
        int length = 0;
        while (length < maxLength && value[length] != 0)
            length++;
        return Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value, length));
    }

    private static unsafe string Hex(byte* value, uint length)
        => Convert.ToHexString(new ReadOnlySpan<byte>(value, checked((int)length))).ToLowerInvariant();

    private static string AssemblyVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? assembly.GetName().Version?.ToString()
           ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' has no version.");

    private static string ReadBuildConfiguration(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration is { Length: > 0 } value
            ? value
            : "no-configuration-attribute";
}
