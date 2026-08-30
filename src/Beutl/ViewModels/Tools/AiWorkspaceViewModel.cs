using System.Text.Json.Nodes;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels.Dialogs;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;
using Icon = FluentIcons.Common.Icon;

namespace Beutl.ViewModels.Tools;

/// <summary>Identifies one page of the AI tool tab, and is what a saved dock layout remembers.</summary>
internal enum AiWorkspaceSection
{
    ImageGeneration,
    ImageEdit,
    VideoGeneration,
    Subtitles,
    Jobs,
}

/// <summary>
/// One page of an AI tool tab. Its view model is built the first time the page
/// is shown and kept afterwards, so a half-written prompt survives a look at
/// something else while a page nobody opens never talks to the server. Pages
/// belong to their own tab: a second tab is a second workbench, not a mirror.
/// </summary>
internal sealed class AiWorkspaceSectionViewModel : IAsyncDisposable
{
    private readonly Func<IAsyncDisposable> _create;
    private readonly object _disposeGate = new();
    private IAsyncDisposable? _content;
    private Task? _disposeTask;
    private bool _disposed;

    internal AiWorkspaceSectionViewModel(
        AiWorkspaceSection id,
        string displayName,
        Icon icon,
        Func<IAsyncDisposable> create)
    {
        Id = id;
        DisplayName = displayName;
        Icon = icon;
        _create = create;
    }

    public AiWorkspaceSection Id { get; }

    public string DisplayName { get; }

    public Icon Icon { get; }

    internal object? Content => _content;

    internal object EnsureContent()
    {
        lock (_disposeGate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _content ??= _create();
        }
    }

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _disposed = true;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return completion.Task;
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        IAsyncDisposable? content;
        lock (_disposeGate)
        {
            content = _content;
            _content = null;
        }

        if (content is not null)
            await content.DisposeAsync();
    }
}

/// <summary>
/// An AI tool tab. The five AI workflows used to be five dock tabs, which pushed
/// everything else off the tab strip; they are pages of this one tab now, and a
/// second tab can be opened when two of them are needed at once.
/// </summary>
internal sealed class AiWorkspaceViewModel : IToolContext, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly EditViewModel _editViewModel;
    private readonly AiWorkspaceSectionViewModel[] _sections;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;
    private bool _disposed;

    internal AiWorkspaceViewModel(
        EditViewModel editViewModel,
        Func<AiWorkspaceSection, IAsyncDisposable> createPage)
    {
        ArgumentNullException.ThrowIfNull(editViewModel);
        ArgumentNullException.ThrowIfNull(createPage);

        _editViewModel = editViewModel;

        // Making comes first and the history reads back over it, so the pages run
        // in that order rather than in the order the menu lists them.
        _sections =
        [
            Section(AiWorkspaceSection.ImageGeneration, Strings.AiImageGeneration, Icon.SparkleCircle),
            Section(AiWorkspaceSection.ImageEdit, Strings.AiImageEdit, Icon.ImageEdit),
            Section(AiWorkspaceSection.VideoGeneration, Strings.AiVideoGeneration, Icon.Video),
            Section(AiWorkspaceSection.Subtitles, Strings.AiSubtitle, Icon.Subtitles),
            Section(AiWorkspaceSection.Jobs, Strings.AiJobCenter, Icon.History),
        ];

        SelectedSection = new ReactivePropertySlim<AiWorkspaceSectionViewModel?>(_sections[0])
            .DisposeWith(_disposables);

        // The selector can report null for an instant while its items are handed
        // over; keeping the last page on screen through that avoids a blank flash.
        IObservable<AiWorkspaceSectionViewModel> shown =
            SelectedSection.Where(section => section is not null).Select(section => section!);

        ActiveContent = shown
            .Select(section => section.EnsureContent())
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        // A page is built once and kept, so coming back to it does not reload
        // the model list the way opening it the first time did. Without this, a
        // tab left in the foreground goes on offering models an operator has
        // since withdrawn for as long as the tab lives.
        ActiveContent
            .Subscribe(content => (content as IAiModelListConsumer)?.RefreshModels())
            .DisposeWith(_disposables);

        // Two AI tabs side by side are told apart by the page each one is on,
        // which is why the dock tab is named after the page rather than "AI".
        Header = shown
            .Select(section => section.DisplayName)
            .ToReadOnlyReactivePropertySlim(Strings.Ai)
            .DisposeWith(_disposables);

        AiWorkspaceSectionViewModel Section(AiWorkspaceSection id, string displayName, Icon icon)
            => new(id, displayName, icon, () => createPage(id));
    }

    public ToolTabExtension Extension => AiWorkspaceTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; }

    internal IReadOnlyList<AiWorkspaceSectionViewModel> Sections => _sections;

    internal ReactivePropertySlim<AiWorkspaceSectionViewModel?> SelectedSection { get; }

    public ReadOnlyReactivePropertySlim<object?> ActiveContent { get; }

    /// <summary>
    /// Brings a page to the front of this tab and hands back its view model,
    /// building it if this is the first look at that page.
    /// </summary>
    internal object Show(AiWorkspaceSection section)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        AiWorkspaceSectionViewModel target = _sections.First(item => item.Id == section);
        SelectedSection.Value = target;
        return target.EnsureContent();
    }

    public object? GetService(Type serviceType)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        return serviceType.IsInstanceOfType(this)
            ? this
            : _editViewModel.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);

        // Which page, and nothing from inside it: prompts and generated content
        // are deliberately kept out of the persisted dock layout.
        if (json["section"] is JsonValue value
            && value.TryGetValue(out string? name)
            && Enum.TryParse(name, out AiWorkspaceSection section))
        {
            Show(section);
        }
    }

    public void WriteToJson(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (SelectedSection.Value is { } section)
        {
            json["section"] = section.Id.ToString();
        }
    }

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            _disposed = true;
            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return completion.Task;
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        _disposables.Dispose();
        IsSelected.Dispose();
        Task[] disposals = _sections
            .Select(section => section.DisposeAsync().AsTask())
            .ToArray();
        await Task.WhenAll(disposals);
    }
}
