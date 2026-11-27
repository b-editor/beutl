using System.Reactive.Subjects;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor.Services;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.Models;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using NAudio.Wave;
using AudioBuffer = Beutl.Audio.AudioBuffer;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class DuckingAudioTests
{
    private const int SampleRate = 48000;
    private const int DurationMs = 1200;
    private const int MusicStartMs = 80;
    private const float MusicLevel = 0.5f;
    private const float ReductionDb = 12f;
    private const int AttackMs = 40;
    private const int ReleaseMs = 100;
    private static readonly float s_duckedLevel = MusicLevel * MathF.Pow(10, -ReductionDb / 20);

    [AvaloniaTest]
    public void Both_source_layers_reach_the_reduction_and_recover_within_the_configured_times()
    {
        using var fixture = new WaveScene();
        fixture.FirstVoice.FindRequiredHierarchicalParent<Element>().Length.Value = Time(440);
        fixture.SecondVoice.FindRequiredHierarchicalParent<Element>().Length.Value = Time(960);
        var (music, voice) = RenderPreview(fixture.Scene);

        Assert.Multiple(() =>
        {
            Assert.That(music[Frames(209)], Is.EqualTo(MusicLevel));
            Assert.That(music[Frames(230)], Is.InRange(s_duckedLevel + 0.01f, MusicLevel - 0.01f));
            Assert.That(music[Frames(210 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(music[Frames(490)], Is.InRange(s_duckedLevel + 0.01f, MusicLevel - 0.01f));
            Assert.That(music[Frames(440 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(music[Frames(710 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(music[Frames(960 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(voice[Frames(210)], Is.EqualTo(0.40f));
            Assert.That(voice[Frames(710)], Is.EqualTo(0.35f));
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void Muted_or_solo_excluded_sources_are_silent_and_do_not_trigger(bool solo)
    {
        using var fixture = new WaveScene();
        if (solo)
        {
            fixture.MusicLayer.IsSolo.Value = true;
            fixture.FirstVoiceLayer.IsSolo.Value = true;
        }
        else
        {
            fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        }

        var (music, voice) = RenderPreview(fixture.Scene);
        Assert.Multiple(() =>
        {
            Assert.That(music[Frames(300)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(music[Frames(800)], Is.EqualTo(MusicLevel));
            Assert.That(voice[Frames(800)], Is.Zero);
        });
    }

    [AvaloniaTest]
    public void Detector_uses_the_loudest_layer_without_cross_layer_cancellation_or_addition()
    {
        using var fixture = new WaveScene(
            index => Burst(index, 210, 440, 0.40f) + Burst(index, 710, 960, 0.12f),
            index => Burst(index, 210, 440, -0.40f) + Burst(index, 710, 960, 0.12f));
        var (music, voice) = RenderPreview(fixture.Scene);

        Assert.Multiple(() =>
        {
            Assert.That(voice[Frames(300)], Is.Zero);
            Assert.That(music[Frames(300)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(voice[Frames(800)], Is.EqualTo(0.24f));
            Assert.That(music[Frames(800)], Is.EqualTo(MusicLevel));
        });
    }

    [AvaloniaTest]
    public void Cyclic_and_self_sources_are_ignored_and_an_independent_source_still_triggers()
    {
        using var fixture = new WaveScene();
        fixture.Ducking.SourceLayers.Add(fixture.MusicLayer);
        var reverse = new DuckingEffect { Attack = { Value = 0 }, Release = { Value = 0 } };
        reverse.SourceLayers.Add(fixture.MusicLayer);
        fixture.FirstVoice.Effect.Value = reverse;

        var (music, voice) = RenderPreview(fixture.Scene);
        Assert.Multiple(() =>
        {
            Assert.That(music[Frames(300)], Is.EqualTo(MusicLevel));
            Assert.That(voice[Frames(300)], Is.EqualTo(0.40f));
            Assert.That(music[Frames(800)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(music[Frames(1100)], Is.EqualTo(MusicLevel));
        });
    }

    [AvaloniaTest]
    [TestCase(SampleRate)]
    [TestCase(44100)]
    public async Task Preview_and_export_match_bit_for_bit_with_different_composition_blocks(int musicRate)
    {
        // The burst straddles the one-second export block and a different preview boundary.
        using var fixture = new WaveScene(second: index => Burst(index, 890, 1100, 0.35f), musicRate: musicRate);
        var (left, right) = RenderPreview(fixture.Scene);

        using var exporter = ExportRendererFactory.CreateComposer(fixture.Scene, TestShell.FontManager, SampleRate, TestShell.Decoders);
        using var progress = new Subject<MediaTime>();
        using var provider = new SampleProviderImpl(fixture.Scene, exporter, SampleRate, progress, AudioChannelLayout.Stereo);
        using var exported = await provider.ReadNextSamples(Frames(DurationMs));

        Assert.That(exported, Is.Not.Null);
        AssertBits(exported!.GetChannelData(0).ToArray(), left);
        AssertBits(exported.GetChannelData(1).ToArray(), right);
    }

    [AvaloniaTest]
    public void A_scene_without_ducking_preserves_the_wav_samples_and_mix_bits()
    {
        using var fixture = new WaveScene();
        fixture.Music.Effect.Value = null;
        var (left, right) = RenderPreview(fixture.Scene);

        AssertBits(left, Enumerable.Range(0, Frames(DurationMs)).Select(index => index >= Frames(MusicStartMs) ? MusicLevel : 0f));
        AssertBits(right, Enumerable.Range(0, Frames(DurationMs))
            .Select(index => FirstBurst(index) + SecondBurst(index)));
    }

    [AvaloniaTest]
    public void Saved_layer_references_survive_reload_and_moving_the_source_layers()
    {
        using var fixture = new WaveScene();
        using var history = new PropertyEditorTestHost.HistoryScope(fixture.Scene);
        var move = new LayerMoveService(history.History);
        move.ApplyMove(fixture.Scene, 1, 4, [fixture.Scene.Children.Single(element => element.Objects.Contains(fixture.FirstVoice))]);
        Assert.That(fixture.FirstVoiceLayer.ZIndex.Value, Is.EqualTo(4));

        Uri path = fixture.Scene.Uri!;
        CoreSerializer.StoreToUri(fixture.Scene, path, CoreSerializationMode.Write | CoreSerializationMode.SaveReferencedObjects);
        var restored = CoreSerializer.RestoreFromUri<Scene>(path);
        var effect = restored.EnumerateAllChildren<DuckingEffect>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(effect.SourceLayers.Select(source => source.Id),
                Is.EqualTo(new[] { fixture.FirstVoiceLayer.Id.Value, fixture.SecondVoiceLayer.Id.Value }));
            Assert.That(restored.Layers.Single(layer => layer.Id.Value == effect.SourceLayers[0].Id).ZIndex.Value, Is.EqualTo(4));
            Assert.That(effect.Threshold.Value, Is.EqualTo(-16f));
            Assert.That(effect.Reduction.Value, Is.EqualTo(ReductionDb));
            Assert.That(effect.Attack.Value, Is.EqualTo(AttackMs));
            Assert.That(effect.Release.Value, Is.EqualTo(ReleaseMs));
        });

        var (left, _) = RenderPreview(restored);
        Assert.That(left[Frames(300)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
        Assert.That(left[Frames(800)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
    }

    private static (float[] Left, float[] Right) RenderPreview(Scene scene)
    {
        using var composer = new SceneComposer(scene, new SceneRenderServices(TestShell.FontManager, TestShell.Decoders))
        {
            SampleRate = SampleRate,
            ChannelLayout = AudioChannelLayout.Stereo,
        };
        var left = new float[Frames(DurationMs)];
        var right = new float[left.Length];
        const int blockSize = 5760;
        for (int start = 0; start < left.Length; start += blockSize)
        {
            int count = Math.Min(blockSize, left.Length - start);
            using AudioBuffer audio = composer.Compose(new TimeRange(
                MediaTime.FromSamples(start, SampleRate), MediaTime.FromSamples(count, SampleRate)))!;
            audio.GetChannelData(0).CopyTo(left.AsSpan(start, count));
            audio.GetChannelData(1).CopyTo(right.AsSpan(start, count));
        }
        return (left, right);
    }

    private static void AssertBits(IEnumerable<float> actual, IEnumerable<float> expected)
        => Assert.That(actual.Select(BitConverter.SingleToInt32Bits), Is.EqualTo(expected.Select(BitConverter.SingleToInt32Bits)));

    private static int Frames(int milliseconds) => milliseconds * (SampleRate / 1000);

    private static MediaTime Time(int milliseconds) => MediaTime.FromSamples(Frames(milliseconds), SampleRate);

    private static float FirstBurst(int index) => Burst(index, 210, 440, 0.40f);

    private static float SecondBurst(int index) => Burst(index, 710, 960, 0.35f);

    private static float Burst(int index, int startMs, int endMs, float level)
        => index >= Frames(startMs) && index < Frames(endMs)
            ? level * (float)Math.Cos(2 * Math.PI * 1000 * (index - Frames(startMs)) / SampleRate)
            : 0f;

    private sealed class WaveScene : IDisposable
    {
        private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("beutl-ducking-");
        private readonly WaveDecoderInfo _decoder = new();

        public WaveScene(Func<int, float>? first = null, Func<int, float>? second = null, int musicRate = SampleRate)
        {
            TestShell.Decoders.Register(_decoder);
            try
            {
                Scene = new Scene(320, 180, "Ducking")
                {
                    Uri = new Uri(Path.Combine(_directory.FullName, "ducking.bep")),
                    Duration = { Value = Time(DurationMs) },
                    FrameRate = { Value = new Rational(50) },
                    SampleRate = { Value = SampleRate },
                };
                MusicLayer = AddLayer(0, "Music");
                FirstVoiceLayer = AddLayer(1, "Narration");
                SecondVoiceLayer = AddLayer(2, "Interview");
                Music = AddClip(MusicLayer, MusicStartMs, _ => (MusicLevel, 0f), musicRate);
                FirstVoice = AddClip(FirstVoiceLayer, 0, index => (0f, (first ?? FirstBurst)(index)));
                SecondVoice = AddClip(SecondVoiceLayer, 0, index => (0f, (second ?? SecondBurst)(index)));
                Ducking = new DuckingEffect
                {
                    Threshold = { Value = -16f },
                    Reduction = { Value = ReductionDb },
                    Attack = { Value = AttackMs },
                    Release = { Value = ReleaseMs },
                };
                Ducking.SourceLayers.Add(FirstVoiceLayer);
                Ducking.SourceLayers.Add(SecondVoiceLayer);
                Music.Effect.Value = Ducking;
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        public Scene Scene { get; }
        public TimelineLayer MusicLayer { get; }
        public TimelineLayer FirstVoiceLayer { get; }
        public TimelineLayer SecondVoiceLayer { get; }
        public SourceSound Music { get; }
        public SourceSound FirstVoice { get; }
        public SourceSound SecondVoice { get; }
        public DuckingEffect Ducking { get; }

        private TimelineLayer AddLayer(int index, string name)
        {
            var layer = new TimelineLayer { ZIndex = { Value = index }, Name = { Value = name } };
            Scene.Layers.Add(layer);
            return layer;
        }

        private SourceSound AddClip(TimelineLayer layer, int startMs, Func<int, (float Left, float Right)> sample, int rate = SampleRate)
        {
            string name = layer.Name.Value;
            string path = Path.Combine(_directory.FullName, name + ".wav");
            var samples = new float[(DurationMs - startMs) * rate / 1000 * 2];
            for (int i = 0; i < samples.Length / 2; i++)
                (samples[2 * i], samples[2 * i + 1]) = sample(i);

            using (var wave = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)))
                wave.WriteSamples(samples, 0, samples.Length);

            var clip = new Element
            {
                Uri = new Uri(Path.Combine(_directory.FullName, name + ".belm")),
                ZIndex = { Value = layer.ZIndex.Value },
                Start = { Value = Time(startMs) },
                Length = { Value = Time(DurationMs - startMs) },
            };
            var sound = new SourceSound { Source = { Value = SoundSource.Open(path) } };
            clip.Objects.Add(sound);
            Scene.Children.Add(clip);
            return sound;
        }

        public void Dispose()
        {
            TestShell.Decoders.Unregister(_decoder);
            _directory.Delete(recursive: true);
        }
    }
}
