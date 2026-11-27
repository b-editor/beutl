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
    public void Ducking_before_time_stretch_keeps_the_music_lowered_across_composition_blocks()
    {
        using var fixture = new WaveScene(first: _ => 0.40f, second: _ => 0f);
        var effects = new AudioEffectGroup();
        fixture.Music.Effect.Value = effects;
        effects.Children.Add(fixture.Ducking);
        effects.Children.Add(new TimeStretchEffect { Speed = { Value = 200f } });

        var (chunked, _) = RenderPreview(fixture.Scene);
        var (whole, _) = RenderPreview(fixture.Scene, blockSize: Frames(DurationMs));

        // A 200% stretch makes the music end at 640 ms. Check its steady level across several blocks
        // after the attack and before the stretched WAV ends.
        var steadyMusic = chunked[Frames(MusicStartMs + AttackMs)..Frames(600)];
        Assert.Multiple(() =>
        {
            Assert.That(steadyMusic.Min(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(steadyMusic.Max(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
            AssertBits(chunked, whole);
        });
    }

    [AvaloniaTest]
    public void Ducking_before_pitch_shift_keeps_the_music_lowered_across_composition_blocks()
    {
        using var fixture = new WaveScene(first: _ => 0.40f, second: _ => 0f);
        var effects = new AudioEffectGroup();
        fixture.Music.Effect.Value = effects;
        effects.Children.Add(fixture.Ducking);
        effects.Children.Add(new PitchShiftEffect { Semitones = { Value = 12f } });

        var (chunked, _) = RenderPreview(fixture.Scene);
        var (whole, _) = RenderPreview(fixture.Scene, blockSize: Frames(DurationMs));

        var steadyMusic = chunked[Frames(MusicStartMs + AttackMs)..Frames(900)];
        Assert.Multiple(() =>
        {
            Assert.That(steadyMusic.Min(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(steadyMusic.Max(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
            AssertBits(chunked, whole);
        });
    }

    [AvaloniaTest]
    [TestCase(SampleRate, null)]
    [TestCase(44100, null)]
    [TestCase(SampleRate, 200f)]
    [TestCase(SampleRate, 100f)]
    public void Ducking_keeps_its_order_before_a_following_limiter(int musicRate, float? stretchSpeed)
    {
        using var fixture = new WaveScene(first: _ => 0.40f, second: _ => 0f, musicRate: musicRate);
        // An immediate attack isolates effect ordering from the limiter's release envelope.
        fixture.Ducking.Attack.Value = 0f;
        var effects = new AudioEffectGroup();
        fixture.Music.Effect.Value = effects;
        var nested = new AudioEffectGroup();
        effects.Children.Add(nested);
        nested.Children.Add(fixture.Ducking);
        if (stretchSpeed == 200f)
            nested.Children.Add(new TimeStretchEffect { Speed = { Value = stretchSpeed.Value } });

        const float limiterThresholdDb = -12f;
        effects.Children.Add(new LimiterEffect { Threshold = { Value = limiterThresholdDb } });
        if (stretchSpeed == 100f)
            effects.Children.Add(new TimeStretchEffect { Speed = { Value = stretchSpeed.Value } });
        var (duckingFirst, _) = RenderPreview(fixture.Scene);

        effects.Children.Value.Move(1, 0);
        var (limiterFirst, _) = RenderPreview(fixture.Scene);

        // Full ducking is below the ceiling. In the reverse order the limiter reduces the dry music first.
        float ceiling = MathF.Pow(10, limiterThresholdDb / 20);
        Assert.Multiple(() =>
        {
            Assert.That(duckingFirst[Frames(300)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(limiterFirst[Frames(300)], Is.EqualTo(ceiling * (s_duckedLevel / MusicLevel)).Within(1e-6f));
        });
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
    public async Task Grouped_source_layer_ducks_in_preview_and_export()
    {
        using var fixture = new WaveScene(index => FirstBurst(index) + SecondBurst(index), groupNarration: true);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.FirstVoiceLayer);
        var (left, right) = RenderPreview(fixture.Scene);

        using var exporter = ExportRendererFactory.CreateComposer(fixture.Scene, TestShell.FontManager, SampleRate, TestShell.Decoders);
        using var progress = new Subject<MediaTime>();
        using var provider = new SampleProviderImpl(fixture.Scene, exporter, SampleRate, progress, AudioChannelLayout.Stereo);
        using var exported = await provider.ReadNextSamples(Frames(DurationMs));

        Assert.That(exported, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(left[Frames(209)], Is.EqualTo(MusicLevel));
            Assert.That(left[Frames(210 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(440 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(left[Frames(710 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(960 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(right[Frames(300)], Is.EqualTo(0.40f));
            Assert.That(right[Frames(800)], Is.EqualTo(0.35f));
        });
        AssertBits(exported!.GetChannelData(0).ToArray(), left);
        AssertBits(exported.GetChannelData(1).ToArray(), right);
    }

    [AvaloniaTest]
    public void Selecting_the_group_layer_still_ducks_the_music()
    {
        using var fixture = new WaveScene(index => FirstBurst(index) + SecondBurst(index), groupNarration: true);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.GroupLayer!);
        var (left, _) = RenderPreview(fixture.Scene);

        Assert.Multiple(() =>
        {
            Assert.That(left[Frames(210 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(440 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(left[Frames(710 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(960 + ReleaseMs)], Is.EqualTo(MusicLevel));
        });
    }

    [AvaloniaTest]
    public async Task A_group_starting_later_than_the_scene_ducks_in_preview_and_export()
    {
        // The group and its narration start at 1 s, and 4096 frames does not divide the group start.
        using var fixture = new WaveScene(
            groupNarration: true, groupStartMs: 1000, narrationStartMs: 1000, durationMs: 1800);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.FirstVoiceLayer);
        var (left, right) = RenderPreview(fixture.Scene, blockSize: 4096, durationMs: 1800);

        using var exporter = ExportRendererFactory.CreateComposer(fixture.Scene, TestShell.FontManager, SampleRate, TestShell.Decoders);
        using var progress = new Subject<MediaTime>();
        using var provider = new SampleProviderImpl(fixture.Scene, exporter, SampleRate, progress, AudioChannelLayout.Stereo);
        using var exported = await provider.ReadNextSamples(Frames(1800));

        Assert.That(exported, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(left[Frames(1209)], Is.EqualTo(MusicLevel));
            Assert.That(left[Frames(1210 + AttackMs)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(1440 + ReleaseMs)], Is.EqualTo(MusicLevel));
            Assert.That(right[Frames(1300)], Is.EqualTo(0.40f));
        });
        AssertBits(exported!.GetChannelData(0).ToArray(), left);
        AssertBits(exported.GetChannelData(1).ToArray(), right);
    }

    [AvaloniaTest]
    public void A_group_that_passes_a_child_outside_its_range_still_ducks_on_both_sides()
    {
        // The narration [900, 2100] extends before and after the group [1000, 1800]. Its bursts fall in the
        // left pass-through, the group's own range, and the right pass-through in the block around 1.8 s.
        using var fixture = new WaveScene(
            index => Burst(index, 0, 100, 0.40f) + Burst(index, 700, 800, 0.30f) + Burst(index, 900, 950, 0.35f),
            _ => 0f,
            groupNarration: true,
            groupStartMs: 1000,
            groupLengthMs: 800,
            narrationStartMs: 900,
            durationMs: 2100);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.FirstVoiceLayer);
        var (left, _) = RenderPreview(fixture.Scene, blockSize: 4096, durationMs: 2100);

        Assert.Multiple(() =>
        {
            Assert.That(left[Frames(990)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(1640)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(left[Frames(1840)], Is.EqualTo(s_duckedLevel).Within(1e-6f));
        });
    }

    [AvaloniaTest]
    [TestCase(5760)]
    [TestCase(4096)]
    public void A_group_that_reads_ahead_keeps_ducking_across_the_block(int blockSize)
    {
        using var fixture = new WaveScene(groupNarration: true);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.FirstVoiceLayer);
        fixture.Group!.Effect.Value = new PitchShiftEffect { Semitones = { Value = 12f } };
        var (left, _) = RenderPreview(fixture.Scene, blockSize: blockSize);

        var steady = left[Frames(210 + AttackMs)..Frames(440)];
        Assert.Multiple(() =>
        {
            Assert.That(steady.Min(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(steady.Max(), Is.EqualTo(s_duckedLevel).Within(1e-6f));
        });
    }

    [AvaloniaTest]
    public void Replaying_a_block_after_silencing_the_grouped_child_does_not_keep_ducking()
    {
        using var fixture = new WaveScene(groupNarration: true);
        fixture.SecondVoiceLayer.IsAudioMuted.Value = true;
        fixture.Ducking.SourceLayers.Clear();
        fixture.Ducking.SourceLayers.Add(fixture.FirstVoiceLayer);
        using var composer = new SceneComposer(fixture.Scene, new SceneRenderServices(TestShell.FontManager, TestShell.Decoders))
        {
            SampleRate = SampleRate,
            ChannelLayout = AudioChannelLayout.Stereo,
        };
        // The leading block leaves a loud peak segment that overlaps the start of the block under test.
        var leading = new TimeRange(MediaTime.Zero, MediaTime.FromSamples(12500, SampleRate));
        const int blockStart = 10500;
        const int probe = 1950; // block frame 10500 + 1950 = 12450, 259.375 ms
        var block = new TimeRange(MediaTime.FromSamples(blockStart, SampleRate),
            MediaTime.FromSamples(6500, SampleRate));

        using AudioBuffer seed = composer.Compose(leading)!;
        using AudioBuffer loud = composer.Compose(block)!;
        float loudLevel = loud.GetChannelData(0)[probe];

        // The same tap and composer see the block again, now with a silent child.
        fixture.FirstVoice.Gain.Value = 0f;
        using AudioBuffer silent = composer.Compose(block)!;
        float silentLevel = silent.GetChannelData(0)[probe];

        Assert.Multiple(() =>
        {
            Assert.That(loudLevel, Is.EqualTo(s_duckedLevel).Within(1e-6f));
            Assert.That(silentLevel, Is.EqualTo(MusicLevel));
        });
    }

    [AvaloniaTest]
    public void A_group_without_ducking_preserves_the_wav_samples_and_mix_bits()
    {
        using var fixture = new WaveScene(groupNarration: true);
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

    private static (float[] Left, float[] Right) RenderPreview(Scene scene, int blockSize = 5760, int durationMs = DurationMs)
    {
        using var composer = new SceneComposer(scene, new SceneRenderServices(TestShell.FontManager, TestShell.Decoders))
        {
            SampleRate = SampleRate,
            ChannelLayout = AudioChannelLayout.Stereo,
        };
        var left = new float[Frames(durationMs)];
        var right = new float[left.Length];
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

        public WaveScene(
            Func<int, float>? first = null, Func<int, float>? second = null, int musicRate = SampleRate,
            bool groupNarration = false, int groupStartMs = 0, int groupLengthMs = 0,
            int narrationStartMs = 0, int durationMs = DurationMs)
        {
            TestShell.Decoders.Register(_decoder);
            try
            {
                Scene = new Scene(320, 180, "Ducking")
                {
                    Uri = new Uri(Path.Combine(_directory.FullName, "ducking.bep")),
                    Duration = { Value = Time(durationMs) },
                    FrameRate = { Value = new Rational(50) },
                    SampleRate = { Value = SampleRate },
                };
                MusicLayer = AddLayer(0, "Music");
                if (groupNarration)
                {
                    GroupLayer = AddLayer(1, "Group");
                    FirstVoiceLayer = AddLayer(2, "Narration");
                    SecondVoiceLayer = AddLayer(3, "Interview");
                }
                else
                {
                    FirstVoiceLayer = AddLayer(1, "Narration");
                    SecondVoiceLayer = AddLayer(2, "Interview");
                }
                Music = AddClip(MusicLayer, MusicStartMs, _ => (MusicLevel, 0f), musicRate, durationMs);
                FirstVoice = AddClip(FirstVoiceLayer, groupNarration ? narrationStartMs : 0,
                    index => (0f, (first ?? FirstBurst)(index)), endMs: durationMs);
                SecondVoice = AddClip(SecondVoiceLayer, 0, index => (0f, (second ?? SecondBurst)(index)), endMs: durationMs);
                if (groupNarration)
                {
                    Group = new SoundGroup();
                    AddGroup(GroupLayer!, Group, groupStartMs,
                        groupLengthMs > 0 ? groupLengthMs : durationMs - groupStartMs);
                }
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
        public TimelineLayer? GroupLayer { get; }
        public TimelineLayer FirstVoiceLayer { get; }
        public TimelineLayer SecondVoiceLayer { get; }
        public SoundGroup? Group { get; }
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

        // The group's paired portal pulls the next layer's sound out of the top-level mix.
        private void AddGroup(TimelineLayer layer, SoundGroup group, int startMs, int lengthMs)
        {
            var element = new Element
            {
                Uri = new Uri(Path.Combine(_directory.FullName, "Group.belm")),
                ZIndex = { Value = layer.ZIndex.Value },
                Start = { Value = Time(startMs) },
                Length = { Value = Time(lengthMs) },
            };
            element.AddObject(group);
            ((PortalObject)element.Objects[0]).Count.Value = 1;
            Scene.Children.Add(element);
        }

        private SourceSound AddClip(
            TimelineLayer layer, int startMs, Func<int, (float Left, float Right)> sample, int rate = SampleRate,
            int endMs = DurationMs)
        {
            int lengthMs = endMs - startMs;
            string name = layer.Name.Value;
            string path = Path.Combine(_directory.FullName, name + ".wav");
            var samples = new float[lengthMs * rate / 1000 * 2];
            for (int i = 0; i < samples.Length / 2; i++)
                (samples[2 * i], samples[2 * i + 1]) = sample(i);

            using (var wave = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2)))
                wave.WriteSamples(samples, 0, samples.Length);

            var clip = new Element
            {
                Uri = new Uri(Path.Combine(_directory.FullName, name + ".belm")),
                ZIndex = { Value = layer.ZIndex.Value },
                Start = { Value = Time(startMs) },
                Length = { Value = Time(lengthMs) },
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
