using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

using Beutl.Composition;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Backend;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Rendering;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Graphics3D;
using Beutl.Graphics3D.Camera;
using Beutl.Graphics3D.Lighting;
using Beutl.Graphics3D.Materials;
using Beutl.Graphics3D.Primitives;
using Beutl.Media;
using Beutl.Media.Source;

using SkiaSharp;

using Bitmap = Beutl.Media.Bitmap;

namespace Beutl.Benchmarks.Rendering;

/// <summary>
/// Recreates the pinned target-baseline scene table with the current production
/// <see cref="RenderNodeRenderer"/> and writes independent RGBA16F evidence artifacts.
/// </summary>
internal static class FeatureVisualEvidenceExporter
{
    private const int Seed = 20_040_719;
    private const string OutputDirectoryEnvironmentVariable = "BEUTL_GPU_PASS_EVIDENCE_OUTPUT_DIR";
    private const string BaselineManifestEnvironmentVariable = "BEUTL_GPU_PASS_BASELINE_MANIFEST";
    private const string EvidenceModeEnvironmentVariable = "BEUTL_GPU_PASS_EVIDENCE_MODE";

    private static readonly PixelSize s_frame = new(192, 108);
    private static readonly Rect s_domain = new(0, 0, s_frame.Width, s_frame.Height);
    private static readonly Rect s_allocationProbeOutputBounds = new(5, 3, 182, 102);
    private static readonly PixelSize s_allocationProbeFailureSize = new(174, 94);
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length != 0)
                throw new ArgumentException("feature-visual-export does not accept positional arguments.", nameof(args));
            if (!string.Equals(
                    Environment.GetEnvironmentVariable(EvidenceModeEnvironmentVariable),
                    "feature",
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{EvidenceModeEnvironmentVariable} must be exactly 'feature'.");
            }

            string outputDirectory = RequireEnvironment(OutputDirectoryEnvironmentVariable);
            string baselineManifestPath = RequireEnvironment(BaselineManifestEnvironmentVariable);
            PrepareCreateOnlyDirectory(outputDirectory);

            JsonObject baseline = LoadManifest(baselineManifestPath);
            FeatureVisualGeneration generation = RenderThread.Dispatcher.Invoke(
                () => Generate(baseline, baselineManifestPath));
            WriteGeneration(outputDirectory, generation);

            output.WriteLine(
                $"Generated {generation.Artifacts.Count} independent feature RGBA16F artifacts and manifest.json at {outputDirectory}");
            return 0;
        }
        catch (Exception exception)
        {
            error.WriteLine(exception);
            return 1;
        }
    }

    private static FeatureVisualGeneration Generate(JsonObject baseline, string baselineManifestPath)
    {
        RenderThread.Dispatcher.VerifyAccess();
        IGraphicsContext graphicsContext = GraphicsContextFactory.GetOrCreateShared()
            ?? throw new InvalidOperationException("A real graphics context is required for feature visual evidence.");
        if (!graphicsContext.Supports3DRendering)
            throw new InvalidOperationException("The selected graphics context cannot render the required 3D scene.");

        RenderPipelineEvidenceFingerprint fingerprint = RenderPipelineEvidenceFingerprint.Capture(graphicsContext);
        JsonArray baselineScenes = baseline["scenes"]?.AsArray()
            ?? throw new InvalidDataException("The baseline scene table is missing.");
        var artifacts = new SortedDictionary<string, byte[]>(StringComparer.Ordinal);
        var scenes = new JsonArray();

        foreach (JsonNode? item in baselineScenes)
        {
            JsonObject baselineScene = item?.AsObject()
                ?? throw new InvalidDataException("A baseline scene is not an object.");
            string id = RequiredString(baselineScene, "id");
            JsonObject featureScene = CopySemanticSceneFields(baselineScene);
            string? blob = baselineScene["blob"]?.GetValue<string>();
            if (blob is null)
            {
                FeatureMetadataCapture metadata = CaptureMetadataScene(id, baselineScene);
                featureScene["requestCounters"] = JsonSerializer.SerializeToNode(
                    metadata.RequestCounters,
                    s_jsonOptions);
                if (metadata.Query is not null)
                    featureScene["query"] = metadata.Query;
                scenes.Add(featureScene);
                continue;
            }

            FeatureVisualCapture first = CaptureVisualScene(id, baselineScene);
            FeatureVisualCapture second = CaptureVisualScene(id, baselineScene);
            if (!first.Bytes.AsSpan().SequenceEqual(second.Bytes))
                throw new InvalidOperationException($"Feature scene '{id}' is not byte-stable across clean captures.");
            if (!DictionariesEqual(first.RequestCounters, second.RequestCounters))
                throw new InvalidOperationException($"Feature scene '{id}' emitted unstable request-wide counters.");

            int expectedWidth = RequiredInt32(baselineScene, "blobWidth");
            int expectedHeight = RequiredInt32(baselineScene, "blobHeight");
            if (first.Width != expectedWidth || first.Height != expectedHeight)
            {
                throw new InvalidOperationException(
                    $"Feature scene '{id}' produced {first.Width}x{first.Height}; expected {expectedWidth}x{expectedHeight}.");
            }

            artifacts.Add(blob, first.Bytes);
            featureScene["requestCounters"] = JsonSerializer.SerializeToNode(first.RequestCounters, s_jsonOptions);
            featureScene["lastExecutionStatistics"] = JsonSerializer.SerializeToNode(
                first.LastExecutionStatistics,
                s_jsonOptions);
            featureScene["structuralPlanCacheStatistics"] = JsonSerializer.SerializeToNode(
                first.StructuralPlanCacheStatistics,
                s_jsonOptions);
            featureScene["programCacheStatistics"] = JsonSerializer.SerializeToNode(
                first.ProgramCacheStatistics,
                s_jsonOptions);
            featureScene["targetPoolStatistics"] = JsonSerializer.SerializeToNode(
                first.TargetPoolStatistics,
                s_jsonOptions);
            scenes.Add(featureScene);
        }

        AddNonVacuityMetrics(scenes, artifacts);
        JsonObject sameProcessFusionParity = CaptureSameProcessFusionParity(baselineScenes);
        JsonArray allocationFailures = CaptureAllocationFailures();

        var hashes = new JsonObject();
        foreach ((string name, byte[] bytes) in artifacts)
            hashes[name] = Sha256Hex(bytes);

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["featureCodeSha"] = CurrentCodeSha(),
            ["generatorSeed"] = baseline["generatorSeed"]?.DeepClone() ?? Seed,
            ["evidenceMode"] = "feature-current-production-render-node-renderer",
            ["baselineManifestSha256"] = Sha256Hex(File.ReadAllBytes(baselineManifestPath)),
            ["exporterAssemblyVersion"] = AssemblyVersion(typeof(FeatureVisualEvidenceExporter).Assembly),
            ["fingerprint"] = JsonSerializer.SerializeToNode(fingerprint, s_jsonOptions),
            ["pixelFormat"] = baseline["pixelFormat"]?.DeepClone()
                ?? throw new InvalidDataException("The baseline pixel-format contract is missing."),
            ["artifactSha256"] = hashes,
            ["scenes"] = scenes,
            ["allocationFailures"] = allocationFailures,
            ["sameProcessFusionParity"] = sameProcessFusionParity,
        };
        return new FeatureVisualGeneration(manifest, artifacts);
    }

    private static JsonArray CaptureAllocationFailures()
    {
        return
        [
            CaptureAllocationFailure(RenderIntent.Preview, maxWorkingScale: 2),
            CaptureAllocationFailure(RenderIntent.Delivery, maxWorkingScale: float.PositiveInfinity),
        ];
    }

    internal static JsonArray CaptureAllocationFailuresForTest()
    {
        RenderThread.Dispatcher.VerifyAccess();
        return CaptureAllocationFailures();
    }

    private static JsonObject CaptureAllocationFailure(RenderIntent intent, float maxWorkingScale)
    {
        FeatureAllocationFailureCapture first = RunAllocationFailure(intent, maxWorkingScale);
        FeatureAllocationFailureCapture second = RunAllocationFailure(intent, maxWorkingScale);
        if (first with { Counters = second.Counters } != second
            || !DictionariesEqual(first.Counters, second.Counters))
            throw new InvalidOperationException($"Feature {intent} allocation-failure evidence is not stable.");

        const float expectedOutputScale = 1;
        PixelSize expectedOutputDeviceSize = PixelRect.FromRect(
            s_allocationProbeOutputBounds,
            expectedOutputScale).Size;
        bool valid = intent switch
        {
            RenderIntent.Preview => first is
            {
                Outcome: "dropped-output-without-throw",
                Threw: false,
                RequestSucceeded: true,
                OutputIsEmpty: false,
                OutputNonZeroComponents: 0,
                OutputNonFiniteComponents: 0,
                OutputSha256: not null,
            }
                && first.OutputBounds == s_allocationProbeOutputBounds
                && first.OutputScale == expectedOutputScale
                && first.OutputBitmapWidth == expectedOutputDeviceSize.Width
                && first.OutputBitmapHeight == expectedOutputDeviceSize.Height
                && Counter(first.Counters, "PreviewAllocationDrops") == 1
                && Counter(first.Counters, "Failures") == 0
                && Counter(first.Counters, "CleanupFailures") == 0,
            RenderIntent.Delivery => first is
            {
                Outcome: "threw",
                Threw: true,
                RequestSucceeded: false,
                OutputBounds: null,
                OutputScale: null,
                OutputIsEmpty: null,
                OutputBitmapWidth: null,
                OutputBitmapHeight: null,
                OutputNonZeroComponents: null,
                OutputNonFiniteComponents: null,
                OutputSha256: null,
            }
                && Counter(first.Counters, "PreviewAllocationDrops") == 0
                && Counter(first.Counters, "Failures") == 1
                && Counter(first.Counters, "CleanupFailures") == 0
                && first.ExceptionType == typeof(InvalidOperationException).FullName
                && first.ExceptionMessage ==
                    $"The render-target factory could not allocate "
                    + $"{s_allocationProbeFailureSize.Width}x{s_allocationProbeFailureSize.Height} pixels.",
            _ => false,
        };
        if (!valid
            || first.FailureConsumed is false
            || first.TargetFactoryCreateCalls != 2
            || first.FailedAllocationWidth != s_allocationProbeFailureSize.Width
            || first.FailedAllocationHeight != s_allocationProbeFailureSize.Height)
        {
            throw new InvalidOperationException(
                $"Feature {intent} allocation-failure behavior did not match the render-intent contract: "
                + JsonSerializer.Serialize(first, s_jsonOptions));
        }

        return new JsonObject
        {
            ["intent"] = intent == RenderIntent.Preview ? "preview" : "delivery",
            ["injectionPoint"] = "next EffectMaterialization RenderTarget.Create",
            ["maxWorkingScale"] = float.IsPositiveInfinity(maxWorkingScale)
                ? "+Infinity"
                : maxWorkingScale.ToString("R", CultureInfo.InvariantCulture),
            ["outcome"] = first.Outcome,
            ["exceptionType"] = first.ExceptionType,
            ["exceptionMessage"] = first.ExceptionMessage,
            ["requestSucceeded"] = first.RequestSucceeded,
            ["outputBounds"] = first.OutputBounds is { } outputBounds ? RectString(outputBounds) : null,
            ["outputScale"] = first.OutputScale,
            ["outputIsEmpty"] = first.OutputIsEmpty,
            ["outputBitmapWidth"] = first.OutputBitmapWidth,
            ["outputBitmapHeight"] = first.OutputBitmapHeight,
            ["outputNonZeroComponents"] = first.OutputNonZeroComponents,
            ["outputNonFiniteComponents"] = first.OutputNonFiniteComponents,
            ["outputSha256"] = first.OutputSha256,
            ["featureCounters"] = JsonSerializer.SerializeToNode(first.Counters, s_jsonOptions),
            ["targetFactoryCreateCalls"] = first.TargetFactoryCreateCalls,
            ["failedAllocationWidth"] = first.FailedAllocationWidth,
            ["failedAllocationHeight"] = first.FailedAllocationHeight,
        };
    }

    private static FeatureAllocationFailureCapture RunAllocationFailure(
        RenderIntent intent,
        float maxWorkingScale)
    {
        using FeatureSceneFixture fixture = BuildStrokeEffectScene(applyEffect: true);
        var factory = new FailSecondTargetFactory();
        object diagnostics = RenderPipelineInternalDiagnostics.CreateState();
        var options = new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = intent,
                TargetDomain = s_domain,
                OutputScale = 1,
                MaxWorkingScale = maxWorkingScale,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
            TargetFactory = factory,
        };
        RenderPipelineInternalDiagnostics.Attach(options, diagnostics, RenderRequestPurpose.Frame);
        string outcome = "threw";
        string? exceptionType = null;
        string? exceptionMessage = null;
        Rect? outputBounds = null;
        float? outputScale = null;
        bool? outputIsEmpty = null;
        int? outputBitmapWidth = null;
        int? outputBitmapHeight = null;
        int? outputNonZeroComponents = null;
        int? outputNonFiniteComponents = null;
        string? outputSha256 = null;
        using (var renderer = new RenderNodeRenderer(fixture.Root, options))
        {
            try
            {
                using RenderNodeRasterization rasterization = renderer.Rasterize();
                outputBounds = rasterization.Bounds;
                outputScale = rasterization.OutputScale;
                outputIsEmpty = rasterization.IsEmpty;
                if (rasterization.IsEmpty)
                {
                    outcome = "empty-output-without-throw";
                }
                else if (rasterization.Bitmap is not { } bitmap)
                {
                    outcome = "missing-bitmap-without-throw";
                }
                else
                {
                    outputBitmapWidth = bitmap.Width;
                    outputBitmapHeight = bitmap.Height;
                    (int nonZero, int nonFinite, string sha256) = InspectAllocationOutput(bitmap);
                    outputNonZeroComponents = nonZero;
                    outputNonFiniteComponents = nonFinite;
                    outputSha256 = sha256;
                    outcome = nonFinite > 0
                        ? "non-finite-output-without-throw"
                        : nonZero > 0
                            ? "rendered-output-without-throw"
                            : "dropped-output-without-throw";
                }
            }
            catch (Exception exception)
            {
                exceptionType = exception.GetType().FullName;
                exceptionMessage = exception.Message;
            }
        }

        SortedDictionary<string, long> counters =
            RenderPipelineInternalDiagnostics.CaptureLatestCounters(diagnostics, out bool requestSucceeded);
        return new FeatureAllocationFailureCapture(
            outcome,
            exceptionType is not null,
            exceptionType,
            exceptionMessage,
            requestSucceeded,
            outputBounds,
            outputScale,
            outputIsEmpty,
            outputBitmapWidth,
            outputBitmapHeight,
            outputNonZeroComponents,
            outputNonFiniteComponents,
            outputSha256,
            factory.FailureConsumed,
            factory.CreateCalls,
            factory.FailedDeviceSize?.Width,
            factory.FailedDeviceSize?.Height,
            counters);
    }

    private static (int NonZeroComponents, int NonFiniteComponents, string Sha256) InspectAllocationOutput(Bitmap bitmap)
    {
        int nonZeroComponents = 0;
        int nonFiniteComponents = 0;
        foreach (ushort bits in bitmap.GetPixelSpan<ushort>())
        {
            float value = (float)BitConverter.UInt16BitsToHalf(bits);
            if (!float.IsFinite(value))
                nonFiniteComponents++;
            else if (value != 0)
                nonZeroComponents++;
        }

        return (nonZeroComponents, nonFiniteComponents, Sha256Hex(Rgba16fEvidenceWriter.Encode(bitmap)));
    }

    private static FeatureVisualCapture CaptureVisualScene(string id, JsonObject scene)
    {
        float outputScale = RequiredSingle(scene, "outputScale");
        float maxWorkingScale = float.Parse(
            RequiredString(scene, "maxWorkingScale"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        RequestedRegion? requestedRegion = ParseRequestedRegion(scene["requestedRegion"]);

        return id switch
        {
            "multiple-composition-roots" => RenderCompositionRoots(includeSecond: true),
            "multiple-composition-roots-control" => RenderCompositionRoots(includeSecond: false),
            "nested-drawable-brush-delay" => RenderDrawable(
                BuildNestedDrawableBrushDelay(), outputScale, maxWorkingScale),
            "nested-drawable-brush-delay-control" => RenderDrawable(
                BuildNestedControl(), outputScale, maxWorkingScale),
            "scene3d-with-2d-tail" => RenderDrawable(
                BuildThreeDScene(includeFilterEffect: true), outputScale, maxWorkingScale),
            "scene3d-no-2d-tail-control" => RenderDrawable(
                BuildThreeDScene(includeFilterEffect: false), outputScale, maxWorkingScale),
            "cache-cold" => RenderCacheScene(warm: false, drawContent: true),
            "cache-warm-hit" => RenderCacheScene(warm: true, drawContent: true),
            "cache-control-empty" => RenderCacheScene(warm: false, drawContent: false),
            _ => RenderManual(
                CreateManualFixture(id),
                outputScale,
                maxWorkingScale,
                requestedRegion),
        };
    }

    internal static FeatureVisualCapture CaptureVisualSceneForTest(
        string id,
        float outputScale,
        float maxWorkingScale,
        PixelRect? requestedRegion)
    {
        RenderThread.Dispatcher.VerifyAccess();
        var scene = new JsonObject
        {
            ["outputScale"] = outputScale,
            ["maxWorkingScale"] = maxWorkingScale.ToString("R", CultureInfo.InvariantCulture),
            ["requestedRegion"] = requestedRegion is { } region
                ? new JsonObject
                {
                    ["x"] = region.X,
                    ["y"] = region.Y,
                    ["width"] = region.Width,
                    ["height"] = region.Height,
                }
                : null,
        };
        return CaptureVisualScene(id, scene);
    }

    private static FeatureVisualCapture RenderManual(
        FeatureSceneFixture fixture,
        float outputScale,
        float maxWorkingScale,
        RequestedRegion? requestedRegion)
    {
        using (fixture)
        {
            return RenderExistingNode(
                fixture.Root,
                outputScale,
                maxWorkingScale,
                fixture.UseRenderCache,
                requestedRegion);
        }
    }

    private static FeatureVisualCapture RenderDrawable(
        Drawable.Resource resource,
        float outputScale,
        float maxWorkingScale)
    {
        using (resource)
        {
            using (var drawable = new DrawableRenderNode(resource))
            {
                using (var graphics = new GraphicsContext2D(drawable, s_frame.ToSize(1), outputScale))
                    resource.GetOriginal().Render(graphics, resource);
                return RenderExistingNode(
                    drawable,
                    outputScale,
                    maxWorkingScale,
                    useCache: false,
                    requestedRegion: null);
            }
        }
    }

    private static FeatureVisualCapture RenderExistingNode(
        RenderNode root,
        float outputScale,
        float maxWorkingScale,
        bool useCache,
        RequestedRegion? requestedRegion,
        string? fusionMode = null)
    {
        int width = checked((int)MathF.Ceiling(s_frame.Width * outputScale));
        int height = checked((int)MathF.Ceiling(s_frame.Height * outputScale));
        using RenderTarget target = RenderTarget.Create(width, height)
            ?? throw new InvalidOperationException($"Could not allocate a {width}x{height} feature evidence target.");
        object diagnostics = RenderPipelineInternalDiagnostics.CreateState();
        var options = new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_domain,
                OutputScale = outputScale,
                MaxWorkingScale = maxWorkingScale,
                CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(useCache, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
                RequestedRegion = requestedRegion?.ToRect(),
            },
        };
        if (fusionMode is not null)
            SetInternalFusionMode(options, fusionMode);
        RenderPipelineInternalDiagnostics.Attach(options, diagnostics, RenderRequestPurpose.Frame);
        using var renderer = new RenderNodeRenderer(root, options);
        byte[]? initializedPixels = null;
        PixelRect? requestedDeviceRegion = null;
        if (requestedRegion is { } region)
        {
            requestedDeviceRegion = ResolveRequestedPixelRegion(
                width,
                height,
                outputScale,
                region);
            SKCanvas nativeCanvas = target.Value.Canvas;
            nativeCanvas.Clear(new SKColor(23, 47, 89, 255));
            using (var clearPaint = new SKPaint
            {
                BlendMode = SKBlendMode.Src,
                Color = SKColors.Transparent,
            })
            {
                PixelRect initializedRegion = requestedDeviceRegion.Value;
                nativeCanvas.DrawRect(
                    initializedRegion.X,
                    initializedRegion.Y,
                    initializedRegion.Width,
                    initializedRegion.Height,
                    clearPaint);
            }
            target.Value.Flush();
            using Bitmap initialized = target.Snapshot();
            initializedPixels = Rgba16fEvidenceWriter.Encode(initialized);
        }
        using (var canvas = new ImmediateCanvas(target, outputScale, maxWorkingScale, s_frame.ToSize(1)))
        {
            if (requestedRegion is null)
                canvas.Clear();
            renderer.Render(canvas);
        }

        Bitmap? selected;
        using (Bitmap full = target.Snapshot())
        {
            if (initializedPixels is not null)
            {
                AssertOutsideRequestedRegionUnchanged(
                    initializedPixels,
                    Rgba16fEvidenceWriter.Encode(full),
                    width,
                    height,
                    requestedDeviceRegion!.Value);
            }
            selected = SelectRequestedRegion(full, outputScale, requestedRegion);
        }
        if (selected is null)
            throw new InvalidOperationException("A visual scene unexpectedly produced an empty requested region.");
        using (selected)
        {
            return CaptureBitmap(selected, renderer, diagnostics);
        }
    }

    private static FeatureVisualCapture RenderCompositionRoots(bool includeSecond)
    {
        using Drawable.Resource first = BuildCompositionRect(left: true);
        using Drawable.Resource? second = includeSecond ? BuildCompositionRect(left: false) : null;
        ImmutableArray<EngineObject.Resource> objects = includeSecond ? [first, second!] : [first];
        var frame = new CompositionFrame(objects, new TimeRange(TimeSpan.Zero, TimeSpan.FromTicks(1)), s_frame);
        var diagnostics = new RenderPipelineDiagnosticsState();
        using var renderer = new Renderer(
            s_frame.Width,
            s_frame.Height,
            renderScale: 1,
            maxWorkingScale: 2,
            diagnostics: diagnostics,
            surface: null)
        {
            CacheOptions = RenderCacheOptions.Disabled,
        };
        renderer.Render(frame);
        using Bitmap bitmap = renderer.Snapshot();

        SortedDictionary<string, long> counters =
            RenderPipelineInternalDiagnostics.CaptureLatestCounters(diagnostics, out bool succeeded);
        if (!succeeded)
            throw new InvalidOperationException("The composition-root evidence request failed.");
        return new FeatureVisualCapture(
            bitmap.Width,
            bitmap.Height,
            Rgba16fEvidenceWriter.Encode(bitmap),
            counters,
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "FrameStructuralPlanCacheStatistics"),
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "FrameProgramCacheStatistics"),
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "FrameTargetPoolStatistics"),
            []);
    }

    private static FeatureVisualCapture RenderCacheScene(bool warm, bool drawContent)
    {
        using RenderNode node = drawContent
            ? new EllipseRenderNode(new Rect(31, 12, 118, 82), Brushes.Resource.CornflowerBlue, null)
            : new FeatureEmptySourceNode();
        if (warm)
            node.Cache.ReportRenderCount(RenderNodeCache.Count);

        using RenderTarget target = RenderTarget.Create(s_frame.Width, s_frame.Height)
            ?? throw new InvalidOperationException("Could not allocate the cache evidence target.");
        object diagnostics = RenderPipelineInternalDiagnostics.CreateState();
        var options = new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_domain,
                OutputScale = 1,
                MaxWorkingScale = 2,
                CacheOptions = new Beutl.Graphics.Rendering.Cache.RenderCacheOptions(warm, Beutl.Graphics.Rendering.Cache.RenderCacheRules.Default),
            },
        };
        RenderPipelineInternalDiagnostics.Attach(options, diagnostics, RenderRequestPurpose.Frame);
        using var renderer = new RenderNodeRenderer(node, options);
        using (var canvas = new ImmediateCanvas(target, 1, 2, s_frame.ToSize(1)))
        {
            canvas.Clear();
            renderer.Render(canvas);
            if (warm)
            {
                canvas.Clear();
                renderer.Render(canvas);
            }
        }

        using Bitmap bitmap = target.Snapshot();
        FeatureVisualCapture capture = CaptureBitmap(bitmap, renderer, diagnostics);
        if (warm
            && (!capture.RequestCounters.TryGetValue(nameof(RenderPipelineCounter.RenderCacheHits), out long hits)
                || hits != 1))
        {
            throw new InvalidOperationException(
                $"The warm-cache evidence scene must record exactly one render-cache hit; observed {hits}.");
        }

        return capture;
    }

    private static FeatureVisualCapture CaptureBitmap(
        Bitmap bitmap,
        RenderNodeRenderer renderer,
        object diagnostics)
    {
        SortedDictionary<string, long> counters =
            RenderPipelineInternalDiagnostics.CaptureLatestCounters(diagnostics, out bool succeeded);
        if (!succeeded)
            throw new InvalidOperationException("A feature visual evidence request failed.");
        return new FeatureVisualCapture(
            bitmap.Width,
            bitmap.Height,
            Rgba16fEvidenceWriter.Encode(bitmap),
            counters,
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "StructuralPlanCacheStatistics"),
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "ProgramCacheStatistics"),
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "TargetPoolStatistics"),
            RenderPipelineInternalDiagnostics.CaptureNumericProperties(renderer, "LastExecutionStatistics"));
    }

    private static FeatureSceneFixture CreateManualFixture(string id)
    {
        return id switch
        {
            "primary-cross-node" => BuildPrimaryChain(165, 0.62f, 72),
            "primary-control-all-identity" => BuildPrimaryChain(100, 1, 0),
            "primary-control-gamma-disabled" => BuildPrimaryChain(100, 0.62f, 72),
            "primary-control-opacity-disabled" => BuildPrimaryChain(165, 1, 72),
            "primary-control-invert-disabled" => BuildPrimaryChain(165, 0.62f, 0),
            "aa-thin-line-color-times-alpha" => BuildAaShaderScene(stroke: false, applyShader: true),
            "aa-thin-line-control" => BuildAaShaderScene(stroke: false, applyShader: false),
            "aa-thin-stroke-color-times-alpha" => BuildAaShaderScene(stroke: true, applyShader: true),
            "aa-thin-stroke-control" => BuildAaShaderScene(stroke: true, applyShader: false),
            "whole-source-coordinate-shader" => BuildWholeSourceScene(applyShader: true),
            "whole-source-coordinate-control" => BuildWholeSourceScene(applyShader: false),
            "geometry-stroke" => BuildStrokeEffectScene(applyEffect: true),
            "geometry-stroke-control" => BuildStrokeEffectScene(applyEffect: false),
            "opaque-custom-readback" => BuildColorGradingScene(applyEffect: true),
            "opaque-custom-readback-control" => BuildColorGradingScene(applyEffect: false),
            "mixed-spatial-color-lut" => BuildMixedChain(applyEffects: true),
            "mixed-spatial-color-lut-control" => BuildMixedChain(applyEffects: false),
            "split-expansion" => BuildSplitScene(applyEffect: true),
            "split-expansion-control" => BuildSplitScene(applyEffect: false),
            "external-materialized-source" => BuildExternalSourceScene(altered: false),
            "external-materialized-source-control" => BuildExternalSourceScene(altered: true),
            "root-a-clear-b" => BuildRootOrderScene(includeClear: true),
            "root-a-b-no-clear-control" => BuildRootOrderScene(includeClear: false),
            "snapshot-clear-draw-backdrop" => BuildBackdropScene(includeClear: true),
            "snapshot-draw-backdrop-no-clear-control" => BuildBackdropScene(includeClear: false),
            "scale-half" or "scale-one" or "scale-two" => BuildPrimaryChain(155, 0.68f, 70),
            "scale-half-control" or "scale-one-control" or "scale-two-control" => BuildPrimaryChain(100, 1, 0),
            "shifted-roi" => BuildPrimaryChain(165, 0.62f, 72),
            "shifted-roi-control" => BuildPrimaryChain(100, 1, 0),
            "automatic-device-selection" => BuildPrimaryChain(145, 0.78f, 64),
            "automatic-device-selection-control" => BuildPrimaryChain(100, 1, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown feature visual scene."),
        };
    }

    private static FeatureSceneFixture BuildPrimaryChain(float gammaPercent, float opacity, float invertPercent)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode current = CreateMaterializedPatternSource(altered: false);
        current = fixture.WrapShader(current, FeatureEvidenceShaders.Gamma(gammaPercent));
        var opacityNode = new OpacityRenderNode(opacity);
        opacityNode.AddChild(current);
        current = opacityNode;
        current = fixture.WrapShader(current, FeatureEvidenceShaders.Invert(invertPercent));
        fixture.Root = current;
        return fixture;
    }

    private static FeatureSceneFixture BuildAaShaderScene(bool stroke, bool applyShader)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode source = stroke
            ? new FeatureAnalyticCoverageNode(stroke: true)
            : new FeatureAnalyticLineNode();
        fixture.Root = applyShader
            ? fixture.WrapShader(source, FeatureEvidenceShaders.ColorTimesAlpha)
            : source;
        return fixture;
    }

    private static FeatureSceneFixture BuildWholeSourceScene(bool applyShader)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode source = CreateMaterializedPatternSource(altered: false);
        if (!applyShader)
        {
            fixture.Root = source;
            return fixture;
        }

        fixture.Root = fixture.WrapShader(source, FeatureEvidenceShaders.WholeSourceCoordinate);
        return fixture;
    }

    private static FeatureSceneFixture BuildStrokeEffectScene(bool applyEffect)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode source = new FeatureAnalyticRoundRectNode();
        if (!applyEffect)
        {
            fixture.Root = source;
            return fixture;
        }

        var pen = new Pen();
        pen.Thickness.CurrentValue = 9;
        pen.Brush.CurrentValue = Brushes.OrangeRed;
        var stroke = new StrokeEffect();
        stroke.Pen.CurrentValue = pen;
        fixture.Root = fixture.WrapEffect(source, stroke);
        return fixture;
    }

    private static FeatureSceneFixture BuildColorGradingScene(bool applyEffect)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode source = CreateMaterializedPatternSource(altered: false);
        if (!applyEffect)
        {
            fixture.Root = source;
            return fixture;
        }

        var grading = new ColorGrading();
        grading.Contrast.CurrentValue = 28;
        grading.Saturation.CurrentValue = 35;
        grading.Exposure.CurrentValue = 0.35f;
        fixture.Root = fixture.WrapEffect(source, grading);
        return fixture;
    }

    private static FeatureSceneFixture BuildMixedChain(bool applyEffects)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode current = CreateMaterializedPatternSource(altered: false);
        if (applyEffects)
        {
            var blur = new Blur();
            blur.Sigma.CurrentValue = new Size(3, 3);
            current = fixture.WrapEffect(current, blur);
            current = fixture.WrapShader(current, FeatureEvidenceShaders.Gamma(145));
            var shadow = new DropShadow();
            shadow.Position.CurrentValue = new Point(7, 5);
            shadow.Sigma.CurrentValue = new Size(3, 3);
            shadow.Color.CurrentValue = new Color(190, 5, 10, 20);
            current = fixture.WrapEffect(current, shadow);
            var lut = new LutEffect();
            lut.Source.CurrentValue = CreateInvertLutSource();
            lut.Strength.CurrentValue = 85;
            current = fixture.WrapEffect(current, lut);
        }
        fixture.Root = current;
        return fixture;
    }

    private static FeatureSceneFixture BuildSplitScene(bool applyEffect)
    {
        var fixture = new FeatureSceneFixture();
        RenderNode source = CreateMaterializedPatternSource(altered: false);
        if (!applyEffect)
        {
            fixture.Root = source;
            return fixture;
        }

        var split = new SplitEffect();
        split.HorizontalDivisions.CurrentValue = 3;
        split.VerticalDivisions.CurrentValue = 2;
        split.HorizontalSpacing.CurrentValue = 5;
        split.VerticalSpacing.CurrentValue = 7;
        fixture.Root = fixture.WrapEffect(source, split);
        return fixture;
    }

    private static FeatureSceneFixture BuildExternalSourceScene(bool altered)
        => new() { Root = CreateMaterializedPatternSource(altered) };

    private static FeatureSceneFixture BuildRootOrderScene(bool includeClear)
    {
        var root = new ContainerRenderNode();
        root.AddChild(new FeatureColoredRectNode(new Rect(12, 14, 94, 76), new SKColor(240, 35, 45, 170)));
        if (includeClear)
            root.AddChild(new ClearRenderNode(Colors.Transparent));
        root.AddChild(new FeatureColoredRectNode(new Rect(74, 22, 102, 68), new SKColor(30, 90, 245, 190)));
        return new FeatureSceneFixture { Root = root };
    }

    private static FeatureSceneFixture BuildBackdropScene(bool includeClear)
    {
        var root = new ContainerRenderNode();
        root.AddChild(new FeatureColoredRectNode(new Rect(22, 12, 128, 82), new SKColor(250, 70, 30, 130)));
        var snapshot = new SnapshotBackdropRenderNode();
        root.AddChild(snapshot);
        if (includeClear)
            root.AddChild(new ClearRenderNode(Colors.Transparent));
        root.AddChild(new DrawBackdropRenderNode(snapshot, s_domain));
        return new FeatureSceneFixture { Root = root };
    }

    private static RenderNode CreateMaterializedPatternSource(bool altered)
    {
        RenderTarget target = RenderTarget.Create(s_frame.Width, s_frame.Height)
            ?? throw new InvalidOperationException("Could not allocate the materialized evidence source.");
        SKCanvas canvas = target.Value.Canvas;
        canvas.Clear();
        DrawPattern(canvas, altered);
        target.Value.Flush(true, true);
        return new FeatureMaterializedSourceNode(target, s_domain, altered);
    }

    private static void DrawPattern(SKCanvas canvas, bool altered)
    {
        using var paint = new SKPaint { IsAntialias = true };
        paint.Color = altered ? new SKColor(20, 210, 135, 150) : new SKColor(230, 45, 70, 145);
        canvas.DrawRoundRect(new SKRect(14, 10, 128, 92), 12, 12, paint);
        paint.Color = altered ? new SKColor(245, 75, 20, 205) : new SKColor(35, 145, 240, 190);
        canvas.DrawCircle(130, 51, 38, paint);
        paint.Color = altered ? new SKColor(70, 40, 230, 180) : new SKColor(245, 205, 35, 175);
        using var path = new SKPath();
        path.MoveTo(25, 88);
        path.LineTo(94, 18);
        path.LineTo(172, 88);
        path.Close();
        canvas.DrawPath(path, paint);

        var random = new Random(Seed);
        for (int index = 0; index < 24; index++)
        {
            paint.Color = new SKColor(
                (byte)random.Next(30, 256),
                (byte)random.Next(30, 256),
                (byte)random.Next(30, 256),
                (byte)random.Next(70, 210));
            canvas.DrawCircle(
                random.Next(8, s_frame.Width - 8),
                random.Next(8, s_frame.Height - 8),
                random.Next(1, 5),
                paint);
        }
    }

    private static CubeSource CreateInvertLutSource()
    {
        const string cubeText =
            "TITLE \"004 target invert\"\n"
            + "LUT_3D_SIZE 2\n"
            + "DOMAIN_MIN 0 0 0\n"
            + "DOMAIN_MAX 1 1 1\n"
            + "1 1 1\n0 1 1\n1 0 1\n0 0 1\n1 1 0\n0 1 0\n1 0 0\n0 0 0\n";
        var source = new CubeSource();
        source.ReadFrom(new Uri(
            "data:text/plain;base64," + Convert.ToBase64String(Encoding.ASCII.GetBytes(cubeText))));
        return source;
    }

    private static Drawable.Resource BuildCompositionRect(bool left)
    {
        var shape = new RectShape();
        shape.AlignmentX.CurrentValue = AlignmentX.Center;
        shape.AlignmentY.CurrentValue = AlignmentY.Center;
        shape.Width.CurrentValue = left ? 116 : 94;
        shape.Height.CurrentValue = left ? 78 : 62;
        shape.Fill.CurrentValue = left
            ? new SolidColorBrush(new Color(175, 238, 55, 55))
            : new SolidColorBrush(new Color(185, 35, 100, 245));
        shape.TransformOrigin.CurrentValue = RelativePoint.Center;
        var transform = new TransformGroup();
        var translate = new TranslateTransform();
        translate.X.CurrentValue = left ? -26 : 38;
        translate.Y.CurrentValue = left ? -5 : 14;
        var rotate = new RotationTransform();
        rotate.Rotation.CurrentValue = left ? -11 : 17;
        transform.Children.Add(translate);
        transform.Children.Add(rotate);
        shape.Transform.CurrentValue = transform;
        return shape.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource BuildNestedDrawableBrushDelay()
    {
        var stripes = new RectShape();
        stripes.AlignmentX.CurrentValue = AlignmentX.Center;
        stripes.AlignmentY.CurrentValue = AlignmentY.Center;
        stripes.Width.CurrentValue = 100;
        stripes.Height.CurrentValue = 70;
        var gradient = new LinearGradientBrush();
        gradient.StartPoint.CurrentValue = new RelativePoint(0, 0, RelativeUnit.Absolute);
        gradient.EndPoint.CurrentValue = new RelativePoint(13, 8, RelativeUnit.Absolute);
        gradient.SpreadMethod.CurrentValue = GradientSpreadMethod.Repeat;
        gradient.GradientStops.Add(new GradientStop(Colors.OrangeRed, 0));
        gradient.GradientStops.Add(new GradientStop(Colors.OrangeRed, 0.48f));
        gradient.GradientStops.Add(new GradientStop(Colors.CornflowerBlue, 0.52f));
        gradient.GradientStops.Add(new GradientStop(Colors.CornflowerBlue, 1));
        stripes.Fill.CurrentValue = gradient;

        var mosaic = new MosaicEffect();
        mosaic.TileSize.CurrentValue = new Size(9, 7);
        var delay = new DelayAnimationEffect();
        delay.Delay.CurrentValue = 0;
        delay.Effect.CurrentValue = mosaic;
        stripes.FilterEffect.CurrentValue = delay;

        var drawableBrush = new DrawableBrush(stripes);
        drawableBrush.Stretch.CurrentValue = Stretch.Fill;
        drawableBrush.TileMode.CurrentValue = TileMode.Tile;
        drawableBrush.DestinationRect.CurrentValue =
            new RelativeRect(0, 0, 0.5f, 0.5f, RelativeUnit.Relative);

        var host = new EllipseShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = 154;
        host.Height.CurrentValue = 88;
        host.Fill.CurrentValue = drawableBrush;
        return host.ToResource(new CompositionContext(TimeSpan.FromMilliseconds(250)));
    }

    private static Drawable.Resource BuildNestedControl()
    {
        var host = new EllipseShape();
        host.AlignmentX.CurrentValue = AlignmentX.Center;
        host.AlignmentY.CurrentValue = AlignmentY.Center;
        host.Width.CurrentValue = 154;
        host.Height.CurrentValue = 88;
        host.Fill.CurrentValue = Brushes.CornflowerBlue;
        return host.ToResource(CompositionContext.Default);
    }

    private static Drawable.Resource BuildThreeDScene(bool includeFilterEffect)
    {
        var scene = new Scene3D();
        scene.RenderWidth.CurrentValue = s_frame.Width;
        scene.RenderHeight.CurrentValue = s_frame.Height;
        scene.BackgroundColor.CurrentValue = new Color(255, 16, 20, 28);
        scene.AmbientColor.CurrentValue = Colors.White;
        scene.AmbientIntensity.CurrentValue = 0.18f;
        var camera = new PerspectiveCamera();
        camera.Position.CurrentValue = new System.Numerics.Vector3(0, 0, 5);
        camera.Target.CurrentValue = System.Numerics.Vector3.Zero;
        camera.Up.CurrentValue = System.Numerics.Vector3.UnitY;
        camera.FieldOfView.CurrentValue = 45;
        camera.NearPlane.CurrentValue = 0.1f;
        camera.FarPlane.CurrentValue = 100;
        scene.Camera.CurrentValue = camera;
        var sphere = new Sphere3D();
        sphere.Position.CurrentValue = System.Numerics.Vector3.Zero;
        sphere.Radius.CurrentValue = 1.35f;
        sphere.Segments.CurrentValue = 24;
        sphere.Rings.CurrentValue = 16;
        var material = new PBRMaterial();
        material.Albedo.CurrentValue = new Color(255, 220, 82, 45);
        material.Metallic.CurrentValue = 0.32f;
        material.Roughness.CurrentValue = 0.38f;
        sphere.Material.CurrentValue = material;
        scene.Objects.Add(sphere);
        var light = new DirectionalLight3D();
        light.Direction.CurrentValue = System.Numerics.Vector3.Normalize(
            new System.Numerics.Vector3(-0.4f, -0.7f, -1));
        light.Color.CurrentValue = Colors.White;
        light.Intensity.CurrentValue = 1.25f;
        light.IsEnabled = true;
        scene.Lights.Add(light);
        if (includeFilterEffect)
        {
            scene.FilterEffect.CurrentValue = new Invert
            {
                Amount = { CurrentValue = 65 },
            };
        }

        return scene.ToResource(CompositionContext.Default);
    }

    private static FeatureMetadataCapture CaptureMetadataScene(string id, JsonObject baselineScene)
    {
        return id switch
        {
            "bounds-hit-test-query" => CaptureQueryScene(),
            "outside-roi" or "empty-roi" => CaptureEmptyRequestedRegion(baselineScene),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown metadata-only scene."),
        };
    }

    private static FeatureMetadataCapture CaptureQueryScene()
    {
        var pen = new Pen();
        pen.Brush.CurrentValue = Brushes.White;
        pen.Thickness.CurrentValue = 3;
        using var penResource = (Pen.Resource)pen.ToResource(CompositionContext.Default);
        using var node = new EllipseRenderNode(
            new Rect(27.5f, 14.25f, 91.5f, 67.75f),
            Brushes.Resource.CornflowerBlue,
            penResource);
        using var executionProbe = new FeatureExecutionProbeNode();
        executionProbe.AddChild(node);
        object diagnostics = RenderPipelineInternalDiagnostics.CreateState();
        var options = new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                TargetDomain = s_domain,
                OutputScale = 1,
                MaxWorkingScale = 2,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        };
        RenderPipelineInternalDiagnostics.Attach(options, diagnostics, RenderRequestPurpose.Bounds);
        using var renderer = new RenderNodeRenderer(executionProbe, options);
        RenderNodeMeasurement measurement = renderer.Measure();
        CaptureAndValidateMetadataOnlyQuery(diagnostics, renderer, executionProbe, "measure");
        int validatedRequestCount = 1;
        bool inside = renderer.HitTest(new Point(73, 48));
        CaptureAndValidateMetadataOnlyQuery(diagnostics, renderer, executionProbe, "inside hit test");
        validatedRequestCount++;
        bool outside = renderer.HitTest(new Point(2, 2));
        SortedDictionary<string, long> counters = CaptureAndValidateMetadataOnlyQuery(
            diagnostics,
            renderer,
            executionProbe,
            "outside hit test");
        validatedRequestCount++;
        if (!inside || outside)
            throw new InvalidOperationException("The feature query control points are not discriminating.");
        var query = new JsonObject
        {
            ["bounds"] = RectString(measurement.OutputBounds),
            ["insidePoint"] = "73,48",
            ["insideHit"] = inside,
            ["outsidePoint"] = "2,2",
            ["outsideHit"] = outside,
            ["deferredWorkExecuted"] = executionProbe.CallbackEntries != 0,
            ["validatedRequestCount"] = validatedRequestCount,
        };
        return new FeatureMetadataCapture(counters, query);
    }

    internal static FeatureMetadataCapture CaptureQuerySceneForTest()
        => CaptureQueryScene();

    private static SortedDictionary<string, long> CaptureAndValidateMetadataOnlyQuery(
        object diagnostics,
        RenderNodeRenderer renderer,
        FeatureExecutionProbeNode executionProbe,
        string operation)
    {
        SortedDictionary<string, long> counters =
            RenderPipelineInternalDiagnostics.CaptureLatestCounters(diagnostics, out bool succeeded);
        if (!succeeded)
            throw new InvalidOperationException($"The feature query {operation} request failed.");

        RenderPipelineCounter[] executionOrAllocationCounters =
        [
            RenderPipelineCounter.ExecutedGpuPasses,
            RenderPipelineCounter.ExecutedBackendTransitions,
            RenderPipelineCounter.IntermediateAcquires,
            RenderPipelineCounter.IntermediateCreates,
            RenderPipelineCounter.IntermediateDischarges,
            RenderPipelineCounter.FullFrameMaterializations,
            RenderPipelineCounter.RoiMaterializations,
            RenderPipelineCounter.Synchronizations,
            RenderPipelineCounter.ProgramCreations,
            RenderPipelineCounter.RenderCacheCaptures,
            RenderPipelineCounter.OpaqueExternalExecutions,
            RenderPipelineCounter.ExecutedOutcomes,
            RenderPipelineCounter.PreviewAllocationDrops,
        ];
        foreach (RenderPipelineCounter counter in executionOrAllocationCounters)
        {
            string name = counter.ToString();
            if (counters.TryGetValue(name, out long value) && value != 0)
            {
                throw new InvalidOperationException(
                    $"The metadata-only feature query {operation} requires {name}=0; observed "
                    + value.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }

        if (executionProbe.CallbackEntries != 0)
        {
            throw new InvalidOperationException(
                $"The metadata-only feature query {operation} executed "
                + executionProbe.CallbackEntries.ToString(CultureInfo.InvariantCulture)
                + " deferred callbacks.");
        }
        if (renderer.TargetPoolStatistics.Creates != 0
            || renderer.TargetPoolStatistics.LeasedTargets != 0)
        {
            throw new InvalidOperationException(
                $"The metadata-only feature query {operation} allocated or retained an intermediate target.");
        }

        return counters;
    }

    private static FeatureMetadataCapture CaptureEmptyRequestedRegion(JsonObject scene)
    {
        RequestedRegion region = ParseRequestedRegion(scene["requestedRegion"])
            ?? throw new InvalidDataException("The empty-region scene has no requested region.");
        using FeatureSceneFixture fixture = BuildPrimaryChain(165, 0.62f, 72);
        var executionProbe = new FeatureExecutionProbeNode();
        executionProbe.AddChild(fixture.Root);
        fixture.Root = executionProbe;
        object diagnostics = RenderPipelineInternalDiagnostics.CreateState();
        var options = new RenderNodeRendererOptions
        {
            DefaultRequest = new RenderNodeRenderRequest
            {
                Intent = RenderIntent.Preview,
                TargetDomain = s_domain,
                RequestedRegion = region.ToRect(),
                OutputScale = 1,
                MaxWorkingScale = 2,
                CacheOptions = Beutl.Graphics.Rendering.Cache.RenderCacheOptions.Disabled,
            },
        };
        RenderPipelineInternalDiagnostics.Attach(options, diagnostics, RenderRequestPurpose.Frame);
        using var renderer = new RenderNodeRenderer(fixture.Root, options);
        using RenderNodeRasterization rasterization = renderer.Rasterize();
        if (rasterization.Bitmap is not null)
            throw new InvalidOperationException("An empty requested-region scene produced a bitmap.");
        SortedDictionary<string, long> counters =
            RenderPipelineInternalDiagnostics.CaptureLatestCounters(diagnostics, out bool succeeded);
        if (!succeeded)
            throw new InvalidOperationException("The empty requested-region request failed.");
        string[] zeroExecutionCounters =
        [
            nameof(RenderPipelineCounter.ExecutedOutcomes),
            nameof(RenderPipelineCounter.ExecutedGpuPasses),
            nameof(RenderPipelineCounter.IntermediateAcquires),
            nameof(RenderPipelineCounter.IntermediateCreates),
            nameof(RenderPipelineCounter.OpaqueExternalExecutions),
        ];
        foreach (string counter in zeroExecutionCounters)
        {
            // An absent counter means the recorder never observed the event, which is
            // the same zero-execution evidence as an explicit zero.
            if (counters.TryGetValue(counter, out long value) && value != 0)
            {
                throw new InvalidOperationException(
                    $"Empty-region pruning requires {counter}=0; observed "
                    + value.ToString(CultureInfo.InvariantCulture) + ".");
            }
        }
        if (executionProbe.CallbackEntries != 0)
        {
            throw new InvalidOperationException(
                $"Empty-region pruning invoked {executionProbe.CallbackEntries} deferred callbacks.");
        }
        if (renderer.TargetPoolStatistics.Creates != 0
            || renderer.TargetPoolStatistics.LeasedTargets != 0)
        {
            throw new InvalidOperationException(
                "Empty-region pruning allocated or retained an intermediate render target.");
        }
        return new FeatureMetadataCapture(counters, null);
    }

    private static Bitmap? SelectRequestedRegion(
        Bitmap full,
        float outputScale,
        RequestedRegion? requestedRegion)
    {
        if (requestedRegion is null)
            return full.Clone();
        PixelRect selected = ResolveRequestedPixelRegion(
            full.Width,
            full.Height,
            outputScale,
            requestedRegion.Value);
        if (selected.Width <= 0 || selected.Height <= 0)
            return null;
        return full.ExtractSubset(selected);
    }

    private static PixelRect ResolveRequestedPixelRegion(
        int width,
        int height,
        float outputScale,
        RequestedRegion value)
    {
        int x = (int)MathF.Floor(value.X * outputScale);
        int y = (int)MathF.Floor(value.Y * outputScale);
        int right = (int)MathF.Ceiling((value.X + value.Width) * outputScale);
        int bottom = (int)MathF.Ceiling((value.Y + value.Height) * outputScale);
        int clippedX = Math.Clamp(x, 0, width);
        int clippedY = Math.Clamp(y, 0, height);
        int clippedRight = Math.Clamp(right, 0, width);
        int clippedBottom = Math.Clamp(bottom, 0, height);
        return new PixelRect(
            clippedX,
            clippedY,
            clippedRight - clippedX,
            clippedBottom - clippedY);
    }

    private static void AssertOutsideRequestedRegionUnchanged(
        ReadOnlySpan<byte> initialized,
        ReadOnlySpan<byte> completed,
        int width,
        int height,
        PixelRect requestedRegion)
    {
        if (initialized.Length != completed.Length || initialized.Length != checked(width * height * 8))
            throw new InvalidOperationException("Requested-region sentinel buffers have inconsistent dimensions.");

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (x >= requestedRegion.X && x < requestedRegion.Right
                    && y >= requestedRegion.Y && y < requestedRegion.Bottom)
                {
                    continue;
                }

                int offset = checked((y * width + x) * 8);
                if (!initialized.Slice(offset, 8).SequenceEqual(completed.Slice(offset, 8)))
                {
                    throw new InvalidOperationException(
                        $"Requested-region rendering modified sentinel pixel ({x}, {y}) outside {requestedRegion}.");
                }
            }
        }
    }

    private static JsonObject CaptureSameProcessFusionParity(JsonArray baselineScenes)
    {
        JsonObject primary = FindBaselineScene(baselineScenes, "primary-cross-node");
        JsonObject aaStroke = FindBaselineScene(baselineScenes, "aa-thin-stroke-color-times-alpha");
        PixelRect aaEdgeCrop = ParsePixelRectParameter(aaStroke, "edgeCrop");
        return new JsonObject
        {
            ["modePair"] = "internal-FusionMode.Disabled-vs-Enabled",
            ["thresholds"] = new JsonObject
            {
                ["minimumLinearLightSsim"] = 0.99,
                ["maximumLinearRgbMae"] = 0.02,
                ["maximumAlphaMae"] = 0.02,
                ["maximumAaCoverageBandMeanError"] = 0.02,
                ["maximumAaCoverageBandChannelError"] = 0.02,
            },
            ["primaryCrossNode"] = CaptureSameProcessCase(primary, aaEdgeCrop: null),
            ["aaThinStroke"] = CaptureSameProcessCase(aaStroke, aaEdgeCrop),
        };
    }

    private static JsonObject CaptureSameProcessCase(JsonObject scene, PixelRect? aaEdgeCrop)
    {
        string id = RequiredString(scene, "id");
        float outputScale = RequiredSingle(scene, "outputScale");
        float maxWorkingScale = float.Parse(
            RequiredString(scene, "maxWorkingScale"),
            NumberStyles.Float,
            CultureInfo.InvariantCulture);
        RequestedRegion? requestedRegion = ParseRequestedRegion(scene["requestedRegion"]);
        FeatureVisualCapture disabled = CaptureSameProcessMode(
            id,
            outputScale,
            maxWorkingScale,
            requestedRegion,
            "Disabled");
        FeatureVisualCapture enabled = CaptureSameProcessMode(
            id,
            outputScale,
            maxWorkingScale,
            requestedRegion,
            "Enabled");
        if (disabled.Width != enabled.Width || disabled.Height != enabled.Height)
            throw new InvalidOperationException($"Same-process scene '{id}' produced mismatched dimensions.");

        Rgba16fParityMetrics fullImage = Rgba16fEvidenceWriter.CalculateParity(
            disabled.Bytes,
            enabled.Bytes,
            disabled.Width,
            disabled.Height,
            region: null);
        ValidateSameProcessParity(id, "full image", fullImage);
        var result = new JsonObject
        {
            ["sceneId"] = id,
            ["fullImage"] = JsonSerializer.SerializeToNode(fullImage, s_jsonOptions),
            ["fusionDisabledCounters"] = JsonSerializer.SerializeToNode(
                disabled.RequestCounters,
                s_jsonOptions),
            ["fusionEnabledCounters"] = JsonSerializer.SerializeToNode(
                enabled.RequestCounters,
                s_jsonOptions),
        };

        if (id == "primary-cross-node")
        {
            long disabledPasses = disabled.RequestCounters.GetValueOrDefault("ExecutedGpuPasses");
            long enabledPasses = enabled.RequestCounters.GetValueOrDefault("ExecutedGpuPasses");
            if (enabledPasses != 1
                || disabledPasses <= enabledPasses
                || enabled.RequestCounters.GetValueOrDefault("FusedStages") < 3)
            {
                throw new InvalidOperationException(
                    "The same-process primary control did not distinguish disabled and enabled fusion schedules.");
            }
        }

        if (aaEdgeCrop is { } crop)
        {
            Rgba16fParityMetrics cropMetrics = Rgba16fEvidenceWriter.CalculateParity(
                disabled.Bytes,
                enabled.Bytes,
                disabled.Width,
                disabled.Height,
                crop);
            ValidateSameProcessParity(id, "AA edge crop", cropMetrics);
            Rgba16fCoverageBandMetrics coverage = Rgba16fEvidenceWriter.CalculateCoverageBand(
                disabled.Bytes,
                enabled.Bytes,
                disabled.Width,
                disabled.Height,
                crop);
            if (coverage.RgbaMae > 0.02 || coverage.MaximumError.Maximum > 0.02)
            {
                throw new InvalidOperationException(
                    $"Same-process AA coverage-band parity failed for '{id}'.");
            }
            result["aaEdge"] = new JsonObject
            {
                ["region"] = $"{crop.X},{crop.Y},{crop.Width},{crop.Height}",
                ["crop"] = JsonSerializer.SerializeToNode(cropMetrics, s_jsonOptions),
                ["coverageBand"] = JsonSerializer.SerializeToNode(coverage, s_jsonOptions),
                ["fixedMaximumChannelErrorBound"] = 0.02,
            };
        }

        return result;
    }

    private static FeatureVisualCapture CaptureSameProcessMode(
        string id,
        float outputScale,
        float maxWorkingScale,
        RequestedRegion? requestedRegion,
        string fusionMode)
    {
        using FeatureSceneFixture fixture = CreateManualFixture(id);
        return RenderExistingNode(
            fixture.Root,
            outputScale,
            maxWorkingScale,
            fixture.UseRenderCache,
            requestedRegion,
            fusionMode);
    }

    private static void ValidateSameProcessParity(
        string sceneId,
        string region,
        Rgba16fParityMetrics metrics)
    {
        if (metrics.LinearLightSsim < 0.99
            || metrics.LinearRgbMae > 0.02
            || metrics.AlphaMae > 0.02)
        {
            throw new InvalidOperationException(
                $"Same-process {region} parity failed for '{sceneId}': {metrics}.");
        }
    }

    private static JsonObject FindBaselineScene(JsonArray scenes, string id)
        => scenes
            .Select(static item => item?.AsObject())
            .SingleOrDefault(scene => scene is not null
                                      && string.Equals(RequiredString(scene, "id"), id, StringComparison.Ordinal))
           ?? throw new InvalidDataException($"The baseline scene '{id}' is missing.");

    private static PixelRect ParsePixelRectParameter(JsonObject scene, string name)
    {
        string text = scene["parameters"]?[name]?.GetValue<string>()
            ?? throw new InvalidDataException($"Scene '{RequiredString(scene, "id")}' lacks parameter '{name}'.");
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length != 4
            || parts.Any(static item => !int.TryParse(
                item,
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out _)))
        {
            throw new InvalidDataException($"Scene parameter '{name}' is not a four-integer pixel rectangle.");
        }
        return new PixelRect(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture),
            int.Parse(parts[3], CultureInfo.InvariantCulture));
    }

    private static void SetInternalFusionMode(RenderNodeRendererOptions options, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        PropertyInfo requestProperty = typeof(RenderNodeRendererOptions).GetProperty("DefaultRequest", flags)
            ?? throw new MissingMemberException(typeof(RenderNodeRendererOptions).FullName, "DefaultRequest");
        object request = requestProperty.GetValue(options)
            ?? throw new InvalidOperationException("The renderer options have no default request.");
        PropertyInfo property = request.GetType().GetProperty("FusionMode", flags)
            ?? throw new MissingMemberException(request.GetType().FullName, "FusionMode");
        object value = Enum.Parse(property.PropertyType, name, ignoreCase: false);
        property.SetValue(request, value);
    }

    private static void AddNonVacuityMetrics(
        JsonArray scenes,
        IReadOnlyDictionary<string, byte[]> artifacts)
    {
        Dictionary<string, JsonObject> byId = scenes
            .Select(static item => item!.AsObject())
            .ToDictionary(static scene => RequiredString(scene, "id"), StringComparer.Ordinal);
        foreach (JsonObject scene in byId.Values.Where(static item => RequiredString(item, "role") == "parity"))
        {
            string id = RequiredString(scene, "id");
            string controlId = RequiredString(scene, "controlSceneId");
            JsonObject control = byId[controlId];
            string blob = RequiredString(scene, "blob");
            string controlBlob = RequiredString(control, "blob");
            int width = RequiredInt32(scene, "blobWidth");
            int height = RequiredInt32(scene, "blobHeight");
            string mode = scene["nonVacuityMode"]?.GetValue<string>() ?? "full-frame";
            RequestedRegion? region = ParseRequestedRegion(scene["nonVacuityRegion"]);
            Rgba16fPixelDelta delta = Rgba16fEvidenceWriter.CalculateDelta(
                artifacts[blob],
                artifacts[controlBlob],
                width,
                height,
                mode,
                region is null
                    ? null
                    : new PixelRect(region.Value.X, region.Value.Y, region.Value.Width, region.Value.Height));
            double discriminator = Math.Max(delta.LinearRgbMae, delta.AlphaMae);
            double margin = discriminator - 0.02;
            if (margin <= 0.005)
                throw new InvalidOperationException($"Feature scene '{id}' is vacuous; margin={margin:F6}.");
            scene["nonVacuity"] = new JsonObject
            {
                ["linearRgbMae"] = delta.LinearRgbMae,
                ["alphaMae"] = delta.AlphaMae,
                ["maximumChannelError"] = delta.MaximumChannelError,
                ["sampleCount"] = delta.SampleCount,
                ["metricMode"] = mode,
                ["metricRegion"] = scene["nonVacuityRegion"]?.DeepClone(),
                ["parityTolerance"] = 0.02,
                ["marginAboveTolerance"] = margin,
            };
        }
    }

    private static JsonObject CopySemanticSceneFields(JsonObject baselineScene)
    {
        string[] names =
        [
            "id", "category", "role", "controlSceneId", "blob", "blobWidth", "blobHeight",
            "logicalWidth", "logicalHeight", "outputScale", "maxWorkingScale", "requestedRegion",
            "nonVacuityMode", "nonVacuityRegion", "empty", "parameters",
        ];
        var result = new JsonObject();
        foreach (string name in names)
        {
            if (!baselineScene.ContainsKey(name))
                throw new InvalidDataException($"Baseline scene '{RequiredString(baselineScene, "id")}' lacks '{name}'.");
            result[name] = baselineScene[name]?.DeepClone();
        }
        return result;
    }

    private static JsonObject LoadManifest(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("The paired target manifest is missing.", path);
        return JsonNode.Parse(File.ReadAllText(path, Encoding.UTF8))?.AsObject()
            ?? throw new InvalidDataException("The paired target manifest root is not an object.");
    }

    private static void WriteGeneration(string outputDirectory, FeatureVisualGeneration generation)
    {
        foreach ((string name, byte[] bytes) in generation.Artifacts)
            File.WriteAllBytes(Path.Combine(outputDirectory, name), bytes);
        File.WriteAllText(
            Path.Combine(outputDirectory, "manifest.json"),
            generation.Manifest.ToJsonString(s_jsonOptions) + "\n",
            new UTF8Encoding(false));
    }

    private static void PrepareCreateOnlyDirectory(string path)
    {
        string fullPath = Path.GetFullPath(path);
        if (Directory.Exists(fullPath) || File.Exists(fullPath))
            throw new IOException($"Create-only feature evidence directory already exists: {fullPath}");
        Directory.CreateDirectory(fullPath);
    }

    private static RequestedRegion? ParseRequestedRegion(JsonNode? node)
    {
        if (node is null)
            return null;
        JsonObject value = node.AsObject();
        return new RequestedRegion(
            RequiredInt32(value, "x"),
            RequiredInt32(value, "y"),
            RequiredInt32(value, "width"),
            RequiredInt32(value, "height"));
    }

    private static string RequiredString(JsonObject value, string name)
        => value[name]?.GetValue<string>()
           ?? throw new InvalidDataException($"Required string field '{name}' is missing.");

    private static int RequiredInt32(JsonObject value, string name)
        => value[name]?.GetValue<int>()
           ?? throw new InvalidDataException($"Required integer field '{name}' is missing.");

    private static float RequiredSingle(JsonObject value, string name)
        => value[name]?.GetValue<float>()
           ?? throw new InvalidDataException($"Required numeric field '{name}' is missing.");

    private static string RequireEnvironment(string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Required environment variable '{name}' is missing.")
            : value;
    }

    private static string CurrentCodeSha()
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(Environment.CurrentDirectory);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("HEAD");
        using System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git for feature provenance.");
        string stdout = process.StandardOutput.ReadToEnd();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"Could not resolve the feature code SHA: {stderr.Trim()}");
        return stdout.Trim();
    }

    private static string AssemblyVersion(Assembly assembly)
        => assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
           ?? assembly.GetName().Version?.ToString()
           ?? throw new InvalidOperationException($"Assembly '{assembly.FullName}' has no version.");

    private static string Sha256Hex(ReadOnlySpan<byte> bytes)
        => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string RectString(Rect value)
        => string.Join(",", new[] { value.X, value.Y, value.Width, value.Height }
            .Select(static item => item.ToString("0.######", CultureInfo.InvariantCulture)));

    private static bool DictionariesEqual(
        IReadOnlyDictionary<string, long> left,
        IReadOnlyDictionary<string, long> right)
        => left.Count == right.Count
           && left.All(pair => right.TryGetValue(pair.Key, out long value) && value == pair.Value);

    private static long Counter(IReadOnlyDictionary<string, long> counters, string name)
        => counters.TryGetValue(name, out long value) ? value : 0;

    private readonly record struct RequestedRegion(int X, int Y, int Width, int Height)
    {
        public Rect ToRect() => new(X, Y, Width, Height);
    }

    private sealed class FailSecondTargetFactory : IRenderTargetFactory
    {
        public int CreateCalls { get; private set; }

        public bool FailureConsumed { get; private set; }

        public PixelSize? FailedDeviceSize { get; private set; }

        public RenderTarget? Create(RenderTargetAllocationDescriptor allocation)
        {
            PixelSize deviceSize = allocation.DeviceSize;
            int index = CreateCalls++;
            if (index == 1)
            {
                FailureConsumed = true;
                FailedDeviceSize = deviceSize;
                return null;
            }

            return RenderTarget.Create(deviceSize.Width, deviceSize.Height)
                   ?? throw new InvalidOperationException(
                       $"Could not allocate the feature allocation-probe target {deviceSize.Width}x{deviceSize.Height}.");
        }
    }

    private sealed record FeatureAllocationFailureCapture(
        string Outcome,
        bool Threw,
        string? ExceptionType,
        string? ExceptionMessage,
        bool RequestSucceeded,
        Rect? OutputBounds,
        float? OutputScale,
        bool? OutputIsEmpty,
        int? OutputBitmapWidth,
        int? OutputBitmapHeight,
        int? OutputNonZeroComponents,
        int? OutputNonFiniteComponents,
        string? OutputSha256,
        bool FailureConsumed,
        int TargetFactoryCreateCalls,
        int? FailedAllocationWidth,
        int? FailedAllocationHeight,
        SortedDictionary<string, long> Counters);
}

