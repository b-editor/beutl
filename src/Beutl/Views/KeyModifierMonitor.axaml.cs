using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using FluentAvalonia.UI.Windowing;

namespace Beutl;

public partial class KeyModifierMonitor : FAAppWindow
{
    private readonly CompositeDisposable _disposables = [];
    private readonly Dictionary<Key, Button> _buttons = [];
    private readonly Dictionary<Key, CancellationTokenSource> _cts = [];

    public KeyModifierMonitor()
    {
        InitializeComponent();
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        if (Owner != null)
        {
            Owner.AddDisposableHandler(KeyDownEvent, OnOwnerKeyDown, RoutingStrategies.Tunnel)
                .DisposeWith(_disposables);
            Owner.AddDisposableHandler(KeyUpEvent, OnOwnerKeyUp, RoutingStrategies.Tunnel)
                .DisposeWith(_disposables);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _disposables.Clear();
    }

    private static bool Skip(Key key)
    {
        return key is Key.ImeAccept
            or Key.ImeConvert
            or Key.ImeModeChange
            or Key.ImeNonConvert
            or Key.ImeProcessed
            or Key.DbeEnterImeConfigureMode
            or Key.DbeAlphanumeric
            or Key.DbeCodeInput
            or Key.DbeDbcsChar
            or Key.DbeDetermineString
            or Key.DbeEnterDialogConversionMode
            or Key.DbeEnterWordRegisterMode
            or Key.DbeFlushString
            or Key.DbeHiragana
            or Key.DbeKatakana
            or Key.DbeNoCodeInput
            or Key.DbeNoRoman
            or Key.DbeRoman
            or Key.DbeSbcsChar;
    }

    private static string GetKeyName(KeyEventArgs e)
    {
        return e.Key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LeftAlt or Key.RightAlt => "Alt",
            // What the key types in the current layout, such as ";" for Oem1 on a US keyboard.
            _ when e.KeySymbol is { } symbol && !string.IsNullOrWhiteSpace(symbol) && !char.IsControl(symbol, 0)
                => symbol.ToUpperInvariant(),
            _ => e.Key.ToString(),
        };
    }

    private async void OnOwnerKeyUp(object? sender, KeyEventArgs e)
    {
        if (Skip(e.Key)) return;

        if (_buttons.TryGetValue(e.Key, out Button? btn))
        {
            btn.Classes.Set("accent", false);
            if (_cts.TryGetValue(e.Key, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                _cts.Remove(e.Key);
            }

            cts = new CancellationTokenSource();
            _cts.TryAdd(e.Key, cts);

            var anim = new Avalonia.Animation.Animation
            {
                Duration = TimeSpan.FromSeconds(3),
                Children =
                {
                    new KeyFrame
                    {
                        Cue = new Cue(0),
                        Setters = { new Setter(OpacityProperty, 1d) }
                    },
                    new KeyFrame
                    {
                        Cue = new Cue(1),
                        Setters = { new Setter(OpacityProperty, 0d) }
                    },
                }
            };

            await anim.RunAsync(btn, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                Host.Children.Remove(btn);
            }
        }
    }

    private void OnOwnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (Skip(e.Key)) return;

        if (_cts.TryGetValue(e.Key, out CancellationTokenSource? cts))
        {
            cts.Cancel();
            _cts.Remove(e.Key);
        }

        if (!_buttons.TryGetValue(e.Key, out Button? btn))
        {
            btn = new Button();
            _buttons.Add(e.Key, btn);
        }

        // The symbol depends on the modifiers of this press, such as "!" for Shift+1.
        btn.Content = GetKeyName(e);
        btn.Classes.Set("accent", true);
        if (btn.Parent == null)
        {
            Host.Children.Add(btn);
        }
    }
}
