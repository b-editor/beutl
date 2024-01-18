using System.Runtime.InteropServices;

using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;

using FluentAvalonia.UI.Windowing;

namespace Beutl;

public partial class KeyModifierMonitor : AppWindow
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
            or Key.DbeEnterImeConfigureMode
            or Key.DbeEnterWordRegisterMode
            or Key.DbeFlushString
            or Key.DbeHiragana
            or Key.DbeKatakana
            or Key.DbeNoCodeInput
            or Key.DbeNoRoman
            or Key.DbeRoman
            or Key.DbeSbcsChar;
    }

    private static string GetKeyName(Key key)
    {
        return key switch
        {
            Key.LeftCtrl or Key.RightCtrl => "Ctrl",
            Key.LeftShift or Key.RightShift => "Shift",
            Key.LeftAlt or Key.RightAlt => "Alt",
            Key.OemComma => ",",
            Key.Oem1 => "1",
            Key.Oem102 => "3",
            Key.Oem2 => "2",
            Key.Oem3 => "3",
            Key.Oem4 => "4",
            Key.Oem5 => "5",
            Key.Oem6 => "6",
            Key.Oem7 => "7",
            Key.Oem8 => "8",
            Key.OemMinus => "-",
            Key.OemPeriod => ".",
            _ => key.ToString(),
        };
    }

    private async void OnOwnerKeyUp(object? sender, KeyEventArgs e)
    {
        if (Skip(e.Key)) return;

        if (_buttons.TryGetValue(e.Key, out Button? btn))
        {
            btn.Classes.Set("accent", false);
            CancellationTokenSource? cts;
            lock (_cts)
            {
                if (_cts.TryGetValue(e.Key, out cts))
                {
                    cts.Cancel();
                    _cts.Remove(e.Key);
                }

                cts = new CancellationTokenSource();
                _cts.TryAdd(e.Key, cts);
            }

            try
            {
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
            catch
            {
            }
        }
    }

    private void OnOwnerKeyDown(object? sender, KeyEventArgs e)
    {
        if (Skip(e.Key)) return;

        lock (_cts)
        {
            if (_cts.TryGetValue(e.Key, out CancellationTokenSource? cts))
            {
                cts.Cancel();
                _cts.Remove(e.Key);
            }
        }

        if (!_buttons.TryGetValue(e.Key, out Button? btn))
        {
            btn = new Button()
            {
                Content = GetKeyName(e.Key)
            };
            _buttons.Add(e.Key, btn);
        }

        btn.Classes.Set("accent", true);
        if (btn.Parent == null)
        {
            Host.Children.Add(btn);
        }
    }
}
