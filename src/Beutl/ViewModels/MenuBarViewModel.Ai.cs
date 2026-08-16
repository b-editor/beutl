using System.Diagnostics.CodeAnalysis;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(
        nameof(ShowAiJobs),
        nameof(GenerateImage),
        nameof(GenerateSubtitles),
        nameof(EditImage),
        nameof(GenerateVideo))]
    private void InitializeAiCommands(IObservable<bool> isSceneOpened)
    {
        ShowAiJobs = new ReactiveCommandSlim();
        GenerateImage = new ReactiveCommandSlim(isSceneOpened);
        GenerateSubtitles = new ReactiveCommandSlim(isSceneOpened);
        EditImage = new ReactiveCommandSlim(isSceneOpened);
        GenerateVideo = new ReactiveCommandSlim(isSceneOpened);
    }

    // AI
    //    Generate image
    //    Generate subtitles
    //    Edit image
    //    Generate video
    public ReactiveCommandSlim ShowAiJobs { get; private set; } = null!;

    public ReactiveCommandSlim GenerateImage { get; private set; } = null!;

    public ReactiveCommandSlim GenerateSubtitles { get; private set; } = null!;

    public ReactiveCommandSlim EditImage { get; private set; } = null!;

    public ReactiveCommandSlim GenerateVideo { get; private set; } = null!;
}