internal sealed class FeatureSceneFixture : IDisposable
{
    private readonly List<IDisposable> _resources = [];

    public RenderNode Root { get; set; } = null!;

    public bool UseRenderCache { get; init; }

    public RenderNode WrapEffect(RenderNode input, FilterEffect effect)
    {
        FilterEffect.Resource resource = effect.ToResource(CompositionContext.Default);
        _resources.Add(resource);
        FilterEffectRenderNode node = resource.CreateRenderNode();
        node.AddChild(input);
        return node;
    }

    public RenderNode WrapShader(RenderNode input, ShaderDescription description)
    {
        var node = new FeatureEvidenceShaderNode(description);
        node.AddChild(input);
        return node;
    }

    public void Dispose()
    {
        Root?.Dispose();
        for (int index = _resources.Count - 1; index >= 0; index--)
            _resources[index].Dispose();
    }
}

internal sealed class FeatureEvidenceShaderNode(ShaderDescription description) : ContainerRenderNode
{
    public override void Process(RenderNodeContext context)
    {
        foreach (RenderFragmentHandle input in context.Inputs)
            context.Publish(context.Shader(input, description));
    }
}

internal sealed class FeatureExecutionProbeNode : ContainerRenderNode
{
    public int CallbackEntries { get; private set; }

    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.Create(
            session =>
            {
                CallbackEntries++;
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(session.Inputs.Single().Draw);
                session.Publish(output);
            },
            OpaqueRenderBoundsContract.Map(RenderBoundsContract.Identity),
            RenderHitTestContract.AnyInput,
            RenderValueCardinality.Single,
            RenderScaleContract.PreserveInputSupply);
        foreach (RenderFragmentHandle input in context.Inputs)
            context.Publish(context.OpaqueMap(input, description));
    }
}

