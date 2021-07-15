using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

namespace BEditor
{
    public class WindowConfig : AvaloniaObject
    {
        private class Config
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
                    if (obj is null) return;

                    if (win.WindowStartupLocation is WindowStartupLocation.Manual)
                    {
                        win.Position = new PixelPoint(obj.X, obj.Y);
                    }

                    var state = (WindowState)obj.State;

                    if (!win.CanResize) return;

                    if (state is WindowState.Normal)
                    {
                        win.Width = obj.Width;
                        win.Height = obj.Height;
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
