using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Extensibility;
using Beutl.Extensions.FFmpeg.Encoding;
using Beutl.Extensions.FFmpeg.PropertyEditors;
using Beutl.FFmpegIpc;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.PropertyAdapters;
using Beutl.Serialization;

namespace Beutl.UnitTests.Extensions.FFmpeg;

[TestFixture]
public sealed class EncoderOptionsEditorViewModelTests
{
    private static EncoderOptionInfo[] Schema(params string[] profiles) =>
    [
        new() { Name = "profile", Kind = EncoderOptionKind.Choice, Choices = profiles.Select(p => new EncoderOptionChoiceInfo { Value = p }).ToArray() },
        new() { Name = "crf", Kind = EncoderOptionKind.Number, Minimum = -1, Maximum = 51, DefaultValue = "-1" },
    ];

    [TearDown]
    public void TearDown() => FFmpegOptionsCaches.ClearAll();

    [Test]
    public void PackageDiscoveryLoadsTheSpecializedOptionsEditor()
    {
        var provider = new ExtensionProvider();
        var commands = new ContextCommandManager(new ContextCommandSettingsStore(), new ContextCommandHandlerRegistry());
        var manager = new PackageManager(new InstalledPackageRepository(), provider, commands, apiApplication: null!);
        var loaded = manager.LoadPackageExtensions([typeof(FFmpegEncoderSpecializedPropertyExtension)]);
        var extension = (PropertyEditorExtension)loaded.Single();
        var settings = CreateSettings();
        var adapter = new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings);
        Assert.That(extension.MatchProperty([adapter]), Is.EqualTo(new[] { adapter }));
        extension.Unload();
    }

    [Test]
    public async Task LoadingUnsupportedProfilePreservesSavedValuesAndReportsWarning()
    {
        var settings = CreateSettings();
        settings.Options.Single(o => o.Name == "profile").Value = "main10";
        settings.Options.Add(new AdditionalOption("x265-params", "repeat-headers=1"));
        await Cache(settings, Schema("main", "high", "high10"));
        using var model = CreateModel(settings);

        Assert.That(model.GetValue("profile"), Is.EqualTo("main10"));
        Assert.That(model.GetWarning(model.Descriptors[0], "main10"), Is.Not.Null);
        Assert.That(model.GetValue("x265-params"), Is.EqualTo("repeat-headers=1"));

        model.SetValue("profile", "high");
        model.SetValue("crf", "18.5");
        Assert.That(settings.Options.Single(o => o.Name == "profile").Value, Is.EqualTo("high"));
        Assert.That(settings.Options.Single(o => o.Name == "crf").Value, Is.EqualTo("18.5"));
        Assert.That(model.GetWarning(model.Descriptors[0], "high"), Is.Null);
        Assert.That(model.GetValue("x265-params"), Is.EqualTo("repeat-headers=1"));
        var serialized = CoreSerializer.SerializeToJsonObject(settings)["Options"]!.AsArray();
        Assert.That(serialized.Any(o => o?["Name"]?.GetValue<string>() == "crf"
            && o["Value"]?.GetValue<string>() == "18.5"), Is.True);
        Assert.That(serialized.Any(o => o?["Name"]?.GetValue<string>() == "x265-params"
            && o["Value"]?.GetValue<string>() == "repeat-headers=1"), Is.True);
    }

    [Test]
    public async Task CodecAndPixelFormatChangesRefreshChoicesWithoutRewritingOptions()
    {
        var settings = CreateSettings();
        settings.Options.Single(o => o.Name == "profile").Value = "main";
        await Cache(settings, Schema("main", "high10"));
        using var model = CreateModel(settings);
        var tenBit = CreateSettings();
        tenBit.Format = FFPixelFormat.YUV420P10LE;
        await Cache(tenBit, Schema("high10"));

        settings.Format = tenBit.Format;
        Assert.That(model.Descriptors[0].Choices.Select(c => c.Value), Is.EqualTo(new[] { "high10" }));
        Assert.That(model.GetValue("profile"), Is.EqualTo("main"));
        Assert.That(model.GetWarning(model.Descriptors[0], "main"), Is.Not.Null);

        tenBit.Codec = new CodecRecord("libx265", "HEVC");
        await Cache(tenBit, Schema("main10"));
        settings.Codec = tenBit.Codec;
        model.SetValue("profile", "main10");
        Assert.That(model.GetWarning(model.Descriptors[0], "main10"), Is.Null);
    }

    [Test]
    public async Task AutomaticRemovesOnlyTheSelectedOverrideAndEditsResolveDuplicateKeys()
    {
        var settings = CreateSettings();
        settings.Options.Add(new AdditionalOption("profile", "main10"));
        await Cache(settings, Schema("high"));
        using var model = CreateModel(settings);

        model.SetValue("profile", "high");
        Assert.That(settings.Options.Count(o => o.Name == "profile"), Is.EqualTo(1));
        model.SetValue("profile", null);
        Assert.That(settings.Options.Any(o => o.Name == "profile"), Is.False);
        Assert.That(settings.Options.Any(o => o.Name == "crf"), Is.True);
        Assert.That(model.AddOption("crf"), Is.False);
        Assert.That(model.AddOption("custom-option"), Is.True);
    }

    [Test]
    public async Task ReplacingOptionsReconnectsObserversAndDisposeStopsNotifications()
    {
        var settings = CreateSettings();
        await Cache(settings, Schema("high"));
        var model = CreateModel(settings);
        CoreList<AdditionalOption> oldList = settings.Options;
        int notifications = 0;
        model.ValuesChanged += () => notifications++;
        settings.Options = [new AdditionalOption("profile", "high")];
        int afterReplacement = notifications;
        oldList[0].Value = "stale";
        Assert.That(notifications, Is.EqualTo(afterReplacement));
        settings.Options[0].Value = "main10";
        Assert.That(notifications, Is.GreaterThan(afterReplacement));
        model.Dispose();
        int afterDispose = notifications;
        settings.Options[0].Value = "high";
        Assert.That(notifications, Is.EqualTo(afterDispose));
    }

    private static FFmpegVideoEncoderSettings CreateSettings() => new()
    {
        Codec = new CodecRecord("libx264", "H.264"),
        OutputFile = "out.mp4",
        Format = FFPixelFormat.YUV420P,
    };

    [Test]
    public async Task EncoderSpecificNamesAppearWithoutAnOptionWhitelist()
    {
        var settings = CreateSettings();
        EncoderOptionInfo[] schema = [new()
        {
            Name = "cpu-used", Kind = EncoderOptionKind.Integer, DefaultValue = "1", Minimum = -8, Maximum = 8,
        }];
        await Cache(settings, schema);
        using var model = CreateModel(settings);
        Assert.That(model.ActiveDescriptors, Is.Empty);
        Assert.That(model.AddOption("cpu-used"), Is.True);
        Assert.That(model.ActiveDescriptors.Select(d => d.Name), Is.EqualTo(new[] { "cpu-used" }));
        Assert.That(model.GetValue("cpu-used"), Is.EqualTo("1"));
    }

    [Test]
    public async Task NumericConstantsAreSuggestionsRatherThanAClosedValueSet()
    {
        var settings = CreateSettings();
        var descriptor = new EncoderOptionInfo
        {
            Name = "deadline",
            Kind = EncoderOptionKind.Choice,
            AllowsNumericValues = true,
            RequiresInteger = true,
            Minimum = 0,
            Maximum = 2000000,
            Choices = [new() { Value = "realtime", NumericValue = 1 }],
        };
        await Cache(settings, [descriptor]);
        using var model = CreateModel(settings);
        Assert.That(model.GetWarning(descriptor, "realtime"), Is.Null);
        Assert.That(model.GetWarning(descriptor, "120000"), Is.Null);
        Assert.That(model.GetWarning(descriptor, "1.5"), Is.Not.Null);
        Assert.That(model.GetWarning(descriptor, "unknown"), Is.Not.Null);
    }

    private static EncoderOptionsEditorViewModel CreateModel(FFmpegVideoEncoderSettings settings)
        => new(new CorePropertyAdapter<CoreList<AdditionalOption>>(FFmpegVideoEncoderSettings.OptionsProperty, settings),
            new FFmpegEncoderSpecializedPropertyExtension());

    private static Task<OptionsQueryResult<EncoderOptionInfo>> Cache(FFmpegVideoEncoderSettings settings, EncoderOptionInfo[] schema)
        => FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(
            EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec, settings.OutputFile), settings.Format),
            () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
}