internal static class FeatureEvidenceShaders
{
    public static ShaderDescription ColorTimesAlpha { get; } = ShaderDescription.CurrentPixel(
        "half4 apply(half4 color) { return color * color.a; }");

    public static ShaderDescription WholeSourceCoordinate { get; } = ShaderDescription.WholeSource(
        "uniform shader src; half4 main(float2 fc) { float dx = 5.0 * sin(fc.y / 9.0); "
        + "half4 c = src.eval(fc + float2(dx, 0.0)); return half4(c.b, c.r, c.g, c.a); }",
        RenderBoundsContract.Identity);

    public static ShaderDescription Gamma(float amountPercent)
    {
        float gamma = Math.Clamp(amountPercent / 100, 0.01f, 3);
        return ShaderDescription.CurrentPixel(
            "uniform float gamma; uniform float strength; "
            + "half4 apply(half4 color) { "
            + "float alpha = color.a; if (alpha <= 0.0001) return half4(0.0); "
            + "float3 rgb = color.rgb / alpha; "
            + "float3 corrected = pow(max(rgb, float3(0.0)), float3(1.0 / gamma)); "
            + "float3 result = mix(rgb, corrected, strength); "
            + "return half4(half3(result * alpha), half(alpha)); }",
            bindings =>
            {
                bindings.Uniform("gamma", gamma);
                bindings.Uniform("strength", 1f);
            });
    }

