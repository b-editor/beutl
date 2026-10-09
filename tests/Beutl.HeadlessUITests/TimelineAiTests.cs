using System.Text;
using Avalonia.Headless.NUnit;
using Beutl.Audio;
using Beutl.Editor.Components.TimelineTab.Generative;
using Beutl.Editor.Services;
using Beutl.Editor.Services.AI;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.NodeGraph.Generative;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;
using Beutl.ViewModels;
using Beutl.ViewModels.Dialogs;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TimelineAiTests
{
    private static async Task<EditViewModel> OpenEditor(string name)
    {
        string workspace = Path.Combine(BeutlHomeIsolation.CurrentHome!, name);
        Directory.CreateDirectory(workspace);
        Project project = (await TestShell.Project.CreateProject(
            640, 480, 30, 44100, name, workspace))!;
        Scene scene = project.Items.OfType<Scene>().First();
        TestShell.Editor.ActivateTabItem(scene);
        HeadlessTestHelpers.Settle();
        return (EditViewModel)TestShell.Editor.SelectedTabItem.Value!.Context.Value!;
    }

    [AvaloniaTest]
    public async Task TheEditorOffersTimelineGenerationWithoutTheMainWindow()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("timeline-ai-services");

        Assert.Multiple(() =>
        {
            Assert.That(editor.GetService<TimelineGenerationService>(), Is.Not.Null);
            Assert.That(
                editor.GetService<TimelineGenerationService>(),
                Is.SameAs(editor.GetService<TimelineGenerationService>()));
        });
    }

    [AvaloniaTest]
    public async Task AResultIsAddedThroughTheEditorsAdderAsOneUndoableStep()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("timeline-ai-adder");
        Scene scene = editor.Scene;
        string resources = Beutl.Services.AI.AiResultImporter.GetResourceDirectory(scene);
        Directory.CreateDirectory(resources);
        string original = WritePng(resources, 200, 100);
        string square = WritePng(resources, 100, 100);

        var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(4), ZIndex = 0 };
        var image = new SourceImage();
        image.Source.CurrentValue = ImageSource.Open(original);
        element.AddObject(image);
        scene.AddChild(element);
        editor.HistoryManager.Commit("image");

        using var service = new TimelineGenerationService(
            scene,
            editor.HistoryManager,
            editor.GetRequiredService<IElementAdder>(),
            () => new FixedExecutor(square),
            () => resources);
        TimelineGenerationJob job = service.Create(
            TimelineGenerationTarget.ForElement(TimelineGenerationPlacement.ReplaceImage, element),
            new TimelineGenerationSpec { Kind = TimelineGenerationKind.ImageEdit, ImageTask = AiImageEditTask.Restyle, Prompt = "ink" },
            new TimelineGenerationInputs { ImagePath = original });
        await service.RunAsync(job);
        HeadlessTestHelpers.Settle();

        Element added = scene.Children.Single(child => !ReferenceEquals(child, element));
        using (Assert.EnterMultipleScope())
        {
            Assert.That(job.Error.Value, Is.Null);
            Assert.That(added.ZIndex, Is.EqualTo(1));
            Assert.That(added.Uri, Is.Not.Null, "the adder gives it a file of its own");
            Assert.That(added.Generation?.ActiveTake?.Image?.Uri.LocalPath, Is.EqualTo(square));
        }

        Assert.That(editor.HistoryManager.Undo(), Is.True);
        Assert.That(scene.Children, Has.Count.EqualTo(1));
    }

    [AvaloniaTest]
    public async Task SubtitlesForAClipSelectTheSoundItPlays()
    {
        await TestReset.ResetShellAsync();
        EditViewModel editor = await OpenEditor("timeline-ai-subtitles");
        Scene scene = editor.Scene;
        string wave = Path.Combine(Path.GetDirectoryName(scene.Uri!.LocalPath)!, "voice.wav");
        WritePcmWave(wave, 16000, 16000);
        var element = new Element { Start = TimeSpan.Zero, Length = TimeSpan.FromSeconds(1), ZIndex = 2, Name = "Voice" };
        var sound = new SourceSound();
        sound.Source.CurrentValue = SoundSource.Open(wave);
        element.AddObject(sound);
        scene.AddChild(element);
        using AiSubtitleDialogViewModel viewModel = TestShell.MainViewModel.CreateAiSubtitleToolViewModel(editor);

        bool selected = viewModel.TrySelectAudioSource(element.Id);

        Assert.That(selected, Is.True);
        Assert.That(viewModel.SelectedAudioSource.Value?.FilePath, Is.EqualTo(wave));
        Assert.That(viewModel.TrySelectAudioSource(Guid.NewGuid()), Is.False);
        Assert.That(viewModel.SelectedAudioSource.Value?.FilePath, Is.EqualTo(wave), "an unknown element changes nothing");
    }

    private static string WritePng(string directory, int width, int height)
    {
        string path = Path.Combine(directory, $"{Guid.NewGuid():N}.png");
        using var bitmap = new Bitmap(width, height);
        Assert.That(bitmap.Save(path, EncodedImageFormat.Png), Is.True);
        return path;
    }

    private static void WritePcmWave(string path, int sampleRate, int sampleCount)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        int dataLength = checked(sampleCount * sizeof(short));
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataLength);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(sampleRate);
        writer.Write(sampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataLength);
        for (int index = 0; index < sampleCount; index++)
            writer.Write((short)0);
    }

    private sealed class FixedExecutor(string result) : IGenerativeNodeExecutor
    {
        public Task<GenerativeExecutionResult> ExecuteAsync(
            GenerativeRequest request,
            IProgress<GenerativeProgress> progress,
            CancellationToken cancellationToken)
            => Task.FromResult(new GenerativeExecutionResult(new Uri(result), "model", null));
    }
}
