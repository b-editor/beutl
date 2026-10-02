using System.Reflection;
using System.Windows.Input;
using Beutl.Editor.Components.AudioVisualizerTab.ViewModels;
using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Components.ColorScopesTab.ViewModels;
using Beutl.Editor.Components.CurvesTab.ViewModels;
using Beutl.Editor.Components.FileBrowserTab.ViewModels;
using Beutl.Editor.Components.PreviewSettingsTab.ViewModels;
using Beutl.ViewModels.Tools;
using Reactive.Bindings;

namespace Beutl.Services;

internal sealed class ToolUsageTracker : IDisposable
{
    private readonly CompositeDisposable _subscriptions = [];
    private readonly HashSet<object> _commands = new(ReferenceEqualityComparer.Instance);
    private readonly IToolContext _context;
    private bool _interacted;
    internal string Tool { get; }

    internal ToolUsageTracker(IToolContext context)
    {
        _context = context;
        Tool = GetToolId(context.Extension.GetType());
        UsageTelemetry.Current?.Record("tool.opened", Tool);
        if (Tool == "Extension") return;

        TrackCommands(context).DisposeWith(_subscriptions);
        switch (context)
        {
            case ColorScopesTabViewModel scopes:
                TrackSetting(scopes.SelectedScopeType, nameof(scopes.SelectedScopeType));
                TrackSetting(scopes.WaveformMode, nameof(scopes.WaveformMode));
                TrackSetting(scopes.HistogramMode, nameof(scopes.HistogramMode));
                TrackSetting(scopes.ColorSpace, nameof(scopes.ColorSpace));
                break;
            case AudioVisualizerTabViewModel audio:
                TrackSetting(audio.SelectedMode, nameof(audio.SelectedMode));
                TrackSetting(audio.SpectrumShape, nameof(audio.SpectrumShape));
                break;
            case ColorGradingTabViewModel grading:
                TrackSetting(grading.WheelMode, nameof(grading.WheelMode));
                TrackSetting(grading.IsNumberEditorsVisible, nameof(grading.IsNumberEditorsVisible));
                break;
            case CurvesTabViewModel curves:
                TrackSetting(curves.SelectedGroupItem.Select(item => item?.Group), "Group");
                break;
            case PreviewSettingsTabViewModel preview:
                TrackSetting(preview.IsOnionSkinEnabled, nameof(preview.IsOnionSkinEnabled));
                TrackSetting(preview.IsFrameCacheEnabled, nameof(preview.IsFrameCacheEnabled));
                break;
            case AiWorkspaceViewModel ai:
                var aiCommands = new SerialDisposable().DisposeWith(_subscriptions);
                ai.SelectedSection.Skip(1).Where(section => section is not null)
                    .Subscribe(section => RecordSetting("Section", section!.Id.ToString()))
                    .DisposeWith(_subscriptions);
                ai.ActiveContent.DistinctUntilChanged().Subscribe(content =>
                {
                    aiCommands.Disposable = content is null ? Disposable.Empty
                        : TrackCommands(content, content.GetType().Name + ".");
                }).DisposeWith(_subscriptions);
                break;
            case FileBrowserTabViewModel files:
                TrackSetting(files.ViewMode, nameof(files.ViewMode));
                var storageCommands = new SerialDisposable().DisposeWith(_subscriptions);
                files.StorageBrowser.DistinctUntilChanged().Subscribe(browser =>
                {
                    storageCommands.Disposable = browser is null ? Disposable.Empty : TrackCommands(browser, "Storage.");
                }).DisposeWith(_subscriptions);
                break;
        }
    }

    internal static bool IsBuiltIn(Type type) => type.Assembly == typeof(ToolUsageTracker).Assembly
        || type.Assembly == typeof(ColorScopesTabViewModel).Assembly;

    internal static string GetToolId(Type type) => IsBuiltIn(type)
        ? type.Name.Replace("TabExtension", "", StringComparison.Ordinal) : "Extension";

    internal void Interact() => _interacted = true;

    // These are compiled command property names on built-in view models, never
    // command parameters, labels or arbitrary extension member names. Observing
    // the command covers buttons, shortcuts and the palette with one subscription.
    private IDisposable TrackCommands(object owner, string prefix = "")
    {
        var subscriptions = new CompositeDisposable();
        if (!IsBuiltIn(owner.GetType())) return subscriptions;
        foreach (PropertyInfo property in owner.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            if (!typeof(ICommand).IsAssignableFrom(property.PropertyType) || property.GetIndexParameters().Length != 0)
                continue;
            // ScrollTo is an internal navigation signal, not a user-facing command.
            if (property.Name == "ScrollTo") continue;
            try
            {
                if (property.GetValue(owner) is not ICommand command || !_commands.Add(command)) continue;
                Disposable.Create(() => _commands.Remove(command)).DisposeWith(subscriptions);
                for (Type? type = command.GetType(); type is not null; type = type.BaseType)
                {
                    if (!type.IsGenericType) continue;
                    Type definition = type.GetGenericTypeDefinition();
                    string? method = definition == typeof(ReactiveCommand<>) ? nameof(TrackCommand)
                        : definition == typeof(ReactiveCommandSlim<>) ? nameof(TrackSlimCommand)
                        : definition == typeof(AsyncReactiveCommand<>) ? nameof(TrackAsyncCommand) : null;
                    if (method is null) continue;
                    var subscription = (IDisposable)typeof(ToolUsageTracker).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .MakeGenericMethod(type.GenericTypeArguments[0])
                        .Invoke(this, [command, prefix + property.Name])!;
                    subscription.DisposeWith(subscriptions);
                    break;
                }
            }
            catch
            {
                // Instrumentation must not prevent a tool from opening.
            }
        }
        return subscriptions;
    }

    private IDisposable TrackCommand<T>(ReactiveCommand<T> command, string feature) =>
        command.Subscribe(_ => RecordCommand(feature));

    private IDisposable TrackSlimCommand<T>(ReactiveCommandSlim<T> command, string feature) =>
        command.Subscribe(_ => RecordCommand(feature));

    private IDisposable TrackAsyncCommand<T>(AsyncReactiveCommand<T> command, string feature) =>
        command.Subscribe(_ =>
        {
            RecordCommand(feature);
            return Task.CompletedTask;
        });

    private void RecordCommand(string feature) => UsageTelemetry.Current?.Record("tool.command", Tool, feature);

    private void TrackSetting<T>(IObservable<T> values, string feature) => values.DistinctUntilChanged().Skip(1)
        .Subscribe(value =>
        {
            if (value is bool boolean) RecordSetting(feature, boolean ? "enabled" : "disabled");
            else if (value is Enum mode && Enum.IsDefined(mode.GetType(), mode)) RecordSetting(feature, mode.ToString());
        }).DisposeWith(_subscriptions);

    private void RecordSetting(string feature, string mode)
    {
        // Skip initial layout restore and model-driven changes in background tabs.
        if (_interacted && _context.IsSelected.Value)
            UsageTelemetry.Current?.Record("tool.setting", Tool, feature + "." + mode);
    }

    public void Dispose() => _subscriptions.Dispose();
}