    public static ShaderDescription Invert(float amountPercent)
    {
        float amount = amountPercent / 100;
        return ShaderDescription.CurrentPixel(
            "uniform float amount; uniform int excludeAlpha; "
            + "half4 apply(half4 color) { "
            + "float alpha = color.a; if (alpha <= 0.0001) return half4(0.0); "
            + "float3 rgb = color.rgb / alpha; float3 inverted = 1.0 - rgb; "
            + "float3 result = mix(rgb, inverted, amount); "
            + "if (excludeAlpha == 0) { float newAlpha = mix(alpha, 1.0 - alpha, amount); "
            + "return half4(half3(result * newAlpha), half(newAlpha)); } "
            + "return half4(half3(result * alpha), half(alpha)); }",
            bindings =>
            {
                bindings.Uniform("amount", amount);
                bindings.Uniform("excludeAlpha", 1);
            });
    }
}

internal sealed class FeatureMaterializedSourceNode(
    RenderTarget target,
    Rect bounds,
    bool altered) : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(
            target,
            altered ? "feature-evidence-pattern-altered" : "feature-evidence-pattern",
            version: 1);
        context.Publish(context.MaterializedInput(MaterializedInputDescription.FromRenderTarget(
            resource,
            bounds,
            EffectiveScale.At(1),
            PixelRect.FromRect(bounds, 1),
            default,
            RenderHitTestContract.OutputBounds)));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
            target.Dispose();
        base.OnDispose(disposing);
    }
}

