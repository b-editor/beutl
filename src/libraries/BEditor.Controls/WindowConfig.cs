using System;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

using Avalonia;
using Avalonia.Controls;

namespace BEditor.Controls
{
    public sealed class WindowConfig : AvaloniaObject
    {
        private sealed class Config
        {
            [JsonPropertyName("x")]
            public int X { get; set; }

            [JsonPropertyName("y")]
            public int Y { get; set; }

            [JsonPropertyName("width")]
            public int Width { get; set; }

            [JsonPropertyName("height")]
            public int Height { get; set; }

            [JsonPropertyName("state")]
            public int State { get; set; }

            [JsonPropertyName("version")]
            public string Version { get; set; } = "0.0.0";
        }

        public static readonly AttachedProperty<bool> SaveProperty = AvaloniaProperty.RegisterAttached<WindowConfig, Window, bool>(
            "Save",
            coerce: (obj, value) =>
            {
                if (obj is Window win)
                {
                    if (value)
                    {
                        win.Initialized -= Win_Opened;
                        win.Closing -= Win_Closing;
                        win.Initialized += Win_Opened;
                        win.Closing += Win_Closing;
                    }
                    else
                    {
                        win.Initialized -= Win_Opened;
                        win.Closing -= Win_Closing;
                    }
                }
                return value;
            });

        private static readonly Version _currentVersion = typeof(WindowConfig).Assembly.GetName().Version ?? new Version(0, 0);

        public static string GetFolder()
        {
            var path = Path.Combine(ServicesLocator.GetUserFolder(), "window");
            if (!Directory.Exists(path)) Directory.CreateDirectory(path);

            return path;
        }

        private static void Win_Opened(object? sender, EventArgs e)
        {
            if (sender is Window win)
            {
                var path = Path.Combine(GetFolder(), win.GetType().Name + ".json");
                if (!File.Exists(path)) return;
                try
                {
                    var json = File.ReadAllText(path);

                    var obj = JsonSerializer.Deserialize<Config>(json, Packaging.PackageFile._serializerOptions);
                    if (obj is null ||
                        obj.Version != _currentVersion.ToString(3) ||
                        ((WindowState)obj.State) == WindowState.Minimized) return;

                    if (win.WindowStartupLocation is WindowStartupLocation.Manual)
                    {
                        win.Position = new PixelPoint(obj.X, obj.Y);
                    }

                    var state = (WindowState)obj.State;

                    if (!win.CanResize) return;

                    if (state is WindowState.Normal)
                    {
                        win.Width = obj.Width < 100 ? win.Width : obj.Width;
                        win.Height = obj.Height < 100 ? win.Height : obj.Height;
                    }

                    win.WindowState = state;
                }
                catch
                {
                }
            }
        }

        private static void Win_Closing(object? sender, CancelEventArgs e)
        {
            if (sender is Window win)
            {
                var path = Path.Combine(GetFolder(), win.GetType().Name + ".json");
                try
                {
                    var json = JsonSerializer.Serialize(new Config
                    {
                        X = win.Position.X,
                        Y = win.Position.Y,
                        Width = (int)win.Width,
                        Height = (int)win.Height,
                        State = (int)win.WindowState,
                        Version = _currentVersion.ToString(3),
                    }, Packaging.PackageFile._serializerOptions);

                    File.WriteAllText(path, json);
                }
                catch
                {
                }
            }
        }
    }
}