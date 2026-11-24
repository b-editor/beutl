using System.Globalization;
using Avalonia.Threading;
using Beutl.Engine;
using Beutl.Graphics.Transitions;
using Beutl.ProjectSystem;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.TimelineTab.ViewModels;

public sealed partial class TimelineTabViewModel
{
    private readonly Dictionary<Element, string?> _flowInputWarnings = [];

    internal event Action? FlowInputWarningsInvalidated;

    internal string? GetFlowInputWarning(Element element)
    {
        if (_flowInputWarnings.TryGetValue(element, out string? warning)) return warning;
        if (!element.Objects.Any(obj => obj is IFlowInputOperator)) return null;

        IReadOnlyList<EngineObject> missing = FlowInputDiagnostics.FindMissingInputs(Scene, element);
        warning = missing.Count == 0 ? null : string.Join("\n\n", missing.Select(obj => string.Format(CultureInfo.CurrentCulture,
            ((IFlowInputOperator)obj).MissingInputMessage, TypeDisplayHelpers.GetLocalizedName(obj.GetType()))));
        _flowInputWarnings.Add(element, warning);
        return warning;
    }

    private void TrackFlowInputWarnings()
    {
        Scene.Edited += OnEdited;
        Disposable.Create(() => Scene.Edited -= OnEdited).AddTo(_disposables);
        Scene.FindHierarchicalParent<Project>()?.FrameRate.GetObservable().Skip(1)
            .Subscribe(_ => Invalidate()).AddTo(_disposables);

        void OnEdited(object? sender, EventArgs e)
        {
            // Scene publishes unrelated edits too, including animated property changes and layer mute/solo.
            if (sender is IProperty property)
            {
                bool affectsInputs = property.GetOwnerObject() switch
                {
                    PortalObject portal => ReferenceEquals(property, portal.Count) || ReferenceEquals(property, portal.Clear)
                        || ReferenceEquals(property, portal.IsEnabled),
                    ClipTransition => true,
                    EngineObject obj => ReferenceEquals(property, obj.IsEnabled) || (obj is IFlowInputOperator && property is IListProperty),
                    _ => false
                };
                if (!affectsInputs) return;
            }
            else if (sender is TimelineLayer)
            {
                return;
            }

            Invalidate();
        }

        void Invalidate()
        {
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(Invalidate);
                return;
            }

            if (_isDisposed) return;
            _flowInputWarnings.Clear();
            FlowInputWarningsInvalidated?.Invoke();
        }
    }
}