internal sealed class FeatureAnalyticCoverageNode : RenderNode
{
    private readonly Geometry _geometry;
    private readonly Geometry.Resource _geometryResource;
    private readonly Brush.Resource? _fillResource;
    private readonly Pen.Resource? _penResource;
    private readonly GeometryRenderNode _source;

    public FeatureAnalyticCoverageNode(bool stroke, bool filled = false)
    {
        _geometry = filled
            ? PathGeometry.Parse(
                "M43.25,17.5 H147.75 A17,17 0 0 1 164.75,34.5 "
                + "V74.25 A17,17 0 0 1 147.75,91.25 H43.25 "
                + "A17,17 0 0 1 26.25,74.25 V34.5 A17,17 0 0 1 43.25,17.5 Z")
            : stroke
                ? PathGeometry.Parse("M18.25,83.6 C51.75,12.4 119.5,96.2 174.4,22.75")
                : PathGeometry.Parse("M17.35,86.45 L174.65,19.55");
        _geometryResource = _geometry.ToResource(CompositionContext.Default);
        if (filled)
        {
            var brush = new SolidColorBrush(new Color(205, 225, 85, 30));
            _fillResource = brush.ToResource(CompositionContext.Default);
        }
        else
        {
            var pen = new Pen
            {
                Thickness = { CurrentValue = stroke ? 1.25f : 0.75f },
                Brush = { CurrentValue = new SolidColorBrush(new Color(205, 225, 85, 30)) },
                StrokeCap = { CurrentValue = StrokeCap.Round },
                StrokeJoin = { CurrentValue = StrokeJoin.Round },
            };
            _penResource = pen.ToResource(CompositionContext.Default);
        }
        _source = new GeometryRenderNode(_geometryResource, _fillResource, _penResource);
    }

