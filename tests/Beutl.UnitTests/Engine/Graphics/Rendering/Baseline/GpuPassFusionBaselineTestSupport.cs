using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using Beutl.Graphics.Rendering;
using Beutl.Media;
using Beutl.UnitTests.Engine.Graphics.Rendering.Golden;

namespace Beutl.UnitTests.Engine.Graphics.Rendering.Baseline;

internal static class GpuPassFusionBaselineEvidence
{
    public const int ExpectedSchemaVersion = 1;
    public const int ExpectedGeneratorSeed = 20040719;
    public const string ExpectedBaselineCodeSha = "43a38e665d9bf52548161a3917e748bd1457ff55";

    // This is the trust anchor for the immutable manifest. Update it only when the
    // pinned-SHA evidence is deliberately regenerated and reviewed.
    public const string ExpectedManifestSha256 = "e3a1a6821715c88f927422c1f2bc9fa0f3e74c887bab2f85ac13b15e9b822618";

    public const double NonVacuityParityTolerance = 0.02;

    private static readonly string[] s_expectedManifestProperties =
    [
        "allocationFailures",
        "artifactSha256",
        "baselineCodeSha",
        "baselineCommitTimestamp",
        "benchmark",
        "evidenceTools",
        "fingerprint",
        "generatorSeed",
        "patchedDiffSha256",
        "pixelFormat",
        "prePatchRepositoryState",
        "roiBaselineMode",
        "scenes",
        "schemaVersion",
    ];

    private static readonly string[] s_expectedEvidenceToolProperties =
    [
        "generatorPatchSha256",
        "generatorScriptSha256",
        "generatorSourceBundleSha256",
        "pairedRunnerSha256",
    ];

    private static readonly string[] s_expectedFingerprintProperties =
    [
        "beutlEngineAssemblyVersion",
        "deviceSelection",
        "environmentVersion",
        "frameworkDescription",
        "metalDeviceName",
        "metalDriver",
        "metalFeatureFamily",
        "metalRegistryId",
        "osArchitecture",
        "osBuild",
        "osDescription",
        "osVersion",
        "processArchitecture",
        "rendererBackend",
        "runtimeIdentifier",
        "silkNetVulkanVersion",
        "skiaBackend",
        "skiaSharpManagedVersion",
        "skiaSharpNativeVersion",
        "vulkanApiVersion",
        "vulkanDeviceId",
        "vulkanDeviceName",
        "vulkanDeviceType",
        "vulkanDeviceUuid",
        "vulkanDriverId",
        "vulkanDriverInfo",
        "vulkanDriverName",
        "vulkanDriverUuid",
        "vulkanDriverVersionDecoded",
        "vulkanDriverVersionRaw",
        "vulkanEnabledExtensions",
        "vulkanVendorId",
    ];

    private static readonly string[] s_expectedPixelFormatProperties =
    [
        "alphaType",
        "bytesPerPixel",
        "colorSpace",
        "colorType",
        "endianness",
        "packing",
    ];

    private static readonly string[] s_expectedSceneProperties =
    [
        "blob",
        "blobHeight",
        "blobWidth",
        "category",
        "controlSceneId",
        "empty",
        "id",
        "legacyCounters",
        "legacyEvents",
        "logicalHeight",
        "logicalWidth",
        "maxWorkingScale",
        "nonVacuity",
        "nonVacuityMode",
        "nonVacuityRegion",
        "outputScale",
        "parameters",
        "query",
        "requestedRegion",
        "role",
    ];

    private static readonly string[] s_expectedNonVacuityProperties =
    [
        "alphaMae",
        "linearRgbMae",
        "marginAboveTolerance",
        "maximumChannelError",
        "metricMode",
        "metricRegion",
        "parityTolerance",
        "sampleCount",
    ];

    private static readonly string[] s_expectedAllocationFailureProperties =
    [
        "exceptionMessage",
        "exceptionType",
        "injectionPoint",
        "intent",
        "legacyCounters",
        "legacyEvents",
        "maxWorkingScale",
        "outcome",
    ];

    public static IReadOnlyList<string> RequiredFingerprintFields => s_expectedFingerprintProperties;

