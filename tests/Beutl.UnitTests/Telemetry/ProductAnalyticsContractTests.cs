using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Beutl.Api.Clients;
using Beutl.Api.Services;
using Beutl.Configuration;
using Beutl.Services;
using Microsoft.Extensions.Time.Testing;
using NuGet.Packaging.Core;
using NuGet.Versioning;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using TelemetryService = Beutl.Services.Telemetry;

namespace Beutl.UnitTests.Telemetry;

[TestFixture]
public sealed class ProductAnalyticsContractTests
{
    private const string ValidSha256 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [Test]
    public void IdentityStore_PersistsThenRotatesOnlyWhenReset()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-telemetry-{Guid.NewGuid():N}");
        try
        {
            var store = new TelemetryIdentityStore(root);

            TelemetryIdentity first = store.GetOrCreate();
            TelemetryIdentity second = store.GetOrCreate();

            Assert.Multiple(() =>
            {
                Assert.That(Guid.TryParseExact(first.InstallationId, "N", out _), Is.True);
                Assert.That(first.FirstSeenMonth, Does.Match("^\\d{4}-\\d{2}$"));
                Assert.That(second, Is.EqualTo(first));
                Assert.That(File.Exists(store.Path), Is.True);
            });

            store.Reset();

            Assert.That(store.TryRead(), Is.Null);
            Assert.That(store.GetOrCreate().InstallationId, Is.Not.EqualTo(first.InstallationId));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void IdentityStore_IgnoresMalformedPersistedIdentity()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-telemetry-{Guid.NewGuid():N}");
        try
        {
            var store = new TelemetryIdentityStore(root);
            Directory.CreateDirectory(Path.GetDirectoryName(store.Path)!);
            File.WriteAllText(store.Path, "{\"installationId\":\"not-a-guid\",\"firstSeenMonth\":\"2026-99\"}");

            Assert.That(store.TryRead(), Is.Null);
            Assert.That(Guid.TryParseExact(store.GetOrCreate().InstallationId, "N", out _), Is.True);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task IdentityStore_SerializesDesktopAndPackageToolsFirstCreation()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-telemetry-race-{Guid.NewGuid():N}");
        try
        {
            TelemetryIdentity[] identities = await Task.WhenAll(
                Enumerable.Range(0, 16)
                    .Select(_ => Task.Run(() => new TelemetryIdentityStore(root).GetOrCreate())));

            Assert.Multiple(() =>
            {
                Assert.That(identities.Select(x => x.InstallationId).Distinct().Count(), Is.EqualTo(1));
                Assert.That(identities.Select(x => x.FirstSeenMonth).Distinct().Count(), Is.EqualTo(1));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task IdentityStore_WaitsForCrossProcessLockAndResetReadNeverReturnsMalformedData()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-telemetry-lock-{Guid.NewGuid():N}");
        try
        {
            var store = new TelemetryIdentityStore(root);
            TelemetryIdentity initial = store.GetOrCreate();
            using (var externalLock = new FileStream(
                       store.LockPath,
                       FileMode.OpenOrCreate,
                       FileAccess.ReadWrite,
                       FileShare.None))
            {
                Task<TelemetryIdentity?> blockedRead = Task.Run(store.TryRead);
                await Task.Delay(100);
                Assert.That(blockedRead.IsCompleted, Is.False);
                externalLock.Dispose();
                Assert.That(await blockedRead, Is.EqualTo(initial));
            }

            Task reset = Task.Run(() =>
            {
                for (int i = 0; i < 25; i++) store.Reset();
            });
            Task readCreate = Task.Run(() =>
            {
                for (int i = 0; i < 25; i++)
                {
                    TelemetryIdentity identity = new TelemetryIdentityStore(root).GetOrCreate();
                    Assert.That(Guid.TryParseExact(identity.InstallationId, "N", out _), Is.True);
                }
            });
            await Task.WhenAll(reset, readCreate);

            Assert.That(Guid.TryParseExact(store.GetOrCreate().InstallationId, "N", out _), Is.True);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public async Task IdentityStore_CoordinatesActualDesktopAndPackageToolsProcesses()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-telemetry-process-race-{Guid.NewGuid():N}");
        try
        {
            string[] firstCreation = await Task.WhenAll(
                RunIdentityTestHostAsync("get", root),
                RunIdentityTestHostAsync("get", root));

            Assert.Multiple(() =>
            {
                Assert.That(firstCreation.All(value => Guid.TryParseExact(value, "N", out _)), Is.True);
                Assert.That(firstCreation.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(1));
            });

            Task<string> reset = RunIdentityTestHostAsync("reset", root);
            Task<string> concurrentRead = RunIdentityTestHostAsync("read", root);
            await Task.WhenAll(reset, concurrentRead);

            Assert.Multiple(() =>
            {
                Assert.That(reset.Result, Is.EqualTo("reset"));
                Assert.That(
                    concurrentRead.Result == "null"
                    || Guid.TryParseExact(concurrentRead.Result, "N", out _),
                    Is.True);
            });

            string finalIdentity = await RunIdentityTestHostAsync("get", root);
            Assert.That(Guid.TryParseExact(finalIdentity, "N", out _), Is.True);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void Manifest_ParsesOnlyTheApprovedV1Shape()
    {
        byte[] json = Encoding.UTF8.GetBytes(
            """
            {"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.BlurExtension"}]}]}
            """);

        bool parsed = AnalyticsFeatureManifest.TryParse(json, ValidSha256, out AnalyticsFeatureManifest? manifest);

        Assert.Multiple(() =>
        {
            Assert.That(parsed, Is.True);
            Assert.That(manifest, Is.Not.Null);
            Assert.That(manifest!.Find("Acme.Plugin", "Acme.Plugin.BlurExtension"), Is.Not.Null);
            Assert.That(manifest.Find("Acme.Plugin", "Acme.Plugin.Other"), Is.Null);
        });
    }

    [TestCase("""{"schemaVersion":1,"unexpected":true,"features":[]}""")]
    [TestCase("""{"schemaVersion":1,"schemaVersion":1,"features":[]}""")]
    [TestCase("""{"schemaVersion":2,"features":[]}""")]
    [TestCase("""{"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.Blur"}]},{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.Other"}]}]}""")]
    [TestCase("""{"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.Blur"}]},{"kind":"other","key":"blur-two","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.Blur"}]}]}""")]
    [TestCase("""{"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"../Acme.Plugin","type":"Acme.Plugin.Blur"}]}]}""")]
    public void Manifest_RejectsUnknownAndAmbiguousDeclarations(string json)
    {
        Assert.That(
            AnalyticsFeatureManifest.TryParse(Encoding.UTF8.GetBytes(json), ValidSha256, out _),
            Is.False);
    }

    [Test]
    public void Manifest_RejectsPayloadLargerThanTheContractLimit()
    {
        byte[] bytes = new byte[AnalyticsFeatureManifest.MaxBytes + 1];

        Assert.That(AnalyticsFeatureManifest.TryParse(bytes, ValidSha256, out _), Is.False);
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(128, true)]
    [TestCase(129, false)]
    public void Manifest_RequiresOneThrough128Features(int featureCount, bool expected)
    {
        string features = string.Join(
            ',',
            Enumerable.Range(0, featureCount).Select(index =>
                $$"""{"kind":"effect","key":"feature-{{index}}","types":[{"assembly":"Acme.Plugin{{index}}","type":"Acme.Plugin.Type{{index}}"}]}"""));
        byte[] json = Encoding.UTF8.GetBytes($$"""{"schemaVersion":1,"features":[{{features}}]}""");

        Assert.That(
            AnalyticsFeatureManifest.TryParse(json, ValidSha256, out _),
            Is.EqualTo(expected));
    }

    [TestCase("assembly")]
    [TestCase("type")]
    public void Manifest_RequiresAssemblyAndTypeLengthsFromOneThrough256(string property)
    {
        string valid = new('a', 256);
        string tooLong = new('a', 257);
        string validJson = $$"""
            {"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"{{(property == "assembly" ? valid : "Acme.Plugin")}}","type":"{{(property == "type" ? valid : "Acme.Plugin.Blur")}}"}]}]}
            """;
        string invalidJson = $$"""
            {"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"{{(property == "assembly" ? tooLong : "Acme.Plugin")}}","type":"{{(property == "type" ? tooLong : "Acme.Plugin.Blur")}}"}]}]}
            """;

        Assert.Multiple(() =>
        {
            Assert.That(AnalyticsFeatureManifest.TryParse(
                Encoding.UTF8.GetBytes(validJson), ValidSha256, out _), Is.True);
            Assert.That(AnalyticsFeatureManifest.TryParse(
                Encoding.UTF8.GetBytes(invalidJson), ValidSha256, out _), Is.False);
        });
    }

    [Test]
    public void Manifest_LoadFromInstalledDirectory_RequiresTheApprovedHash()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-manifest-{Guid.NewGuid():N}");
        try
        {
            string directory = Path.Combine(root, "beutl");
            Directory.CreateDirectory(directory);
            byte[] bytes = Encoding.UTF8.GetBytes(
                """{"schemaVersion":1,"features":[{"kind":"effect","key":"blur","types":[{"assembly":"Acme.Plugin","type":"Acme.Plugin.BlurExtension"}]}]}""");
            File.WriteAllBytes(Path.Combine(directory, "analytics-features.v1.json"), bytes);
            string hash = Convert.ToHexString(SHA256.HashData(bytes));

            Assert.Multiple(() =>
            {
                Assert.That(AnalyticsFeatureManifest.TryLoadFromInstalledDirectory(root, hash), Is.Not.Null);
                Assert.That(AnalyticsFeatureManifest.TryLoadFromInstalledDirectory(root, ValidSha256), Is.Null);
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Test]
    public void TrustedFeatureRegistry_UsesGenericUntilVerifiedMappingIsRegistered()
    {
        TelemetryService.UnregisterTrustedFeature(typeof(TrustedFeatureType));
        try
        {
            Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureType)), Is.EqualTo("generic"));

            TelemetryService.RegisterTrustedFeature(
                typeof(TrustedFeatureType),
                "extension/acme.plugin/effect/blur");

            Assert.That(
                TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureType)),
                Is.EqualTo("extension/acme.plugin/effect/blur"));
        }
        finally
        {
            TelemetryService.UnregisterTrustedFeature(typeof(TrustedFeatureType));
        }

        Assert.That(TelemetryService.GetTrustedFeatureId(typeof(TrustedFeatureType)), Is.EqualTo("generic"));
    }

    [Test]
    public void Telemetry_DoesNotExposeAPluginRuntimeProductEventApi()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                typeof(TelemetryService).GetMethod("RecordProductEvent", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
            Assert.That(
                typeof(TelemetryService).GetMethod("StartProductOperation", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
            Assert.That(
                typeof(TelemetryService).GetMethod("StartActivity", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
            Assert.That(
                typeof(TelemetryService).GetMethod("ResetUsageIdentity", BindingFlags.Public | BindingFlags.Static),
                Is.Null);
            Assert.That(typeof(TelemetryIdentityStore).IsPublic, Is.False);
            Assert.That(typeof(TelemetryService).IsPublic, Is.False);
            Assert.That(typeof(TelemetryHostKind).IsPublic, Is.False);
            Assert.That(typeof(TelemetryService).Assembly.GetExportedTypes(), Is.Empty);
            Assert.That(
                typeof(PackageManager).Assembly.GetReferencedAssemblies()
                    .Select(static assembly => assembly.Name),
                Does.Not.Contain("Beutl.Telemetry"));
            Assert.That(typeof(TelemetryConfig).GetProperty(nameof(TelemetryConfig.UsageAnalytics)), Is.Not.Null);
            Assert.That(
                typeof(TelemetryConfig).GetProperty(
                    "UsageAnalyticsMigratedFromLegacy",
                    BindingFlags.Public | BindingFlags.Instance),
                Is.Null);
            Assert.That(
                typeof(TelemetryConfig).GetMethod(
                    "MigrateUsageAnalyticsFromLegacy",
                    BindingFlags.Public | BindingFlags.Instance),
                Is.Null);
            Assert.That(
                typeof(ReleaseResponse).GetProperty(nameof(ReleaseResponse.ApprovedAnalyticsManifestSha256)),
                Is.Not.Null);
        });
    }

    [Test]
    public void InstrumentationScopesAndHostNames_AreFixedByTheV1Contract()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProductAnalytics.ActivitySource.Name, Is.EqualTo("Beutl.ProductAnalytics"));
            Assert.That(ProductAnalytics.ActivitySource.Version, Is.EqualTo("v1"));
            Assert.That(ProductAnalytics.Meter.Name, Is.EqualTo("Beutl.Quality"));
            Assert.That(ProductAnalytics.Meter.Version, Is.EqualTo("v1"));
            Assert.That(TelemetryService.GetServiceName(TelemetryHostKind.Desktop), Is.EqualTo("beutl.desktop"));
            Assert.That(TelemetryService.GetServiceName(TelemetryHostKind.PackageTools), Is.EqualTo("beutl.package-tools"));
            Assert.That(TelemetryService.GetServiceName((TelemetryHostKind)999), Is.EqualTo("beutl.desktop"));
        });
    }

    [Test, NonParallelizable]
    public void Telemetry_EmitsOneSessionStartForEachNewEnabledInstallation()
    {
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        bool? previousUsageAnalytics = config.UsageAnalytics;
        var starts = new List<string>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ProductAnalytics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity =>
            {
                if (activity.OperationName == ProductEventNames.AppSessionStart
                    && activity.GetTagItem("beutl.event.id") is string eventId)
                {
                    starts.Add(eventId);
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        try
        {
            config.UsageAnalytics = false;
            using IDisposable telemetryLifetime = TelemetryService.GetDisposable(Guid.NewGuid().ToString("N"));
            Assert.That(starts, Is.Empty);

            config.UsageAnalytics = true;
            string firstInstallationId = TelemetryService.Instance!.ActiveInstallationId!;
            config.UsageAnalytics = true;

            // A product operation owned by the old provider is deliberately left
            // incomplete while consent is revoked. It cannot cross the provider
            // replacement boundary when completed afterwards.
            ProductOperation oldOperation = TelemetryService.StartProductOperation(ProductEventNames.ProjectOpen);
            config.UsageAnalytics = false;
            oldOperation.Complete();
            Assert.That(TelemetryService.Instance!.ActiveInstallationId, Is.Null);

            config.UsageAnalytics = true;
            string secondInstallationId = TelemetryService.Instance!.ActiveInstallationId!;
            TelemetryService.ResetUsageIdentity();
            string resetInstallationId = TelemetryService.Instance!.ActiveInstallationId!;

            Assert.Multiple(() =>
            {
                Assert.That(firstInstallationId, Is.Not.EqualTo(secondInstallationId));
                Assert.That(secondInstallationId, Is.Not.EqualTo(resetInstallationId));
                Assert.That(starts, Has.Count.EqualTo(3));
                Assert.That(starts.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(3));
            });
        }
        finally
        {
            config.UsageAnalytics = previousUsageAnalytics;
        }
    }

    [Test]
    public void TelemetryUncleanSessionMarker_IsAnEmptyPresenceMarkerWithoutCanaryPayload()
    {
        string root = Path.Combine(Path.GetTempPath(), $"beutl-unclean-marker-{Guid.NewGuid():N}");
        const string canary = "C:\\Users\\alice\\private-project.beutl|alice@example.test|at Secret.Method";
        try
        {
            TelemetryUncleanSessionMarker.Mark(root);
            string markerPath = TelemetryUncleanSessionMarker.GetPath(root);

            Assert.Multiple(() =>
            {
                Assert.That(TelemetryUncleanSessionMarker.Exists(root), Is.True);
                Assert.That(File.ReadAllBytes(markerPath), Is.Empty);
                Assert.That(File.ReadAllText(markerPath), Does.Not.Contain(canary));
                Assert.That(TelemetryService.UncleanSessionMarkerFileName, Is.Not.EqualTo("last-unhandled-exeption"));
            });

            TelemetryUncleanSessionMarker.Clear(root);
            Assert.That(TelemetryUncleanSessionMarker.Exists(root), Is.False);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void ProductFinalExportGate_DropsFriendAssemblyPathAndFreeTextCanaries()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ProductAnalytics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using Activity valid = CreateCompletedProductActivity(ProductEventNames.ProjectOpen);
        using Activity pathCanary = CreateCompletedProductActivity(
            ProductEventNames.ProjectOpen,
            activity =>
            {
                activity.SetTag("project.path", "C:\\Users\\alice\\private-project.beutl");
                activity.SetTag("free.text", "alice@example.test");
            });
        using var friendSource = new ActivitySource(new ActivitySourceOptions(
            ProductAnalytics.ActivitySourceName)
        {
            Version = "v1",
            Tags = [new KeyValuePair<string, object?>("scope.path", "C:\\Users\\alice")],
            TelemetrySchemaUrl = "https://example.test/private-schema"
        });
        using Activity scopeCanary = CreateCompletedProductActivity(
            ProductEventNames.ProjectOpen,
            source: friendSource);
        Resource resource = CreateProductResource();
        var batch = new Batch<Activity>([valid, pathCanary, scopeCanary], 3);

        Activity[] accepted = ProductExportContract.Filter(batch, resource);

        Assert.Multiple(() =>
        {
            Assert.That(accepted, Is.EqualTo(new[] { valid }));
            Assert.That(ProductExportContract.IsValid(pathCanary, resource), Is.False);
            Assert.That(ProductExportContract.IsValid(scopeCanary, resource), Is.False);
            Assert.That(
                ProductExportContract.IsValid(valid, ResourceBuilder.CreateEmpty().Build()),
                Is.False);
        });
    }

    [Test]
    public void ProductFinalExportGate_DropsAnAcceptedBatchAfterConsentRevocation()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ProductAnalytics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using Activity activity = CreateCompletedProductActivity(ProductEventNames.ProjectOpen);
        var options = new OpenTelemetry.Exporter.OtlpExporterOptions
        {
            Endpoint = new Uri("http://127.0.0.1:1/v1/traces")
        };
        using var exporter = new ProductOtlpTraceExporter(options, static () => false);
        var batch = new Batch<Activity>(activity);

        Assert.Multiple(() =>
        {
            Assert.That(
                ProductOtlpTraceExporter.FilterForExport(batch, CreateProductResource(), static () => false),
                Is.Empty);
            Assert.That(exporter.Export(in batch), Is.EqualTo(ExportResult.Success));
        });
    }

    [Test]
    public void ProductFinalExportGate_DropsUnknownEventsMissingTagsAndInvalidValues()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = static source => source.Name == ProductAnalytics.ActivitySourceName,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(listener);
        using Activity unknownEvent = CreateCompletedProductActivity("project.private");
        using Activity missingDuration = CreateCompletedProductActivity(
            ProductEventNames.ProjectOpen,
            activity => activity.SetTag(ProductAttributeNames.DurationMilliseconds, null));
        using Activity invalidOutcome = CreateCompletedProductActivity(
            ProductEventNames.ProjectOpen,
            activity => activity.SetTag("beutl.outcome", "user-entered-value"));
        using Activity oversizedValue = CreateCompletedProductActivity(
            ProductEventNames.ProjectOpen,
            activity => activity.SetTag(ProductAttributeNames.Trigger, new string('a', 257)));
        Resource resource = CreateProductResource();
        Resource piiResource = ResourceBuilder.CreateEmpty()
            .AddAttributes(resource.Attributes.Append(
                new KeyValuePair<string, object>("user.email", "alice@example.test")))
            .Build();
        using Activity otherwiseValid = CreateCompletedProductActivity(ProductEventNames.ProjectOpen);

        Assert.Multiple(() =>
        {
            Assert.That(ProductExportContract.IsValid(unknownEvent, resource), Is.False);
            Assert.That(ProductExportContract.IsValid(missingDuration, resource), Is.False);
            Assert.That(ProductExportContract.IsValid(invalidOutcome, resource), Is.False);
            Assert.That(ProductExportContract.IsValid(oversizedValue, resource), Is.False);
            Assert.That(ProductExportContract.IsValid(otherwiseValid, piiResource), Is.False);
        });
    }

    [Test]
    public void QualityMetrics_AreDeltaLowCardinalityAndMergeAcrossSources()
    {
        QualityMetricSnapshot[] firstSource = CaptureQualitySource(
            1,
            includeLocalProductCounter: false,
            includeUncleanSession: true);
        QualityMetricSnapshot[] secondSource = CaptureQualitySource(
            2,
            includeLocalProductCounter: false,
            includeUncleanSession: false);
        QualityMetricSnapshot[] combined = [.. firstSource, .. secondSource];

        Assert.Multiple(() =>
        {
            Assert.That(combined, Is.Not.Empty);
            Assert.That(combined.All(x => x.IsValid), Is.True);
            Assert.That(combined.All(x => x.Temporality == AggregationTemporality.Delta), Is.True);
            Assert.That(
                combined.Where(x => x.Name == QualityMetricNames.OperationTotal).Sum(x => x.Value),
                Is.EqualTo(3));
            Assert.That(
                combined.Where(x => x.Name == QualityMetricNames.OperationDuration).Sum(x => x.Value),
                Is.EqualTo(3));
            Assert.That(
                combined.Where(x => x.Name == QualityMetricNames.OperationDuration)
                    .All(x => x.HasMinMax),
                Is.True);
            Assert.That(combined
                .Where(x => x.Name != QualityMetricNames.UncleanSessionTotal)
                .All(x => x.Tags.Count == 2
                    && x.Tags[QualityAttributeNames.Operation] == ProductEventNames.ProjectOpen
                    && x.Tags[QualityAttributeNames.Outcome] == ProductOutcomes.Success), Is.True);
            Assert.That(
                combined.Single(x => x.Name == QualityMetricNames.UncleanSessionTotal),
                Has.Property(nameof(QualityMetricSnapshot.Value)).EqualTo(1)
                    .And.Property(nameof(QualityMetricSnapshot.Tags)).Empty);
        });
    }

    [Test]
    public void QualityFinalExportGate_LeavesProductRecordedCountersLocal()
    {
        QualityMetricSnapshot[] snapshots = CaptureQualitySource(
            0,
            includeLocalProductCounter: true,
            includeUncleanSession: false);

        Assert.That(
            snapshots.Single(x => x.Name == "beutl.product_event.recorded").IsValid,
            Is.False);
    }

    [TestCase(true, 1L)]
    [TestCase(false, -1L)]
    public void QualityFinalExportGate_DropsFriendAssemblyUnknownDimensionsAndInvalidValues(
        bool includePathCanary,
        long value)
    {
        var exporter = new CapturingMetricExporter();
        using var reader = new PeriodicExportingMetricReader(
            exporter,
            exportIntervalMilliseconds: 600_000)
        {
            TemporalityPreference = MetricReaderTemporalityPreference.Delta
        };
        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(CreateQualityResourceBuilder())
            .AddMeter(ProductAnalytics.MeterName)
            .AddView(
                QualityMetricNames.OperationDuration,
                QualityExportContract.CreateDurationHistogramConfiguration())
            .AddReader(reader)
            .Build();
        using var friendMeter = new Meter(ProductAnalytics.MeterName, "v1");
        Counter<long> counter = friendMeter.CreateCounter<long>(
            QualityMetricNames.OperationTotal,
            "{operation}",
            "Number of completed fixed product operations.");
        TagList tags = default;
        tags.Add(QualityAttributeNames.Operation, ProductEventNames.ProjectOpen);
        tags.Add(QualityAttributeNames.Outcome, ProductOutcomes.Success);
        if (includePathCanary)
        {
            tags.Add("project.path", "C:\\Users\\alice\\private-project.beutl");
        }

        counter.Add(value, in tags);
        Assert.That(provider.ForceFlush(), Is.True);

        Assert.That(
            exporter.Snapshots.Single(x => x.Name == QualityMetricNames.OperationTotal).IsValid,
            Is.False,
            includePathCanary
                ? "An unknown friend-assembly dimension must be rejected."
                : "An invalid friend-assembly counter value must be rejected.");
    }

    [Test]
    public void ExtensionQueueAndPackageToolsFinalOutcomesUseOnlyFixedValues()
    {
        var completions = new List<(string Outcome, string? ErrorCode)>();
        using (var succeeded = new ExtensionManageProductOperation(
                   (outcome, errorCode) => completions.Add((outcome, errorCode))))
        {
            succeeded.CompleteSucceeded();
        }

        using (var failed = new ExtensionManageProductOperation(
                   (outcome, errorCode) => completions.Add((outcome, errorCode))))
        {
            failed.CompleteFailed();
        }

        using (var cancelled = new ExtensionManageProductOperation(
                   (outcome, errorCode) => completions.Add((outcome, errorCode))))
        {
            cancelled.CompleteCancelled();
        }

        using (var partial = new ExtensionManageProductOperation(
                   (outcome, errorCode) => completions.Add((outcome, errorCode))))
        {
            partial.CompletePartial();
        }

        using (var uninstallFailed = new ExtensionManageProductOperation(
                   (outcome, errorCode) => completions.Add((outcome, errorCode))))
        {
            uninstallFailed.CompleteUninstallFailed();
        }

        Assert.That(completions, Is.EqualTo(new[]
        {
            (ProductOutcomes.Success, (string?)null),
            (ProductOutcomes.Failed, "extension-install-failed"),
            (ProductOutcomes.Cancelled, "cancelled"),
            (ProductOutcomes.Partial, "extension-unload-partial"),
            (ProductOutcomes.Failed, "extension-unload-failed")
        }));
        Assert.That(
            ExtensionManageTelemetry.QueuedCompletion,
            Is.EqualTo((ProductOutcomes.Queued, (string?)null)));
    }

    [Test, NonParallelizable]
    public void PackageChangesQueue_RecordsOneQueuedOutcomeAndSupportsAnOwnedOperation()
    {
        var completions = new List<(string Outcome, string? ErrorCode)>();
        PackageAnalyticsHandlers? previous = PackageAnalytics.ExchangeHandlers(new PackageAnalyticsHandlers(
            static () => null,
            () => completions.Add(ExtensionManageTelemetry.QueuedCompletion),
            static (_, _) => { },
            static _ => { },
            static _ => { }));
        try
        {
            var queue = new PackageChangesQueue();
            var direct = new PackageIdentity("acme.direct", NuGetVersion.Parse("1.0.0"));
            var owned = new PackageIdentity("acme.owned", NuGetVersion.Parse("1.0.0"));
            var deferredUninstall = new PackageIdentity("acme.uninstall", NuGetVersion.Parse("1.0.0"));

            queue.InstallQueue(direct);
            queue.InstallQueue(owned, recordAnalytics: false);
            queue.UninstallQueue(deferredUninstall);

            Assert.Multiple(() =>
            {
                Assert.That(completions, Is.EqualTo(new[]
                {
                    (ProductOutcomes.Queued, (string?)null),
                    (ProductOutcomes.Queued, (string?)null)
                }));
                Assert.That(queue.GetInstalls(), Is.EquivalentTo(new[] { direct, owned }));
                Assert.That(queue.GetUninstalls(), Is.EquivalentTo(new[] { deferredUninstall }));
            });
        }
        finally
        {
            PackageAnalytics.ExchangeHandlers(previous);
        }
    }

    [Test]
    public void ProductSummaryBuffer_UsesTheInjectedFiveMinuteTimerAndSupportsShutdownDrain()
    {
        var time = new FakeTimeProvider();
        var emitted = new List<ProductSummarySnapshot>();
        ProductSummaryBuffer? buffer = null;
        buffer = new ProductSummaryBuffer(
            time,
            TimeSpan.FromMinutes(5),
            () => emitted.AddRange(buffer!.Drain()));
        using (buffer!)
        {
            var key = new ProductSummaryKey(ProductEventNames.EditorActionSummary, "undo", null);

            buffer.Add(key, ProductOutcomes.Success, null, 10);
            buffer.Add(key, ProductOutcomes.Success, null, 30);
            time.Advance(TimeSpan.FromMinutes(4).Add(TimeSpan.FromSeconds(59)));

            Assert.That(emitted, Is.Empty);

            time.Advance(TimeSpan.FromSeconds(1));

            ProductSummarySnapshot snapshot = emitted.Single();
            Assert.Multiple(() =>
            {
                Assert.That(snapshot.Key, Is.EqualTo(key));
                Assert.That(snapshot.Count, Is.EqualTo(2));
                Assert.That(snapshot.Outcome, Is.EqualTo(ProductOutcomes.Success));
                Assert.That(snapshot.AverageDurationMilliseconds, Is.EqualTo(20));
            });

            buffer.Add(key, ProductOutcomes.Blocked, "no-history-change", 4);
            ProductSummarySnapshot shutdownSnapshot = buffer.Drain().Single();
            Assert.That(shutdownSnapshot.Count, Is.EqualTo(1));
        }
    }

    [Test]
    public void ProductSummaryBuffer_BoundsDistinctFeaturesAndCoalescesOverflow()
    {
        using var buffer = new ProductSummaryBuffer(
            new FakeTimeProvider(),
            TimeSpan.FromMinutes(5),
            static () => { });

        for (int i = 0; i < ProductSummaryBuffer.MaximumDistinctFeatures + 2; i++)
        {
            buffer.Add(
                new ProductSummaryKey(
                    ProductEventNames.AgentToolSummary,
                    "mcp",
                    $"extension/acme.plugin/effect/feature-{i}"),
                ProductOutcomes.Success,
                null,
                1);
        }

        ProductSummarySnapshot[] snapshots = buffer.Drain();
        ProductSummarySnapshot overflow = snapshots.Single(snapshot =>
            snapshot.Key.FeatureId == ProductSummaryBuffer.OverflowFeatureId);

        Assert.Multiple(() =>
        {
            Assert.That(
                snapshots.Count(snapshot => snapshot.Key.FeatureId is not ProductSummaryBuffer.OverflowFeatureId),
                Is.EqualTo(ProductSummaryBuffer.MaximumDistinctFeatures));
            Assert.That(overflow.Count, Is.EqualTo(2));
        });
    }

    [Test]
    public void ContinuousPlaybackProducesOneFiveMinuteSummaryInsteadOfRawSpans()
    {
        using var buffer = new ProductSummaryBuffer(
            new FakeTimeProvider(),
            TimeSpan.FromMinutes(5),
            static () => { });
        var key = new ProductSummaryKey(ProductEventNames.PreviewPlaybackSummary, "player", null);

        buffer.Add(key, ProductOutcomes.Success, null, 10);
        buffer.Add(key, ProductOutcomes.Success, null, 20);
        buffer.Add(key, ProductOutcomes.Cancelled, "cancelled", 30);

        ProductSummarySnapshot snapshot = buffer.Drain().Single();
        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Key, Is.EqualTo(key));
            Assert.That(snapshot.Count, Is.EqualTo(3));
            Assert.That(snapshot.Outcome, Is.EqualTo(ProductOutcomes.Partial));
            Assert.That(snapshot.AverageDurationMilliseconds, Is.EqualTo(20));
        });
    }