    public override void Process(RenderNodeContext context)
    {
        context.PublishRange(context.RecordNode(_source, []));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
        {
            _source.Dispose();
            _penResource?.Dispose();
            _fillResource?.Dispose();
            _geometryResource.Dispose();
        }
        base.OnDispose(disposing);
    }
}

internal sealed class FeatureAnalyticLineNode : RenderNode
{
    private static readonly Rect s_bounds = new(10, 8, 172, 92);

    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            execute: static session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => Draw(canvas.Canvas));
                session.Publish(output);
            },
            directReplay: static session => Draw(session.Canvas.Canvas),
            bounds: OpaqueRenderBoundsContract.Source(s_bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            scale: RenderScaleContract.Vector,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            structuralKey: typeof(FeatureAnalyticLineNode),
            runtimeIdentity: new RenderRuntimeIdentity(typeof(FeatureAnalyticLineNode)));
        context.Publish(context.OpaqueSource(description));
    }

    private static void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(225, 85, 30, 205),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 0.75f,
            StrokeCap = SKStrokeCap.Round,
            StrokeJoin = SKStrokeJoin.Round,
        };
        canvas.DrawLine(17.35f, 86.45f, 174.65f, 19.55f, paint);
    }
}

internal sealed class FeatureAnalyticRoundRectNode : RenderNode
{
    private static readonly Rect s_bounds = new(10, 8, 172, 92);