    public static GpuPassFusionEvidenceManifest LoadAndVerify()
    {
        GpuPassFusionEvidencePaths paths = GpuPassFusionEvidencePaths.Discover();
        VerifyFileHash(paths.ManifestPath, ExpectedManifestSha256, "target baseline manifest");

        using FileStream stream = OpenExistingReadOnly(paths.ManifestPath, "target baseline manifest");
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("The target baseline manifest is not valid JSON.", ex);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            ValidateExactObjectShape(root, s_expectedManifestProperties, "manifest");

            int schemaVersion = ReadInt32(root, "schemaVersion", "manifest");
            if (schemaVersion != ExpectedSchemaVersion)
            {
                throw new InvalidDataException(
                    $"Unexpected target baseline schema version {schemaVersion}; expected {ExpectedSchemaVersion}.");
            }

            string baselineCodeSha = ReadString(root, "baselineCodeSha", "manifest");
            if (!string.Equals(baselineCodeSha, ExpectedBaselineCodeSha, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Target baseline source SHA mismatch: expected {ExpectedBaselineCodeSha}, found {baselineCodeSha}.");
            }

            ValidateCommitTimestamp(ReadString(root, "baselineCommitTimestamp", "manifest"));
            RequireExactString(root, "prePatchRepositoryState", "clean", "manifest");
            string patchedDiffSha256 = ReadSha256(root, "patchedDiffSha256", "manifest");

            int generatorSeed = ReadInt32(root, "generatorSeed", "manifest");
            if (generatorSeed != ExpectedGeneratorSeed)
            {
                throw new InvalidDataException(
                    $"Target baseline generator seed mismatch: expected {ExpectedGeneratorSeed}, found {generatorSeed}.");
            }

            RequireExactString(
                root,
                "roiBaselineMode",
                "legacy-full-render-then-device-crop",
                "manifest");

            GpuPassFusionEvidenceTools tools = ReadEvidenceTools(root.GetProperty("evidenceTools"));
            VerifyFileHash(paths.GeneratorPatchPath, tools.GeneratorPatchSha256, "target baseline generator patch");
            VerifyFileHash(paths.GeneratorScriptPath, tools.GeneratorScriptSha256, "target baseline generator script");
            VerifyFileHash(paths.PairedRunnerPath, tools.PairedRunnerSha256, "paired visual-evidence runner");

            IReadOnlyDictionary<string, IReadOnlyList<string>> fingerprint =
                ReadFingerprint(root.GetProperty("fingerprint"));
            ValidateFingerprint(fingerprint);
            string engineVersion = fingerprint["beutlEngineAssemblyVersion"].Single();
            if (!engineVersion.EndsWith('+' + ExpectedBaselineCodeSha, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "The fingerprinted Beutl.Engine assembly does not identify the pinned baseline source SHA.");
            }

            ValidatePixelFormat(root.GetProperty("pixelFormat"));
            IReadOnlyDictionary<string, string> artifactHashes = ReadArtifactHashes(root.GetProperty("artifactSha256"));
            IReadOnlyList<GpuPassFusionEvidenceScene> scenes = ReadScenes(root.GetProperty("scenes"));
            ValidateScenesAndArtifacts(paths, scenes, artifactHashes);
            ValidateAllocationFailures(root.GetProperty("allocationFailures"));
            ValidateBenchmark(root.GetProperty("benchmark"));

            return new GpuPassFusionEvidenceManifest(
                paths,
                schemaVersion,
                baselineCodeSha,
                patchedDiffSha256,
                generatorSeed,
                tools,
                fingerprint,
                artifactHashes,
                scenes);
        }
    }

    public static void ValidateFingerprint(
        IReadOnlyDictionary<string, IReadOnlyList<string>> fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        string[] actualNames = [.. fingerprint.Keys.Order(StringComparer.Ordinal)];
        if (!actualNames.SequenceEqual(s_expectedFingerprintProperties, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Evidence fingerprint schema mismatch. "
                + $"Expected [{string.Join(", ", s_expectedFingerprintProperties)}], "
                + $"found [{string.Join(", ", actualNames)}].");
        }

        foreach ((string name, IReadOnlyList<string> values) in fingerprint)
        {
            if (values.Count == 0)
                throw new InvalidDataException($"Evidence fingerprint field '{name}' is empty.");

            foreach (string value in values)
            {
                if (string.IsNullOrWhiteSpace(value)
                    || value.Contains("unknown", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"Evidence fingerprint field '{name}' is missing or unknown.");
                }
            }
        }
    }

    public static void VerifyFileHash(string path, string expectedSha256, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ValidateSha256(expectedSha256, $"expected {description} SHA-256");

        using FileStream stream = OpenExistingReadOnly(path, description);
        string actualSha256 = Convert.ToHexStringLower(SHA256.HashData(stream));
        if (!string.Equals(actualSha256, expectedSha256, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"SHA-256 mismatch for {description}: expected {expectedSha256}, found {actualSha256}.");
        }
    }

    public static GpuPassFusionNonVacuityDelta RecomputeNonVacuity(
        GpuPassFusionEvidenceManifest manifest,
        GpuPassFusionEvidenceScene scene)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(scene);
        if (!string.Equals(scene.Role, "parity", StringComparison.Ordinal)
            || scene.ControlSceneId is null
            || scene.Blob is null)
        {
            throw new InvalidDataException($"Scene '{scene.Id}' is not a complete parity workload.");
        }

        GpuPassFusionEvidenceScene control = manifest.GetScene(scene.ControlSceneId);
        if (control.Blob is null)
            throw new InvalidDataException($"Control scene '{control.Id}' has no immutable RGBA16F blob.");

