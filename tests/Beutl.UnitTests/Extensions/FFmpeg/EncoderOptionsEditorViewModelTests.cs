using Beutl.Api.Services;
using Beutl.Collections;
using Beutl.Editor.Components.PropertyEditors.Services;
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

    [Test]
    public void PackageDiscoveryLoadsTheSpecializedOptionsEditor()
    {
        var provider = new ExtensionProvider();
        var commands = new ContextCommandManager(new ContextCommandSettingsStore());
        var manager = new PackageManager(new InstalledPackageRepository(), provider, commands, apiApplication: null!);
        var loaded = manager.LoadPackageExtensions([typeof(FFmpegEncoderSpecializedPropertyExtension)]);
        var extension = (PropertyEditorExtension)loaded.Single();
        provider.AddExtensions(1, loaded);
        var settings = CreateSettings();
        var adapter = new EnginePropertyAdapter<CoreList<AdditionalOption>>(settings.Options, settings);
        using IPropertyEditorContext? context = new PropertyEditorFactoryAdapter(provider).CreateEditor(adapter);
        Assert.That(context, Is.InstanceOf<EncoderOptionsEditorViewModel>());
        extension.Unload();
    }

    [Test]
    public async Task LoadingUnsupportedProfilePreservesSavedValuesAndReportsWarning()
    {
        var settings = CreateSettings();
        settings.Options.Value.Single(o => o.Name.Value == "profile").Value.Value = "main10";
        settings.Options.Value.Add(new AdditionalOption("x265-params", "repeat-headers=1"));
        await Cache(settings, Schema("main", "high", "high10"));
        using var model = CreateModel(settings);

        Assert.That(model.GetValue("profile"), Is.EqualTo("main10"));
        Assert.That(model.GetWarning(model.Descriptors[0], "main10"), Is.Not.Null);
        Assert.That(model.GetValue("x265-params"), Is.EqualTo("repeat-headers=1"));

        model.SetValue("profile", "high");
        model.SetValue("crf", "18.5");
        Assert.That(settings.Options.Value.Single(o => o.Name.Value == "profile").Value.Value, Is.EqualTo("high"));
        Assert.That(settings.Options.Value.Single(o => o.Name.Value == "crf").Value.Value, Is.EqualTo("18.5"));
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
        settings.Options.Value.Single(o => o.Name.Value == "profile").Value.Value = "main";
        await Cache(settings, Schema("main", "high10"));
        using var model = CreateModel(settings);
        var tenBit = CreateSettings();
        tenBit.OutputFile = settings.OutputFile;
        tenBit.Format.Value = FFPixelFormat.YUV420P10LE;
        await Cache(tenBit, Schema("high10"));

        settings.Format.Value = tenBit.Format.Value;
        Assert.That(model.Descriptors[0].Choices.Select(c => c.Value), Is.EqualTo(new[] { "high10" }));
        Assert.That(model.GetValue("profile"), Is.EqualTo("main"));
        Assert.That(model.GetWarning(model.Descriptors[0], "main"), Is.Not.Null);

        tenBit.Codec.Value = new CodecRecord("libx265", "HEVC");
        await Cache(tenBit, Schema("main10"));
        settings.Codec.Value = tenBit.Codec.Value;
        model.SetValue("profile", "main10");
        Assert.That(model.GetWarning(model.Descriptors[0], "main10"), Is.Null);
    }

    [Test]
    public async Task AutomaticRemovesOnlyTheSelectedOverrideAndEditsResolveDuplicateKeys()
    {
        var settings = CreateSettings();
        settings.Options.Value.Add(new AdditionalOption("profile", "main10"));
        await Cache(settings, Schema("high"));
        using var model = CreateModel(settings);

        model.SetValue("profile", "high");
        Assert.That(settings.Options.Value.Count(o => o.Name.Value == "profile"), Is.EqualTo(1));
        model.SetValue("profile", null);
        Assert.That(settings.Options.Value.Any(o => o.Name.Value == "profile"), Is.False);
        Assert.That(settings.Options.Value.Any(o => o.Name.Value == "crf"), Is.True);
        Assert.That(model.AddOption("crf"), Is.False);
        Assert.That(model.AddOption("custom-option"), Is.True);
    }

    [Test]
    public async Task ReplacingOptionsReconnectsObserversAndDisposeStopsNotifications()
    {
        var settings = CreateSettings();
        await Cache(settings, Schema("high"));
        var model = CreateModel(settings);
        CoreList<AdditionalOption> oldList = settings.Options.Value;
        int notifications = 0;
        model.ValuesChanged += () => notifications++;
        settings.Options.Value = [new AdditionalOption("profile", "high")];
        int afterReplacement = notifications;
        oldList[0].Value.Value = "stale";
        Assert.That(notifications, Is.EqualTo(afterReplacement));
        settings.Options.Value[0].Value.Value = "main10";
        Assert.That(notifications, Is.GreaterThan(afterReplacement));
        model.Dispose();
        int afterDispose = notifications;
        settings.Options.Value[0].Value.Value = "high";
        Assert.That(notifications, Is.EqualTo(afterDispose));
    }

    [Test]
    public async Task RenamingToAnExistingNamePreservesBothOptionsAndExportDictionary()
    {
        var settings = CreateSettings();
        await Cache(settings, Schema("high"));
        using var model = CreateModel(settings);
        AdditionalOption option = settings.Options.Value.Single(o => o.Name.Value == "profile");
        string originalValue = option.Value.Value;
        string quality = model.GetValue("crf")!;

        model.RenameOption(option, " crf ");

        Assert.That(option.Name.Value, Is.EqualTo("profile"));
        Assert.That(option.Value.Value, Is.EqualTo(originalValue));
        Assert.That(model.GetValue("crf"), Is.EqualTo(quality));
        Assert.DoesNotThrow(() => settings.Options.Value.ToDictionary(o => o.Name.Value, o => o.Value.Value));

        model.RenameOption(option, " custom-option ");
        Assert.That(option.Name.Value, Is.EqualTo("custom-option"));
        model.SetValue(option, "custom-value");
        Assert.That(model.GetValue("custom-option"), Is.EqualTo("custom-value"));
        model.RemoveOption(option);
        Assert.That(model.GetValue("custom-option"), Is.Null);
    }

    [TestCase(EncoderOptionKind.Choice, "p4", true)]
    [TestCase(EncoderOptionKind.Choice, "P4", false)]
    [TestCase(EncoderOptionKind.Choice, "4", true)]
    [TestCase(EncoderOptionKind.Integer, "1.5", false)]
    [TestCase(EncoderOptionKind.Integer, "2", true)]
    [TestCase(EncoderOptionKind.Integer, "invalid", false)]
    [TestCase(EncoderOptionKind.Number, "2.5", true)]
    [TestCase(EncoderOptionKind.Number, "-1", false)]
    [TestCase(EncoderOptionKind.Number, "101", false)]
    [TestCase(EncoderOptionKind.Boolean, "true", true)]
    [TestCase(EncoderOptionKind.Boolean, "off", true)]
    [TestCase(EncoderOptionKind.Boolean, "-1", false)]
    [TestCase(EncoderOptionKind.Boolean, "2", false)]
    [TestCase(EncoderOptionKind.Text, "unlisted", true)]
    public async Task ValidatesNativeOptionConstraintsWithoutRewritingValues(EncoderOptionKind kind, string value, bool valid)
    {
        var settings = CreateSettings();
        var descriptor = new EncoderOptionInfo
        {
            Name = "custom-option",
            Kind = kind,
            Minimum = 0,
            Maximum = 100,
            Choices = [new() { Value = "p4", NumericValue = 4 }],
        };
        await Cache(settings, [descriptor]);
        using var model = CreateModel(settings);
        model.SetValue(descriptor.Name, value);

        Assert.That(model.GetWarning(descriptor, value) == null, Is.EqualTo(valid));
        Assert.That(model.GetValue(descriptor.Name), Is.EqualTo(value));
        Assert.That(model.GetWarning(descriptor, null), Is.Null);
    }

    // The option cache is process-wide, so each settings object gets its own output file and cache key.
    private static FFmpegVideoEncoderSettings CreateSettings() => new()
    {
        Codec = { Value = new CodecRecord("libx264", "H.264") },
        OutputFile = $"{Guid.NewGuid():N}.mp4",
        Format = { Value = FFPixelFormat.YUV420P },
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
        => new(new EnginePropertyAdapter<CoreList<AdditionalOption>>(settings.Options, settings),
            new FFmpegEncoderSpecializedPropertyExtension());

    private static Task<OptionsQueryResult<EncoderOptionInfo>> Cache(FFmpegVideoEncoderSettings settings, EncoderOptionInfo[] schema)
        => FFmpegOptionsCaches.EncoderOptions.GetOrQueryAsync(
            EncoderOptionsEditorViewModel.BuildCacheKey(CodecOptionQuery.Create(settings.Codec.Value, settings.OutputFile), settings.Format.Value),
            () => Task.FromResult(new OptionsQueryResult<EncoderOptionInfo>(schema, false)));
}