    public override void Process(RenderNodeContext context)
    {
        OpaqueRenderDescription description = OpaqueRenderDescription.CreateEngineSource(
            execute: static session =>
            {
                using OpaqueRenderOutput output = session.CreateOutput(session.OutputBounds);
                output.Canvas.Use(static canvas => Draw(canvas.Canvas));
                session.Publish(output);
            },
            directReplay: static session => Draw(session.Canvas.Canvas),
            bounds: OpaqueRenderBoundsContract.Source(s_bounds),
            hitTest: RenderHitTestContract.OutputBounds,
            scale: RenderScaleContract.Vector,
            deviceGridSensitivity: RenderDeviceGridSensitivity.Insensitive,
            structuralKey: typeof(FeatureAnalyticRoundRectNode),
            runtimeIdentity: new RenderRuntimeIdentity(typeof(FeatureAnalyticRoundRectNode)));
        context.Publish(context.OpaqueSource(description));
    }

    private static void Draw(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(225, 85, 30, 205),
            Style = SKPaintStyle.Fill,
        };
        canvas.DrawRoundRect(new SKRect(26.25f, 17.5f, 164.75f, 91.25f), 17, 17, paint);
    }
}

internal sealed class FeatureColoredRectNode : RenderNode
{
    private readonly RenderTarget _target;

    public FeatureColoredRectNode(Rect bounds, SKColor color)
    {
        _target = RenderTarget.Create(192, 108)
            ?? throw new InvalidOperationException("Could not allocate a target-order evidence source.");
        SKCanvas canvas = _target.Value.Canvas;
        canvas.Clear();
        using var paint = new SKPaint { IsAntialias = true, Color = color };
        canvas.DrawRoundRect(bounds.ToSKRect(), 9, 9, paint);
        _target.Value.Flush(true, true);
    }

    public override void Process(RenderNodeContext context)
    {
        RenderResource<RenderTarget> resource = context.Borrow(_target, GetType(), version: 1);
        context.Publish(context.MaterializedInput(MaterializedInputDescription.FromRenderTarget(
            resource,
            new Rect(0, 0, 192, 108),
            EffectiveScale.At(1),
            new PixelRect(0, 0, 192, 108),
            default,
            RenderHitTestContract.OutputBounds)));
    }