        string referencePath = Path.Combine(manifest.Paths.BaselineDirectory, scene.Blob);
        string controlPath = Path.Combine(manifest.Paths.BaselineDirectory, control.Blob);
        using Bitmap reference = Rgba16fGoldenStore.Read(referencePath, scene.BlobWidth, scene.BlobHeight);
        using Bitmap disabled = Rgba16fGoldenStore.Read(controlPath, control.BlobWidth, control.BlobHeight);
        return CalculateNonVacuityDelta(
            reference,
            disabled,
            scene.NonVacuityMode,
            scene.NonVacuityRegion);
    }

    public static GpuPassFusionNonVacuityDelta CalculateNonVacuityDelta(
        Bitmap reference,
        Bitmap control,
        string metricMode,
        GpuPassFusionPixelRegion? metricRegion)
    {
        ArgumentNullException.ThrowIfNull(reference);
        ArgumentNullException.ThrowIfNull(control);
        if (reference.Width != control.Width || reference.Height != control.Height)
        {
            throw new InvalidDataException(
                $"Non-vacuity bitmap sizes differ: {reference.Width}x{reference.Height} versus "
                + $"{control.Width}x{control.Height}.");
        }

        if (metricMode is not ("full-frame" or "alpha-edge-band"))
            throw new InvalidDataException($"Unknown non-vacuity metric mode '{metricMode}'.");

        GpuPassFusionPixelRegion region = metricRegion
            ?? new GpuPassFusionPixelRegion(0, 0, reference.Width, reference.Height);
        region.ValidateInside(reference.Width, reference.Height, "non-vacuity metric region");

        double rgbError = 0;
        double alphaError = 0;
        double maximumChannelError = 0;
        int sampleCount = 0;
        for (int y = region.Y; y < region.Bottom; y++)
        {
            ReadOnlySpan<ushort> referenceRow = reference.GetRow<ushort>(y);
            ReadOnlySpan<ushort> controlRow = control.GetRow<ushort>(y);
            for (int x = region.X; x < region.Right; x++)
            {
                int offset = x * 4;
                float referenceAlpha = HalfBitsToFiniteFloat(referenceRow[offset + 3], reference, x, y, 3);
                float controlAlpha = HalfBitsToFiniteFloat(controlRow[offset + 3], control, x, y, 3);
                if (metricMode == "alpha-edge-band"
                    && !IsNonVacuityCoverageEdge(referenceAlpha)
                    && !IsNonVacuityCoverageEdge(controlAlpha))
                {
                    continue;
                }

                sampleCount++;
                for (int channel = 0; channel < 4; channel++)
                {
                    float referenceValue = HalfBitsToFiniteFloat(
                        referenceRow[offset + channel],
                        reference,
                        x,
                        y,
                        channel);
                    float controlValue = HalfBitsToFiniteFloat(
                        controlRow[offset + channel],
                        control,
                        x,
                        y,
                        channel);
                    double difference = Math.Abs(referenceValue - controlValue);
                    maximumChannelError = Math.Max(maximumChannelError, difference);
                    if (channel == 3)
                        alphaError += difference;
                    else
                        rgbError += difference;
                }
            }
        }

        if (sampleCount == 0)
            throw new InvalidDataException($"Non-vacuity metric mode '{metricMode}' selected no pixels.");

        double linearRgbMae = rgbError / (sampleCount * 3d);
        double alphaMae = alphaError / sampleCount;
        double margin = Math.Max(linearRgbMae, alphaMae) - NonVacuityParityTolerance;
        return new GpuPassFusionNonVacuityDelta(
            linearRgbMae,
            alphaMae,
            maximumChannelError,
            sampleCount,
            margin);
    }

    private static GpuPassFusionEvidenceTools ReadEvidenceTools(JsonElement element)
    {
        ValidateExactObjectShape(element, s_expectedEvidenceToolProperties, "manifest.evidenceTools");
        return new GpuPassFusionEvidenceTools(
            ReadSha256(element, "generatorPatchSha256", "manifest.evidenceTools"),
            ReadSha256(element, "generatorScriptSha256", "manifest.evidenceTools"),
            ReadSha256(element, "pairedRunnerSha256", "manifest.evidenceTools"),
            ReadSha256(element, "generatorSourceBundleSha256", "manifest.evidenceTools"));
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadFingerprint(JsonElement element)
    {
        ValidateExactObjectShape(element, s_expectedFingerprintProperties, "manifest.fingerprint");
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            IReadOnlyList<string> values;
            if (property.NameEquals("vulkanEnabledExtensions"))
            {
                if (property.Value.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException(
                        "manifest.fingerprint.vulkanEnabledExtensions must be an array.");
                }

                values = [.. property.Value.EnumerateArray().Select(
                    item => ReadStringValue(item, "manifest.fingerprint.vulkanEnabledExtensions[]"))];
            }
            else
            {
                values = [ReadStringValue(property.Value, $"manifest.fingerprint.{property.Name}")];
            }

            result.Add(property.Name, values);
        }

        return result;
    }

    private static void ValidatePixelFormat(JsonElement element)
    {
        ValidateExactObjectShape(element, s_expectedPixelFormatProperties, "manifest.pixelFormat");
        RequireExactString(element, "colorType", "RGBA16F", "manifest.pixelFormat");
        RequireExactString(element, "alphaType", "premultiplied", "manifest.pixelFormat");
        RequireExactString(element, "colorSpace", "linear-sRGB", "manifest.pixelFormat");
        RequireExactString(element, "endianness", "little", "manifest.pixelFormat");
        RequireExactString(element, "packing", "row-packed-without-padding", "manifest.pixelFormat");
        int bytesPerPixel = ReadInt32(element, "bytesPerPixel", "manifest.pixelFormat");
        if (bytesPerPixel != 8)
            throw new InvalidDataException("manifest.pixelFormat.bytesPerPixel must be 8.");
    }

    private static IReadOnlyDictionary<string, string> ReadArtifactHashes(JsonElement element)
    {
        RequireObject(element, "manifest.artifactSha256");
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            ValidateSafeBlobName(property.Name);
            string sha256 = ReadStringValue(property.Value, $"manifest.artifactSha256.{property.Name}");
            ValidateSha256(sha256, $"manifest artifact SHA-256 for '{property.Name}'");
            if (!result.TryAdd(property.Name, sha256))
                throw new InvalidDataException($"Duplicate artifact hash entry '{property.Name}'.");
        }

        if (result.Count == 0)
            throw new InvalidDataException("manifest.artifactSha256 must not be empty.");
        return result;
    }

    private static IReadOnlyList<GpuPassFusionEvidenceScene> ReadScenes(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("manifest.scenes must be an array.");

        var result = new List<GpuPassFusionEvidenceScene>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonElement item in element.EnumerateArray())
        {
            string context = $"manifest.scenes[{result.Count}]";
            ValidateExactObjectShape(item, s_expectedSceneProperties, context);
            string id = ReadString(item, "id", context);
            if (!ids.Add(id))
                throw new InvalidDataException($"Duplicate scene id '{id}'.");

            string category = ReadString(item, "category", context);
            string role = ReadString(item, "role", context);
            if (role is not ("parity" or "control" or "metadata"))
                throw new InvalidDataException($"Scene '{id}' has unsupported role '{role}'.");

            string? controlSceneId = ReadNullableString(item, "controlSceneId", context);
            string? blob = ReadNullableString(item, "blob", context);
            if (blob is not null)
                ValidateSafeBlobName(blob);

            int blobWidth = ReadInt32(item, "blobWidth", context);
            int blobHeight = ReadInt32(item, "blobHeight", context);
            int logicalWidth = ReadInt32(item, "logicalWidth", context);
            int logicalHeight = ReadInt32(item, "logicalHeight", context);
            double outputScale = ReadFiniteNumber(item, "outputScale", context);
            if (logicalWidth <= 0 || logicalHeight <= 0 || outputScale <= 0)
                throw new InvalidDataException($"Scene '{id}' has invalid logical dimensions or output scale.");

            if (blob is null)
            {
                if (blobWidth != 0 || blobHeight != 0)
                    throw new InvalidDataException($"Blob-less scene '{id}' must have zero blob dimensions.");
            }
            else if (blobWidth <= 0 || blobHeight <= 0)
            {
                throw new InvalidDataException($"Scene '{id}' has invalid blob dimensions.");
            }

            string nonVacuityMode = ReadString(item, "nonVacuityMode", context);
            if (nonVacuityMode is not ("full-frame" or "alpha-edge-band"))
                throw new InvalidDataException($"Scene '{id}' has unknown non-vacuity mode '{nonVacuityMode}'.");

            GpuPassFusionPixelRegion? nonVacuityRegion =
                ReadNullablePixelRegion(item.GetProperty("nonVacuityRegion"), $"{context}.nonVacuityRegion");
            GpuPassFusionNonVacuityRecord? nonVacuity =
                ReadNullableNonVacuity(item.GetProperty("nonVacuity"), $"{context}.nonVacuity");

            ValidateObject(item.GetProperty("parameters"), $"{context}.parameters");
            ValidateCounterObject(item.GetProperty("legacyCounters"), $"{context}.legacyCounters");
            ValidateStringArray(item.GetProperty("legacyEvents"), $"{context}.legacyEvents");
            ValidateNullOrObject(item.GetProperty("query"), $"{context}.query");
            ValidateNullOrObject(item.GetProperty("requestedRegion"), $"{context}.requestedRegion");
            ReadString(item, "maxWorkingScale", context);
            ReadBoolean(item, "empty", context);

            result.Add(new GpuPassFusionEvidenceScene(
                id,
                category,
                role,
                controlSceneId,
                blob,
                blobWidth,
                blobHeight,
                nonVacuityMode,
                nonVacuityRegion,
                nonVacuity));
        }

        if (result.Count == 0)
            throw new InvalidDataException("manifest.scenes must not be empty.");
        return result;
    }

    private static GpuPassFusionNonVacuityRecord? ReadNullableNonVacuity(
        JsonElement element,
        string context)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        ValidateExactObjectShape(element, s_expectedNonVacuityProperties, context);
        return new GpuPassFusionNonVacuityRecord(
            ReadFiniteNumber(element, "linearRgbMae", context),
            ReadFiniteNumber(element, "alphaMae", context),
            ReadFiniteNumber(element, "maximumChannelError", context),
            ReadInt32(element, "sampleCount", context),
            ReadString(element, "metricMode", context),
            ReadNullablePixelRegion(element.GetProperty("metricRegion"), $"{context}.metricRegion"),
            ReadFiniteNumber(element, "parityTolerance", context),
            ReadFiniteNumber(element, "marginAboveTolerance", context));
    }

    private static void ValidateScenesAndArtifacts(
        GpuPassFusionEvidencePaths paths,
        IReadOnlyList<GpuPassFusionEvidenceScene> scenes,
        IReadOnlyDictionary<string, string> artifactHashes)
    {
        Dictionary<string, GpuPassFusionEvidenceScene> sceneById =
            scenes.ToDictionary(scene => scene.Id, StringComparer.Ordinal);
        var sceneByBlob = new Dictionary<string, GpuPassFusionEvidenceScene>(StringComparer.Ordinal);
        foreach (GpuPassFusionEvidenceScene scene in scenes)
        {
            if (scene.Blob is not null)
            {
                if (sceneByBlob.TryGetValue(scene.Blob, out GpuPassFusionEvidenceScene? existing))
                {
                    if (existing.BlobWidth != scene.BlobWidth || existing.BlobHeight != scene.BlobHeight)
                    {
                        throw new InvalidDataException(
                            $"Scenes sharing blob '{scene.Blob}' declare different dimensions.");
                    }
                }
                else
                {
                    sceneByBlob.Add(scene.Blob, scene);
                }
            }

            if (scene.Role == "parity")
            {
                if (scene.ControlSceneId is null || scene.NonVacuity is null)
                    throw new InvalidDataException($"Parity scene '{scene.Id}' lacks control or non-vacuity evidence.");
                if (!sceneById.TryGetValue(scene.ControlSceneId, out GpuPassFusionEvidenceScene? control))
                    throw new InvalidDataException($"Parity scene '{scene.Id}' references a missing control.");
                if (control.Role != "control" || control.Blob is null || scene.Blob is null)
                    throw new InvalidDataException($"Parity scene '{scene.Id}' has an invalid control scene.");
                if (control.BlobWidth != scene.BlobWidth || control.BlobHeight != scene.BlobHeight)
                    throw new InvalidDataException($"Parity/control dimensions differ for scene '{scene.Id}'.");
                if (scene.NonVacuity.MetricMode != scene.NonVacuityMode
                    || scene.NonVacuity.MetricRegion != scene.NonVacuityRegion)
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' has inconsistent non-vacuity declarations.");
                }
                if (scene.NonVacuity.ParityTolerance != NonVacuityParityTolerance
                    || scene.NonVacuity.MarginAboveTolerance <= 0
                    || scene.NonVacuity.SampleCount <= 0)
                {
                    throw new InvalidDataException($"Scene '{scene.Id}' has invalid non-vacuity thresholds.");
                }
            }
            else if (scene.ControlSceneId is not null || scene.NonVacuity is not null)
            {
                throw new InvalidDataException($"Non-parity scene '{scene.Id}' carries parity-only evidence.");
            }
        }

        string[] artifactNames = [.. artifactHashes.Keys.Order(StringComparer.Ordinal)];
        string[] sceneBlobNames = [.. sceneByBlob.Keys.Order(StringComparer.Ordinal)];
        if (!artifactNames.SequenceEqual(sceneBlobNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException("Scene blob set does not match manifest.artifactSha256.");
        }

        string[] expectedFiles = ["manifest.json", .. artifactNames];
        Array.Sort(expectedFiles, StringComparer.Ordinal);
        string[] actualFiles =
        [
            .. Directory.EnumerateFiles(paths.BaselineDirectory, "*", SearchOption.TopDirectoryOnly)
                .Select(path => Path.GetFileName(path)!)
                .Order(StringComparer.Ordinal),
        ];
        if (!expectedFiles.SequenceEqual(actualFiles, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                "Immutable target baseline file set differs from its manifest. "
                + $"Expected [{string.Join(", ", expectedFiles)}], found [{string.Join(", ", actualFiles)}].");
        }

        foreach ((string blobName, string sha256) in artifactHashes)
        {
            string path = Path.Combine(paths.BaselineDirectory, blobName);
            VerifyFileHash(path, sha256, $"target baseline blob '{blobName}'");
            GpuPassFusionEvidenceScene scene = sceneByBlob[blobName];
            long expectedLength = checked((long)scene.BlobWidth * scene.BlobHeight * 8);
            long actualLength = new FileInfo(path).Length;
            if (actualLength != expectedLength)
            {
                throw new InvalidDataException(
                    $"RGBA16F blob '{blobName}' length mismatch: expected {expectedLength}, found {actualLength}.");
            }
        }
    }

    private static void ValidateAllocationFailures(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("manifest.allocationFailures must be an array.");

        var intents = new HashSet<string>(StringComparer.Ordinal);
        int index = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            string context = $"manifest.allocationFailures[{index++}]";
            ValidateExactObjectShape(item, s_expectedAllocationFailureProperties, context);
            string intent = ReadString(item, "intent", context);
            if (!intents.Add(intent))
                throw new InvalidDataException($"Duplicate allocation-failure intent '{intent}'.");

            ReadString(item, "injectionPoint", context);
            ReadString(item, "maxWorkingScale", context);
            string outcome = ReadString(item, "outcome", context);
            string? exceptionType = ReadNullableString(item, "exceptionType", context);
            string? exceptionMessage = ReadNullableString(item, "exceptionMessage", context);
            ValidateCounterObject(item.GetProperty("legacyCounters"), $"{context}.legacyCounters");
            ValidateStringArray(item.GetProperty("legacyEvents"), $"{context}.legacyEvents", requireNonEmpty: true);

            bool validOutcome = intent switch
            {
                "preview" => outcome == "dropped-output-without-throw"
                    && exceptionType is null
                    && exceptionMessage is null,
                "delivery" => outcome == "threw"
                    && exceptionType is not null
                    && exceptionMessage is not null,
                _ => false,
            };
            if (!validOutcome)
                throw new InvalidDataException($"Allocation-failure outcome is incomplete for intent '{intent}'.");
        }

        if (!intents.SetEquals(["delivery", "preview"]))
            throw new InvalidDataException("Allocation-failure evidence must contain preview and delivery exactly once.");
    }

    private static void ValidateBenchmark(JsonElement element)
    {
        ValidateExactObjectShape(
            element,
            ["command", "environment", "rawResultReference", "status"],
            "manifest.benchmark");
        foreach (string property in new[] { "command", "environment", "rawResultReference", "status" })
            ReadString(element, property, "manifest.benchmark");
    }

    private static GpuPassFusionPixelRegion? ReadNullablePixelRegion(JsonElement element, string context)
    {
        if (element.ValueKind == JsonValueKind.Null)
            return null;

        ValidateExactObjectShape(element, ["height", "width", "x", "y"], context);
        return new GpuPassFusionPixelRegion(
            ReadInt32(element, "x", context),
            ReadInt32(element, "y", context),
            ReadInt32(element, "width", context),
            ReadInt32(element, "height", context));
    }

    private static void ValidateExactObjectShape(
        JsonElement element,
        IReadOnlyCollection<string> expectedNames,
        string context)
    {
        RequireObject(element, context);
        string[] actualNames = [.. element.EnumerateObject().Select(item => item.Name).Order(StringComparer.Ordinal)];
        if (!actualNames.SequenceEqual(expectedNames, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{context} schema mismatch. Expected [{string.Join(", ", expectedNames)}], "
                + $"found [{string.Join(", ", actualNames)}].");
        }
    }

    private static void ValidateObject(JsonElement element, string context) => RequireObject(element, context);

    private static void ValidateNullOrObject(JsonElement element, string context)
    {
        if (element.ValueKind is not (JsonValueKind.Null or JsonValueKind.Object))
            throw new InvalidDataException($"{context} must be null or an object.");
    }

    private static void ValidateCounterObject(JsonElement element, string context)
    {
        RequireObject(element, context);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (JsonProperty property in element.EnumerateObject())
        {
            if (!names.Add(property.Name)
                || !property.Value.TryGetInt64(out long value)
                || value < 0)
            {
                throw new InvalidDataException($"{context}.{property.Name} is not a unique non-negative counter.");
            }
        }

        if (names.Count == 0)
            throw new InvalidDataException($"{context} must contain a request-wide counter snapshot.");
    }

    private static void ValidateStringArray(JsonElement element, string context, bool requireNonEmpty = false)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"{context} must be an array.");
        int count = 0;
        foreach (JsonElement item in element.EnumerateArray())
        {
            ReadStringValue(item, $"{context}[]");
            count++;
        }
        if (requireNonEmpty && count == 0)
            throw new InvalidDataException($"{context} must not be empty.");
    }

    private static void RequireObject(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{context} must be an object.");
    }

    private static string ReadString(JsonElement parent, string name, string context)
        => ReadStringValue(ReadRequiredProperty(parent, name, context), $"{context}.{name}");

    private static string? ReadNullableString(JsonElement parent, string name, string context)
    {
        JsonElement element = ReadRequiredProperty(parent, name, context);
        return element.ValueKind == JsonValueKind.Null
            ? null
            : ReadStringValue(element, $"{context}.{name}");
    }

    private static string ReadStringValue(JsonElement element, string context)
    {
        if (element.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{context} must be a string.");
        string? value = element.GetString();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidDataException($"{context} must not be empty.");
        return value;
    }

    private static int ReadInt32(JsonElement parent, string name, string context)
    {
        JsonElement element = ReadRequiredProperty(parent, name, context);
        if (!element.TryGetInt32(out int value))
            throw new InvalidDataException($"{context}.{name} must be a 32-bit integer.");
        return value;
    }

    private static bool ReadBoolean(JsonElement parent, string name, string context)
    {
        JsonElement element = ReadRequiredProperty(parent, name, context);
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{context}.{name} must be a Boolean.");
        return element.GetBoolean();
    }

    private static double ReadFiniteNumber(JsonElement parent, string name, string context)
    {
        JsonElement element = ReadRequiredProperty(parent, name, context);
        if (!element.TryGetDouble(out double value) || !double.IsFinite(value))
            throw new InvalidDataException($"{context}.{name} must be a finite number.");
        return value;
    }

    private static string ReadSha256(JsonElement parent, string name, string context)
    {
        string value = ReadString(parent, name, context);
        ValidateSha256(value, $"{context}.{name}");
        return value;
    }

    private static JsonElement ReadRequiredProperty(JsonElement parent, string name, string context)
    {
        RequireObject(parent, context);
        if (!parent.TryGetProperty(name, out JsonElement result))
            throw new InvalidDataException($"{context}.{name} is missing.");
        return result;
    }

    private static void RequireExactString(
        JsonElement parent,
        string name,
        string expected,
        string context)
    {
        string value = ReadString(parent, name, context);
        if (!string.Equals(value, expected, StringComparison.Ordinal))
            throw new InvalidDataException($"{context}.{name} must be '{expected}', not '{value}'.");
    }

    private static void ValidateCommitTimestamp(string value)
    {
        if (!DateTimeOffset.TryParseExact(
                value,
                "yyyy-MM-dd'T'HH:mm:sszzz",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _))
        {
            throw new InvalidDataException("manifest.baselineCommitTimestamp is not an exact offset timestamp.");
        }
    }

    private static void ValidateSafeBlobName(string name)
    {
        if (!name.EndsWith(Rgba16fGoldenStore.Extension, StringComparison.Ordinal)
            || !string.Equals(name, Path.GetFileName(name), StringComparison.Ordinal)
            || name.Contains('/')
            || name.Contains('\\'))
        {
            throw new InvalidDataException($"Unsafe or non-RGBA16F artifact name '{name}'.");
        }
    }

    private static void ValidateSha256(string value, string context)
    {
        if (value.Length != 64 || value.Any(item => item is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
            throw new InvalidDataException($"{context} must be a lowercase SHA-256 digest.");
    }

    private static FileStream OpenExistingReadOnly(string path, string description)
    {
        try
        {
            return new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);
        }
        catch (FileNotFoundException ex)
        {
            throw new FileNotFoundException($"Required {description} is missing: {path}", path, ex);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new FileNotFoundException($"Required {description} is missing: {path}", path, ex);
        }
    }

    private static float HalfBitsToFiniteFloat(ushort bits, Bitmap bitmap, int x, int y, int channel)
    {
        float value = (float)BitConverter.UInt16BitsToHalf(bits);
        if (!float.IsFinite(value))
        {
            throw new InvalidDataException(
                $"RGBA16F evidence contains a non-finite value at {bitmap.Width}x{bitmap.Height} "
                + $"(x={x}, y={y}, channel={channel}).");
        }

        return value;
    }

    private static bool IsNonVacuityCoverageEdge(float alpha) => alpha > 0.01f && alpha < 0.75f;
}

internal static class GpuPassFusionSameProcessParityHarness
{
    public const double MinimumSsim = 0.99;
    public const double MaximumLinearRgbMae = 0.02;
    public const double MaximumAlphaMae = 0.02;
    public const double MaximumAaEdgeChannelError = 0.02;
    public const double MaximumAaEdgeMeanError = 0.02;

    public static GpuPassFusionParityResult AssertParity(
        Func<FusionMode, Bitmap> render,
        PixelRect? aaEdgeRegion = null)
    {
        ArgumentNullException.ThrowIfNull(render);

        using Bitmap disabled = render(FusionMode.Disabled)
            ?? throw new InvalidOperationException("The fusion-disabled render returned null.");
        using Bitmap enabled = render(FusionMode.Enabled)
            ?? throw new InvalidOperationException("The fusion-enabled render returned null.");
        if (ReferenceEquals(disabled, enabled))
            throw new InvalidOperationException("Fusion-disabled and enabled runs must return independently owned images.");

        string? nonFinite = ImageMetrics.FirstNonFinite(
            ("fusion-disabled", disabled),
            ("fusion-enabled", enabled));
        Assert.That(nonFinite, Is.Null, "Same-process parity inputs must contain only finite RGBA16F values.");

        GpuPassFusionParityMetrics fullImage = Measure(disabled, enabled);
        GpuPassFusionAaParityMetrics? aaEdge = null;
        if (aaEdgeRegion is { } region)
        {
            ValidateCrop(region, disabled.Width, disabled.Height);
            using Bitmap disabledCrop = Crop(disabled, region);
            using Bitmap enabledCrop = Crop(enabled, region);
            GpuPassFusionParityMetrics cropMetrics = Measure(disabledCrop, enabledCrop);
            double edgeMeanError = ImageMetrics.EdgeBandMeanAbsoluteError(disabledCrop, enabledCrop);
            RgbaMaximumError edgeMaximum =
                ImageMetrics.EdgeBandMaximumAbsoluteErrorPerChannel(disabledCrop, enabledCrop);
            aaEdge = new GpuPassFusionAaParityMetrics(cropMetrics, edgeMeanError, edgeMaximum);
        }

        using (Assert.EnterMultipleScope())
        {
            AssertMetrics(fullImage, "full image");
            if (aaEdge is { } edge)
            {
                AssertMetrics(edge.Crop, "AA edge crop");
                Assert.That(
                    edge.EdgeBandMeanError,
                    Is.LessThanOrEqualTo(MaximumAaEdgeMeanError),
                    "AA edge-band mean error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Red,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge red-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Green,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge green-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Blue,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge blue-channel maximum error exceeded the fixed normal-CI bound.");
                Assert.That(
                    edge.MaximumError.Alpha,
                    Is.LessThanOrEqualTo(MaximumAaEdgeChannelError),
                    "AA edge alpha-channel maximum error exceeded the fixed normal-CI bound.");
            }
        }

        return new GpuPassFusionParityResult(fullImage, aaEdge);
    }

    private static GpuPassFusionParityMetrics Measure(Bitmap disabled, Bitmap enabled)
    {
        return new GpuPassFusionParityMetrics(
            ImageMetrics.Ssim(disabled, enabled),
            ImageMetrics.MeanAbsoluteError(disabled, enabled),
            ImageMetrics.AlphaMeanAbsoluteError(disabled, enabled));
    }

    private static void AssertMetrics(GpuPassFusionParityMetrics metrics, string region)
    {
        Assert.That(metrics.Ssim, Is.GreaterThanOrEqualTo(MinimumSsim), $"{region} SSIM was too low.");
        Assert.That(
            metrics.LinearRgbMae,
            Is.LessThanOrEqualTo(MaximumLinearRgbMae),
            $"{region} linear RGB MAE was too high.");
        Assert.That(
            metrics.AlphaMae,
            Is.LessThanOrEqualTo(MaximumAlphaMae),
            $"{region} alpha MAE was too high.");
    }

    private static Bitmap Crop(Bitmap source, PixelRect region)
    {
        var result = new Bitmap(
            region.Width,
            region.Height,
            BitmapColorType.RgbaF16,
            BitmapAlphaType.Premul,
            BitmapColorSpace.LinearSrgb);
        for (int y = 0; y < region.Height; y++)
        {
            ReadOnlySpan<ushort> sourceRow = source.GetRow<ushort>(region.Y + y);
            Span<ushort> destinationRow = result.GetRow<ushort>(y);
            sourceRow.Slice(region.X * 4, region.Width * 4).CopyTo(destinationRow);
        }

        return result;
    }

    private static void ValidateCrop(PixelRect region, int width, int height)
    {
        if (region.X < 0
            || region.Y < 0
            || region.Width <= 0
            || region.Height <= 0
            || region.Right > width
            || region.Bottom > height)
        {
            throw new ArgumentOutOfRangeException(
                nameof(region),
                region,
                $"AA edge region must be a non-empty subset of the {width}x{height} output.");
        }
    }
}

internal sealed record GpuPassFusionEvidenceManifest(
    GpuPassFusionEvidencePaths Paths,
    int SchemaVersion,
    string BaselineCodeSha,
    string PatchedDiffSha256,
    int GeneratorSeed,
    GpuPassFusionEvidenceTools EvidenceTools,
    IReadOnlyDictionary<string, IReadOnlyList<string>> Fingerprint,
    IReadOnlyDictionary<string, string> ArtifactHashes,
    IReadOnlyList<GpuPassFusionEvidenceScene> Scenes)
{
    public GpuPassFusionEvidenceScene GetScene(string id)
    {
        return Scenes.SingleOrDefault(scene => string.Equals(scene.Id, id, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Manifest scene '{id}' is missing.");
    }
}

internal sealed record GpuPassFusionEvidencePaths(
    string RepositoryRoot,
    string EvidenceDirectory,
    string BaselineDirectory,
    string ManifestPath,
    string GeneratorPatchPath,
    string GeneratorScriptPath,
    string PairedRunnerPath)
{
    public static GpuPassFusionEvidencePaths Discover()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
            directory = directory.Parent;

        if (directory is null)
        {
            throw new DirectoryNotFoundException(
                $"Could not locate the Beutl repository root above {AppContext.BaseDirectory}.");
        }

        string repositoryRoot = directory.FullName;
        string evidenceDirectory = Path.Combine(
            repositoryRoot,
            "docs",
            "specs",
            "004-gpu-pass-fusion",
            "evidence");
        string baselineDirectory = Path.Combine(evidenceDirectory, "target-baseline");
        return new GpuPassFusionEvidencePaths(
            repositoryRoot,
            evidenceDirectory,
            baselineDirectory,
            Path.Combine(baselineDirectory, "manifest.json"),
            Path.Combine(evidenceDirectory, "target-baseline-generator.patch"),
            Path.Combine(evidenceDirectory, "generate-target-baseline.sh"),
            Path.Combine(evidenceDirectory, "run-paired-visual-evidence.sh"));
    }
}

internal sealed record GpuPassFusionEvidenceTools(
    string GeneratorPatchSha256,
    string GeneratorScriptSha256,
    string PairedRunnerSha256,
    string GeneratorSourceBundleSha256);

internal sealed record GpuPassFusionEvidenceScene(
    string Id,
    string Category,
    string Role,
    string? ControlSceneId,
    string? Blob,
    int BlobWidth,
    int BlobHeight,
    string NonVacuityMode,
    GpuPassFusionPixelRegion? NonVacuityRegion,
    GpuPassFusionNonVacuityRecord? NonVacuity);

internal sealed record GpuPassFusionNonVacuityRecord(
    double LinearRgbMae,
    double AlphaMae,
    double MaximumChannelError,
    int SampleCount,
    string MetricMode,
    GpuPassFusionPixelRegion? MetricRegion,
    double ParityTolerance,
    double MarginAboveTolerance);

internal readonly record struct GpuPassFusionNonVacuityDelta(
    double LinearRgbMae,
    double AlphaMae,
    double MaximumChannelError,
    int SampleCount,
    double MarginAboveTolerance);

internal readonly record struct GpuPassFusionPixelRegion(int X, int Y, int Width, int Height)
{
    public int Right => checked(X + Width);

    public int Bottom => checked(Y + Height);

    public void ValidateInside(int imageWidth, int imageHeight, string description)
    {
        if (X < 0 || Y < 0 || Width <= 0 || Height <= 0 || Right > imageWidth || Bottom > imageHeight)
        {
            throw new InvalidDataException(
                $"{description} ({X}, {Y}, {Width}, {Height}) is not a non-empty subset of "
                + $"{imageWidth}x{imageHeight}.");
        }
    }
}

internal readonly record struct GpuPassFusionParityMetrics(
    double Ssim,
    double LinearRgbMae,
    double AlphaMae);

internal readonly record struct GpuPassFusionAaParityMetrics(
    GpuPassFusionParityMetrics Crop,
    double EdgeBandMeanError,
    RgbaMaximumError MaximumError);

internal readonly record struct GpuPassFusionParityResult(
    GpuPassFusionParityMetrics FullImage,
    GpuPassFusionAaParityMetrics? AaEdge);
