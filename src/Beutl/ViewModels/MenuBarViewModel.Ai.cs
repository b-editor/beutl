using System.Diagnostics.CodeAnalysis;
using Reactive.Bindings;

namespace Beutl.ViewModels;

public partial class MenuBarViewModel
{
    [MemberNotNull(nameof(ShowAiJobs), nameof(GenerateImage))]
    private void InitializeAiCommands()
    {
        ShowAiJobs = new ReactiveCommandSlim();
        GenerateImage = new ReactiveCommandSlim();
        GenerateSubtitles = new ReactiveCommandSlim();
        EditImage = new ReactiveCommandSlim();
        GenerateVideo = new ReactiveCommandSlim();
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
