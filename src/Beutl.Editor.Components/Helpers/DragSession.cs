using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Beutl.Editor.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Editor.Components.Helpers;

// A pointer gesture's edit session: release, capture loss, playback start and detach confirm it; Escape and Undo cancel it.
internal sealed class DragSession
{
    private readonly EditSession _session;
    private readonly Action _confirm;
    private readonly Action _cancel;
    private readonly CompositeDisposable _watches = [];
    private bool _ended;

    private DragSession(EditSession session, Action confirm, Action cancel)
    {
        _session = session;
        _confirm = confirm;
        _cancel = cancel;
    }

    // Null when another gesture holds history; a nudge window is completed first. Capture the pointer on captureOwner.
    public static DragSession? Begin(
        IServiceProvider editor,
        Control captureOwner,
        string? name,
        Action confirm,
        Action cancel,
        [CallerArgumentExpression(nameof(name))] string? expression = null)
    {
        if (editor.GetService<HistoryManager>() is not { } history)
            return null;

        history.FlushPendingMutations();
        if (history.IsEditing)
            return null;

        var drag = new DragSession(history.BeginEdit(name, expression), confirm, cancel);
        drag.Watch(editor, captureOwner);
        return drag;
    }

    public IDisposable Enter() => _session.Enter();

    public void Confirm() => End(_confirm, complete: true);

    public void Cancel() => End(_cancel, complete: false);

    private void Watch(IServiceProvider editor, Control captureOwner)
    {
        _session.CancelRequested = Cancel;

        captureOwner.AddHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
        captureOwner.DetachedFromVisualTree += OnDetached;
        _watches.Add(Disposable.Create(() =>
        {
            captureOwner.RemoveHandler(InputElement.PointerCaptureLostEvent, OnCaptureLost);
            captureOwner.DetachedFromVisualTree -= OnDetached;
        }));

        if (TopLevel.GetTopLevel(captureOwner) is { } top)
        {
            top.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
            _watches.Add(Disposable.Create(() => top.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown)));
        }

        if (editor.GetService<IPreviewPlayer>() is { } player)
        {
            _watches.Add(player.IsPlaying.Skip(1).Where(playing => playing).Subscribe(_ => Confirm()));
        }
    }

    private void OnCaptureLost(object? sender, PointerCaptureLostEventArgs e) => Confirm();

    private void OnDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e) => Confirm();

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        e.Handled = true;
        Cancel();
    }

    private void End(Action finish, bool complete)
    {
        if (_ended)
            return;

        _ended = true;
        _watches.Dispose();
        _session.CancelRequested = null;
        try
        {
            finish();
        }
        finally
        {
            if (_session.IsOpen)
            {
                if (complete)
                    _session.Complete();
                else
                    _session.Cancel();
            }
        }
    }
}
