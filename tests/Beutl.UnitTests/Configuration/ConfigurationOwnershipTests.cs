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

    [Test, NonParallelizable]
    public void Reinitializing_decoder_priorities_detaches_the_old_section_and_subscribes_once()
    {
        var first = new ExtensionConfig();
        var second = new ExtensionConfig();
        var a = new FirstDecoder();
        var b = new SecondDecoder();
        int changes = 0;
        void Changed(object? sender, EventArgs e) => changes++;
        DecoderRegistry.Register(a);
        DecoderRegistry.Register(b);
        try
        {
            DecoderRegistry.Initialize(first);
            DecoderRegistry.DecodersChanged += Changed;
            first.DecoderPriority.Add(new(TypeFormat.ToString(typeof(SecondDecoder))));
            Assert.That(DecoderRegistry.EnumerateDecoder().First(), Is.SameAs(b));
            Assert.That(changes, Is.EqualTo(1));

            DecoderRegistry.Initialize(second);
            DecoderRegistry.Initialize(second);
            changes = 0;
            first.DecoderPriority.Clear();
            Assert.That(changes, Is.Zero);
            second.DecoderPriority.Add(new(TypeFormat.ToString(typeof(FirstDecoder))));
            Assert.That(DecoderRegistry.EnumerateDecoder().First(), Is.SameAs(a));
            Assert.That(changes, Is.EqualTo(1));
            second.DecoderPriority.Clear();
            Assert.That(changes, Is.EqualTo(2));
        }
        finally
        {
            DecoderRegistry.DecodersChanged -= Changed;
            DecoderRegistry.Unregister(a);
            DecoderRegistry.Unregister(b);
            DecoderRegistry.Initialize(new ExtensionConfig());
        }
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
            if (explicitInitialization)
            {
                DecoderRegistry.Initialize(decoderConfig);
            }

            var fonts = new FontManager(explicitInitialization ? fontConfig : new FontConfig());
            FontFamily[] families = fonts.FontFamilies.ToArray();
            Assert.That(families, Does.Contain(FontFamily.Default));
            Assert.That(fonts.IsRegistered(FontFamily.Default), Is.True);
            string[] expectedDirectories = [.. (explicitInitialization ? fontConfig : new FontConfig()).FontDirectories,
                BeutlEnvironment.GetMaterialsDirectoryPath()];
            Assert.That(fonts.FontDirectories, Is.EqualTo(expectedDirectories));

            var first = new FirstDecoder();
            var second = new SecondDecoder();
            DecoderRegistry.Register(first);
            DecoderRegistry.Register(second);
            Assert.That(DecoderRegistry.EnumerateDecoder(), Is.EqualTo(explicitInitialization
                ? new IDecoderInfo[] { second, first } : [first, second]));
            DecoderRegistry.Initialize(decoderConfig);
            DecoderRegistry.Initialize(decoderConfig);
            int changes = 0;
            DecoderRegistry.DecodersChanged += (_, _) => changes++;
            decoderConfig.DecoderPriority.Clear();
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(DecoderRegistry.EnumerateDecoder(), Is.EqualTo(new IDecoderInfo[] { first, second }));
        }
        finally { BeutlHomeIsolation.End(); }
    }

    private class FirstDecoder : IDecoderInfo
    {
        public string Name => GetType().Name;
        public MediaReader? Open(string file, MediaOptions options) => null;
        public IEnumerable<string> VideoExtensions() => [];
        public IEnumerable<string> AudioExtensions() => [];
    }

    private sealed class SecondDecoder : FirstDecoder;
}
