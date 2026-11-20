using Beutl.Configuration;
using Beutl.Graphics.Rendering.Cache;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Serialization;
using Beutl.Testing.Headless;
using Microsoft.Extensions.Logging;

namespace Beutl.UnitTests.Configuration;

public class ConfigurationOwnershipTests
{
    [Test]
    public void Render_cache_options_follow_only_the_supplied_configuration()
    {
        var first = new GlobalConfiguration();
        var second = new GlobalConfiguration();
        first.EditorConfig.IsNodeCacheEnabled.Value = true;
        first.EditorConfig.NodeCacheMinPixels.Value = 123;
        Assert.Multiple(() =>
        {
            Assert.That(RenderCacheOptions.CreateFromConfiguration(first.EditorConfig).IsEnabled, Is.True);
            Assert.That(RenderCacheOptions.CreateFromConfiguration(second.EditorConfig).IsEnabled, Is.False);
            Assert.That(second.EditorConfig.NodeCacheMinPixels.Value, Is.EqualTo(new EditorConfig().NodeCacheMinPixels.Value));
        });
    }

    [Test]
    public void DecoderRegistriesOwnTheirRegistrationsAndPriorityChanges()
    {
        var configA = new ExtensionConfig();
        var configB = new ExtensionConfig();
        var a = new DecoderRegistry(configA);
        var b = new DecoderRegistry(configB);
        var aFirst = new FirstDecoder();
        var aSecond = new SecondDecoder();
        var bFirst = new FirstDecoder();
        var bSecond = new SecondDecoder();
        a.Register(aFirst);
        a.Register(aSecond);
        Assert.That(b.FileExtensions.IsVideo("clip.second"), Is.False);
        b.Register(bSecond);
        b.Register(bFirst);
        int changesA = 0, changesB = 0;
        a.DecodersChanged += (_, _) => changesA++;
        b.DecodersChanged += (_, _) => changesB++;

        configA.DecoderPriority.Add(new(TypeFormat.ToString(typeof(SecondDecoder))));
        Assert.That(changesB, Is.Zero);
        configB.DecoderPriority.Add(new(TypeFormat.ToString(typeof(FirstDecoder))));
        Assert.That(a.EnumerateDecoder(), Is.EqualTo(new IDecoderInfo[] { aSecond, aFirst }));
        Assert.That(b.EnumerateDecoder(), Is.EqualTo(new IDecoderInfo[] { bFirst, bSecond }));
        configA.DecoderPriority.Clear();
        b.Unregister(bSecond);
        Assert.That(a.EnumerateDecoder(), Is.EqualTo(new IDecoderInfo[] { aFirst, aSecond }));
        Assert.That(b.EnumerateDecoder(), Is.EqualTo(new IDecoderInfo[] { bFirst }));
        Assert.That(a.FileExtensions.IsVideo("clip.second"), Is.True);
        Assert.That(b.FileExtensions.IsVideo("clip.second"), Is.False);
        Assert.That(changesA, Is.EqualTo(2));
        Assert.That(changesB, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public Task HostsCanUseDefaultsOrInitializeTheirSectionsBeforeFirstUse(bool explicitInitialization)
        => TestWorkerProgram.RunAsync(TestWorkerProgram.ConfigurationInitializationWorkerArgument, explicitInitialization.ToString());

    internal static void RunInitializationWorker(bool explicitInitialization)
    {
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole());
        Log.LoggerFactory = loggerFactory;
        BeutlHomeIsolation.Begin("beutl-configuration-initialization");
        try
        {
            var fontConfig = new FontConfig();
            fontConfig.FontDirectories.Clear();
            var decoderConfig = new ExtensionConfig();
            decoderConfig.DecoderPriority.Add(new(TypeFormat.ToString(typeof(SecondDecoder))));
            var decoders = new DecoderRegistry(explicitInitialization ? decoderConfig : new ExtensionConfig());

            var fonts = new FontManager(explicitInitialization ? fontConfig : new FontConfig());
            FontFamily[] families = fonts.FontFamilies.ToArray();
            Assert.That(families, Does.Contain(FontFamily.Default));
            Assert.That(fonts.IsRegistered(FontFamily.Default), Is.True);
            string[] expectedDirectories = [.. (explicitInitialization ? fontConfig : new FontConfig()).FontDirectories,
                BeutlEnvironment.GetMaterialsDirectoryPath()];
            Assert.That(fonts.FontDirectories, Is.EqualTo(expectedDirectories));

            var first = new FirstDecoder();
            var second = new SecondDecoder();
            decoders.Register(first);
            decoders.Register(second);
            Assert.That(decoders.EnumerateDecoder(), Is.EqualTo(explicitInitialization
                ? new IDecoderInfo[] { second, first } : [first, second]));

        }
        finally { BeutlHomeIsolation.End(); }
    }

    private class FirstDecoder : IDecoderInfo
    {
        public string Name => GetType().Name;
        public MediaReader? Open(string file, MediaOptions options) => null;
        public virtual IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [];
    }

    private sealed class SecondDecoder : FirstDecoder
    {
        public override IEnumerable<string> VideoExtensions() => [".second"];
    }
}