    protected override void OnDispose(bool disposing)
    {
        if (disposing)
            _target.Dispose();
        base.OnDispose(disposing);
    }
}

internal sealed class FeatureEmptySourceNode : RenderNode
{
    public override void Process(RenderNodeContext context)
    {
    }
}

internal static class Rgba16fEvidenceWriter
{
    public static byte[] Encode(Bitmap bitmap)
    {
        if (bitmap.ColorType != BitmapColorType.RgbaF16
            || bitmap.AlphaType != BitmapAlphaType.Premul
            || !bitmap.ColorSpace.Equals(BitmapColorSpace.LinearSrgb))
        {
            throw new InvalidOperationException("Feature bitmap must be RgbaF16/Premul/LinearSrgb.");
        }

        byte[] bytes = new byte[checked(bitmap.Width * bitmap.Height * 8)];
        int destination = 0;
        for (int y = 0; y < bitmap.Height; y++)
        {
            ReadOnlySpan<ushort> row = bitmap.GetRow<ushort>(y);
            if (row.Length != bitmap.Width * 4)
                throw new InvalidOperationException("Unexpected RGBA16F row length.");
            foreach (ushort component in row)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(destination, 2), component);
                destination += 2;
            }
        }
        return bytes;
    }

    public static Rgba16fParityMetrics CalculateParity(
        ReadOnlySpan<byte> reference,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        PixelRect? region)
    {
        PixelRect selected = ValidateInputs(reference, actual, width, height, region);
        double referenceLumaSum = 0;
        double actualLumaSum = 0;
        double rgbError = 0;
        double alphaError = 0;
        int pixelCount = checked(selected.Width * selected.Height);
        for (int y = selected.Y; y < selected.Bottom; y++)
        {
            for (int x = selected.X; x < selected.Right; x++)
            {
                int offset = checked((y * width + x) * 8);
                float referenceRed = ReadFiniteHalf(reference, offset);
                float referenceGreen = ReadFiniteHalf(reference, offset + 2);
                float referenceBlue = ReadFiniteHalf(reference, offset + 4);
                float actualRed = ReadFiniteHalf(actual, offset);
                float actualGreen = ReadFiniteHalf(actual, offset + 2);
                float actualBlue = ReadFiniteHalf(actual, offset + 4);
                rgbError += Math.Abs(referenceRed - actualRed)
                            + Math.Abs(referenceGreen - actualGreen)
                            + Math.Abs(referenceBlue - actualBlue);
                alphaError += Math.Abs(
                    ReadFiniteHalf(reference, offset + 6) - ReadFiniteHalf(actual, offset + 6));
                referenceLumaSum += Luma(referenceRed, referenceGreen, referenceBlue);
                actualLumaSum += Luma(actualRed, actualGreen, actualBlue);
            }
        }

        double referenceMean = referenceLumaSum / pixelCount;
        double actualMean = actualLumaSum / pixelCount;
        double referenceVariance = 0;
        double actualVariance = 0;
        double covariance = 0;
        for (int y = selected.Y; y < selected.Bottom; y++)
        {
            for (int x = selected.X; x < selected.Right; x++)
            {
                int offset = checked((y * width + x) * 8);
                double referenceDelta = Luma(
                    ReadFiniteHalf(reference, offset),
                    ReadFiniteHalf(reference, offset + 2),
                    ReadFiniteHalf(reference, offset + 4)) - referenceMean;
                double actualDelta = Luma(
                    ReadFiniteHalf(actual, offset),
                    ReadFiniteHalf(actual, offset + 2),
                    ReadFiniteHalf(actual, offset + 4)) - actualMean;
                referenceVariance += referenceDelta * referenceDelta;
                actualVariance += actualDelta * actualDelta;
                covariance += referenceDelta * actualDelta;
            }
        }

        referenceVariance /= pixelCount;
        actualVariance /= pixelCount;
        covariance /= pixelCount;
        const double c1 = 0.01 * 0.01;
        const double c2 = 0.03 * 0.03;
        double ssim = ((2 * referenceMean * actualMean + c1) * (2 * covariance + c2))
                      / ((referenceMean * referenceMean + actualMean * actualMean + c1)
                         * (referenceVariance + actualVariance + c2));
        return new Rgba16fParityMetrics(
            ssim,
            rgbError / (pixelCount * 3d),
            alphaError / pixelCount,
            pixelCount);
    }

    public static Rgba16fCoverageBandMetrics CalculateCoverageBand(
        ReadOnlySpan<byte> reference,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        PixelRect region)
    {
        PixelRect selected = ValidateInputs(reference, actual, width, height, region);
        double sum = 0;
        double red = 0;
        double green = 0;
        double blue = 0;
        double alpha = 0;
        int pixelCount = 0;
        for (int y = selected.Y; y < selected.Bottom; y++)
        {
            for (int x = selected.X; x < selected.Right; x++)
            {
                int offset = checked((y * width + x) * 8);
                float referenceAlpha = ReadFiniteHalf(reference, offset + 6);
                if (referenceAlpha is <= 0 or >= 1)
                    continue;

                double redError = Math.Abs(
                    ReadFiniteHalf(reference, offset) - ReadFiniteHalf(actual, offset));
                double greenError = Math.Abs(
                    ReadFiniteHalf(reference, offset + 2) - ReadFiniteHalf(actual, offset + 2));
                double blueError = Math.Abs(
                    ReadFiniteHalf(reference, offset + 4) - ReadFiniteHalf(actual, offset + 4));
                double alphaError = Math.Abs(
                    referenceAlpha - ReadFiniteHalf(actual, offset + 6));
                red = Math.Max(red, redError);
                green = Math.Max(green, greenError);
                blue = Math.Max(blue, blueError);
                alpha = Math.Max(alpha, alphaError);
                sum += redError + greenError + blueError + alphaError;
                pixelCount++;
            }
        }

        if (pixelCount == 0)
            throw new InvalidOperationException("The same-process AA crop contains no nontrivial coverage pixels.");
        return new Rgba16fCoverageBandMetrics(
            sum / (pixelCount * 4d),
            new Rgba16fMaximumError(red, green, blue, alpha),
            pixelCount);
    }

    public static Rgba16fPixelDelta CalculateDelta(
        ReadOnlySpan<byte> left,
        ReadOnlySpan<byte> right,
        int width,
        int height,
        string mode,
        PixelRect? region)
    {
        int minX = region?.X ?? 0;
        int minY = region?.Y ?? 0;
        int regionWidth = region?.Width ?? width;
        int regionHeight = region?.Height ?? height;
        if (left.Length != right.Length || left.Length != checked(width * height * 8))
            throw new ArgumentException("RGBA16F buffers must have identical row-packed lengths.");

        double rgb = 0;
        double alpha = 0;
        double maximum = 0;
        int pixels = 0;
        for (int y = minY; y < minY + regionHeight; y++)
        {
            for (int x = minX; x < minX + regionWidth; x++)
            {
                int offset = checked((y * width + x) * 8);
                float leftAlpha = ReadHalf(left, offset + 6);
                float rightAlpha = ReadHalf(right, offset + 6);
                if (mode == "alpha-edge-band"
                    && !IsCoverageEdge(leftAlpha)
                    && !IsCoverageEdge(rightAlpha))
                {
                    continue;
                }
                pixels++;
                for (int channel = 0; channel < 4; channel++)
                {
                    double difference = Math.Abs(ReadHalf(left, offset + channel * 2)
                                                 - ReadHalf(right, offset + channel * 2));
                    maximum = Math.Max(maximum, difference);
                    if (channel == 3)
                        alpha += difference;
                    else
                        rgb += difference;
                }
            }
        }

        if (pixels == 0)
            throw new InvalidOperationException($"Non-vacuity mode '{mode}' selected no pixels.");
        return new Rgba16fPixelDelta(rgb / (pixels * 3d), alpha / pixels, maximum, pixels);

        static float ReadHalf(ReadOnlySpan<byte> value, int offset)
            => (float)BitConverter.UInt16BitsToHalf(
                BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2)));

        static bool IsCoverageEdge(float alpha) => alpha is > 0.01f and < 0.75f;
    }

    private static PixelRect ValidateInputs(
        ReadOnlySpan<byte> reference,
        ReadOnlySpan<byte> actual,
        int width,
        int height,
        PixelRect? region)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "RGBA16F dimensions must be positive.");
        int expectedLength = checked(width * height * 8);
        if (reference.Length != expectedLength || actual.Length != expectedLength)
            throw new ArgumentException("RGBA16F buffers must match the declared dimensions.");
        PixelRect selected = region ?? new PixelRect(0, 0, width, height);
        if (selected.Width <= 0
            || selected.Height <= 0
            || selected.X < 0
            || selected.Y < 0
            || selected.Right > width
            || selected.Bottom > height)
        {
            throw new ArgumentOutOfRangeException(nameof(region), region, "Metric region is outside the RGBA16F image.");
        }
        return selected;
    }

    private static float ReadFiniteHalf(ReadOnlySpan<byte> value, int offset)
    {
        float result = (float)BitConverter.UInt16BitsToHalf(
            BinaryPrimitives.ReadUInt16LittleEndian(value.Slice(offset, 2)));
        return float.IsFinite(result)
            ? result
            : throw new InvalidDataException("RGBA16F metric input contains a non-finite component.");
    }

    private static double Luma(float red, float green, float blue)
        => 0.2126 * red + 0.7152 * green + 0.0722 * blue;
}

internal sealed record FeatureVisualGeneration(
    JsonObject Manifest,
    SortedDictionary<string, byte[]> Artifacts);

internal sealed record FeatureVisualCapture(
    int Width,
    int Height,
    byte[] Bytes,
    SortedDictionary<string, long> RequestCounters,
    SortedDictionary<string, long> StructuralPlanCacheStatistics,
    SortedDictionary<string, long> ProgramCacheStatistics,
    SortedDictionary<string, long> TargetPoolStatistics,
    SortedDictionary<string, long> LastExecutionStatistics);

internal sealed record FeatureMetadataCapture(
    SortedDictionary<string, long> RequestCounters,
    JsonObject? Query);

internal sealed record Rgba16fPixelDelta(
    double LinearRgbMae,
    double AlphaMae,
    double MaximumChannelError,
    int SampleCount);

internal sealed record Rgba16fParityMetrics(
    double LinearLightSsim,
    double LinearRgbMae,
    double AlphaMae,
    int SampleCount);

internal sealed record Rgba16fMaximumError(
    double Red,
    double Green,
    double Blue,
    double Alpha)
{
    public double Maximum => Math.Max(Math.Max(Red, Green), Math.Max(Blue, Alpha));
}

internal sealed record Rgba16fCoverageBandMetrics(
    double RgbaMae,
    Rgba16fMaximumError MaximumError,
    int PixelCount);