    [Test]
    public void ProductAttributeValues_RejectPathCanariesAndAcceptOnlyFixedDimensions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                ProductAttributeNames.IsAllowedValue(ProductAttributeNames.Trigger, "/home/user/project"),
                Is.False);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(ProductAttributeNames.ErrorCode, "user-entered-value"),
                Is.False);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    "extension/acme.plugin/effect/blur"),
                Is.True);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    "extension/acme.plugin/effect/../../secret"),
                Is.False);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    "builtin/decoder/animated-image"),
                Is.True);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    $"extension/{new string('a', 100)}/effect/blur"),
                Is.True);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    $"extension/{new string('a', 101)}/effect/blur"),
                Is.False);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(
                    ProductAttributeNames.FeatureId,
                    "extension/not_allowed/effect/blur"),
                Is.False);
            Assert.That(
                ProductAttributeNames.IsAllowedValue(ProductAttributeNames.CountBucket, "51+"),
                Is.True);
        });
    }

    [Test]
    public void SafeDiagnostics_UseOnlyTheCollectorApprovedDimensions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                TelemetryDiagnostics.IsAllowed("project", "open-failed", ProductOutcomes.Failed),
                Is.True);
            Assert.That(
                TelemetryDiagnostics.IsAllowed("unknown", "open-failed", ProductOutcomes.Failed),
                Is.False);
            Assert.That(
                TelemetryDiagnostics.IsAllowed("project", "exception-message", ProductOutcomes.Failed),
                Is.False);
            Assert.That(
                TelemetryDiagnostics.IsAllowed("project", "open-failed", "retrying"),
                Is.False);
        });
    }

    [Test, NonParallelizable]
    public void OperationalDiagnosticResourceNeverContainsProductIdentityCanaries()
    {
        const string sessionCanary = "9f061e9d9f84472a9f451616461a0451";
        TelemetryConfig config = GlobalConfiguration.Instance.TelemetryConfig;
        bool? previousUsageAnalytics = config.UsageAnalytics;
        try
        {
            config.UsageAnalytics = true;
            using IDisposable telemetryLifetime = TelemetryService.GetDisposable(sessionCanary);
            string installationCanary = TelemetryService.Instance!.ActiveInstallationId!;
            ResourceBuilder builder = (ResourceBuilder)typeof(TelemetryService)
                .GetMethod("CreateOperationalResource", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(TelemetryService.Instance, null)!;
            Resource resource = builder.Build();
            KeyValuePair<string, object>[] attributes = [.. resource.Attributes];

            Assert.Multiple(() =>
            {
                Assert.That(attributes.Select(attribute => attribute.Key), Does.Not.Contain("beutl.session.id"));
                Assert.That(attributes.Select(attribute => attribute.Key), Does.Not.Contain("beutl.installation.id"));
                Assert.That(
                    attributes.Select(attribute => attribute.Value?.ToString()),
                    Does.Not.Contain(sessionCanary));
                Assert.That(
                    attributes.Select(attribute => attribute.Value?.ToString()),
                    Does.Not.Contain(installationCanary));
            });
        }
        finally
        {
            config.UsageAnalytics = previousUsageAnalytics;
        }
    }

    [Test]
    public void ProductOperation_DoesNotClearTheAmbientOperationalActivity()
    {
        using var source = new ActivitySource("Beutl.ProductAnalytics.Test");
        using var listener = new ActivityListener
        {
            ShouldListenTo = static candidate => candidate.Name == "Beutl.ProductAnalytics.Test",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);
        using var ambient = new Activity("operational").Start();
        Activity? previous = Activity.Current;
        Activity? product;
        try
        {
            Activity.Current = null;
            product = source.StartActivity("product", ActivityKind.Internal);
        }
        finally
        {
            Activity.Current = previous;
        }

        Assert.That(product, Is.Not.Null);
        Assert.That(product!.Parent, Is.Null);
        using (product)
        {
            using var operation = new ProductOperation(product, static (_, _, _) => { }, ambient);

            operation.Complete();

            Assert.That(Activity.Current, Is.SameAs(ambient));
        }
    }

    private static Activity CreateCompletedProductActivity(
        string name,
        Action<Activity>? addCanary = null,
        ActivitySource? source = null)
    {
        Activity? previous = Activity.Current;
        Activity activity;
        try
        {
            Activity.Current = null;
            activity = (source ?? ProductAnalytics.ActivitySource)
                .StartActivity(name, ActivityKind.Internal)!;
        }
        finally
        {
            Activity.Current = previous;
        }

        activity.SetTag("beutl.event.id", Guid.NewGuid().ToString("N"));
        activity.SetTag("beutl.outcome", ProductOutcomes.Success);
        activity.SetTag(ProductAttributeNames.DurationMilliseconds, 1d);
        addCanary?.Invoke(activity);
        activity.Stop();
        return activity;
    }

    private static async Task<string> RunIdentityTestHostAsync(string command, string homeDirectory)
    {
        string helperPath = Path.Combine(
            FindRepositoryRoot(),
            "tests",
            "Beutl.Telemetry.TestHost",
            "bin",
            GetBuildConfiguration(),
            "net10.0",
            "Beutl.Telemetry.TestHost.dll");
        Assert.That(File.Exists(helperPath), Is.True, $"Missing identity test host: {helperPath}");

        string dotnetHost = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet";
        var startInfo = new ProcessStartInfo(dotnetHost)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(helperPath);
        startInfo.ArgumentList.Add(command);
        startInfo.ArgumentList.Add(homeDirectory);

        using Process process = Process.Start(startInfo)!;
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        string standardError = await error;

        Assert.That(process.ExitCode, Is.Zero, standardError);
        return (await output).Trim();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Beutl.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new DirectoryNotFoundException("Could not locate the Beutl repository root.");
    }

    private static string GetBuildConfiguration()
    {
        string baseDirectory = AppContext.BaseDirectory.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return Directory.GetParent(baseDirectory)?.Name ?? "Debug";
    }

    private static Resource CreateProductResource()
    {
        return ResourceBuilder.CreateEmpty().AddAttributes(
        [
            new KeyValuePair<string, object>("service.name", "beutl.desktop"),
            new KeyValuePair<string, object>("service.version", "2.99.99"),
            new KeyValuePair<string, object>("beutl.telemetry.stream", "product"),
            new KeyValuePair<string, object>("beutl.analytics.schema", "v1"),
            new KeyValuePair<string, object>("beutl.installation.id", Guid.NewGuid().ToString("N")),
            new KeyValuePair<string, object>("beutl.session.id", Guid.NewGuid().ToString("N")),
            new KeyValuePair<string, object>("beutl.first_seen_month", "2026-08"),
            new KeyValuePair<string, object>("beutl.release.channel", "stable"),
            new KeyValuePair<string, object>("os.type", "windows"),
            new KeyValuePair<string, object>("process.architecture", "x64"),
            new KeyValuePair<string, object>("beutl.renderer", "unknown")
        ]).Build();
    }

    private static ResourceBuilder CreateQualityResourceBuilder()
    {
        return ResourceBuilder.CreateEmpty().AddAttributes(
        [
            new KeyValuePair<string, object>("service.name", "beutl.desktop"),
            new KeyValuePair<string, object>("service.version", "2.99.99"),
            new KeyValuePair<string, object>("beutl.telemetry.stream", "product"),
            new KeyValuePair<string, object>("beutl.analytics.schema", "v1"),
            new KeyValuePair<string, object>("beutl.release.channel", "stable"),
            new KeyValuePair<string, object>("os.type", "windows"),
            new KeyValuePair<string, object>("process.architecture", "x64"),
            new KeyValuePair<string, object>("beutl.renderer", "unknown")
        ]);
    }

    private static QualityMetricSnapshot[] CaptureQualitySource(
        int operationCount,
        bool includeLocalProductCounter,
        bool includeUncleanSession)
    {
        var exporter = new CapturingMetricExporter();
        using var reader = new PeriodicExportingMetricReader(
            exporter,
            exportIntervalMilliseconds: 600_000)
        {
            TemporalityPreference = MetricReaderTemporalityPreference.Delta
        };
        using MeterProvider provider = Sdk.CreateMeterProviderBuilder()
            .SetResourceBuilder(CreateQualityResourceBuilder())
            .AddMeter(ProductAnalytics.MeterName)
            .AddView(
                QualityMetricNames.OperationDuration,
                QualityExportContract.CreateDurationHistogramConfiguration())
            .AddReader(reader)
            .Build();

        for (int i = 0; i < operationCount; i++)
        {
            ProductAnalytics.RecordQualityOperation(
                ProductEventNames.ProjectOpen,
                ProductOutcomes.Success,
                10 + i);
        }

        if (includeLocalProductCounter)
        {
            ProductAnalytics.ProductEventRecorded.Add(1);
        }

        if (includeUncleanSession)
        {
            ProductAnalytics.RecordUncleanSession();
        }

        Assert.That(provider.ForceFlush(), Is.True);
        return [.. exporter.Snapshots];
    }

    private sealed class TrustedFeatureType;

    private sealed class CapturingMetricExporter : BaseExporter<Metric>
    {
        internal List<QualityMetricSnapshot> Snapshots { get; } = [];

        public override ExportResult Export(in Batch<Metric> batch)
        {
            Resource resource = ParentProvider.GetResource();
            foreach (Metric metric in batch)
            {
                bool valid = QualityExportContract.IsValid(metric, resource);
                foreach (ref readonly MetricPoint point in metric.GetMetricPoints())
                {
                    var tags = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, object?> tag in point.Tags)
                    {
                        tags.Add(tag.Key, (string)tag.Value!);
                    }
                    long value = metric.MetricType == MetricType.Histogram
                        ? point.GetHistogramCount()
                        : point.GetSumLong();
                    bool hasMinMax = metric.MetricType == MetricType.Histogram
                        && point.TryGetHistogramMinMaxValues(out _, out _);
                    Snapshots.Add(new QualityMetricSnapshot(
                        metric.Name,
                        metric.Temporality,
                        tags,
                        value,
                        hasMinMax,
                        valid));
                }
            }

            return ExportResult.Success;
        }
    }

    private sealed record QualityMetricSnapshot(
        string Name,
        AggregationTemporality Temporality,
        IReadOnlyDictionary<string, string> Tags,
        long Value,
        bool HasMinMax,
        bool IsValid);
}
