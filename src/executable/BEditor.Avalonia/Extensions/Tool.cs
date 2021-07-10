using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Media;
using BEditor.Media.Encoding;
using BEditor.Models;
using BEditor.Packaging;
using BEditor.Properties;
using BEditor.ViewModels;
using BEditor.Views;

using Microsoft.Extensions.Logging;

namespace BEditor.Extensions
{
    public static class Tool
    {
        private static Image? _image;

        public static bool PreviewIsEnabled { get; set; } = true;

        public static async Task PreviewUpdateAsync(this Project project, ClipElement clipData, ApplyType type = ApplyType.Edit)
        {
            if (project is null) return;
            var now = project.CurrentScene.PreviewFrame;
            if (clipData.Start <= now && now <= clipData.End)
            {
                await project.PreviewUpdateAsync(type);
            }
        }

        public static async Task PreviewUpdateAsync(this Project project, ApplyType type = ApplyType.Edit)
        {
            if (project?.IsLoaded != true || project.CurrentScene.GraphicsContext is null || !PreviewIsEnabled) return;
            PreviewIsEnabled = false;
            try
            {
                using var img = await Task.Run(() => project.CurrentScene.Render(type));
                var snd = project.CurrentScene.Sample();

                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _image ??= App.GetMainWindow().FindControl<Previewer>("previewer").FindControl<Image>("image");

                    var viewmodel = MainWindowViewModel.Current.Previewer;

                    if (viewmodel.PreviewImage.Value is null
                        || viewmodel.PreviewImage.Value.PixelSize.Width != img.Width
                        || viewmodel.PreviewImage.Value.PixelSize.Height != img.Height)
                    {
                        viewmodel.PreviewImage.Value = new(
                            new(img.Width, img.Height),
                            new(96, 96),
                            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
                    }

                    var buf = viewmodel.PreviewImage.Value.Lock();

                    unsafe
                    {
                        fixed (void* src = img.Data)
                        {
                            var size = img.DataSize;
                            Buffer.MemoryCopy(src, (void*)buf.Address, size, size);
                        }
                    }

                    buf.Dispose();

                    // 再描画
                    _image.InvalidateVisual();

                    viewmodel.PreviewAudio.Value?.Dispose();
                    viewmodel.PreviewAudio.Value = snd;
                });

                PreviewIsEnabled = true;
            }
            catch (Exception e)
            {
                var app = AppModel.Current;
                App.Logger.LogError(e, "Failed to rendering.");

                if (app.AppStatus is Status.Playing)
                {
                    app.AppStatus = Status.Edit;
                    app.Project!.CurrentScene.Player.Stop();
                    app.IsNotPlaying = true;

                    app.Message.Snackbar(Strings.An_exception_was_thrown_during_rendering);

                    PreviewIsEnabled = true;
                }
                else
                {
                    PreviewIsEnabled = false;

                    app.Message.Snackbar(Strings.An_exception_was_thrown_during_rendering_preview);

                    await Task.Delay(TimeSpan.FromSeconds(5));

                    PreviewIsEnabled = true;
                }
            }
        }

        public static async ValueTask<AuthenticationLink?> LoadFromAsync(string filename, IAuthenticationProvider provider)
        {
            if (!File.Exists(filename)) return null;
            try
            {
                using var reader = new StreamReader(filename);
                var token = reader.ReadLine();
                var reftoken = reader.ReadLine();

                if (token is null || reftoken is null) return null;
                var auth = new AuthenticationLink(
                    new()
                    {
                        RefreshToken = reftoken,
                        Token = token,
                    },
                    provider);
                await auth.RefreshAuthAsync();

                return auth;
            }
            catch
            {
                return null;
            }
        }

        public static void Save(this Authentication auth, string filename)
        {
            using var stream = new FileStream(filename, FileMode.Create);
            using var writer = new StreamWriter(stream);

            writer.WriteLine(auth.Token);
            writer.WriteLine(auth.RefreshToken);
        }

        public static double ToPixel(this Scene scene, Frame frame)
        {
            return ConstantSettings.WidthOf1Frame * (scene.TimeLineZoom / 200) * frame;
        }

        public static Frame ToFrame(this Scene scene, double pixel)
        {
            return (int)(pixel / (ConstantSettings.WidthOf1Frame * (scene.TimeLineZoom / 200)));
        }

        public static bool Clamp(this Scene self, ClipElement? clip_, ref Frame start, ref Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (var i = 0; i < array.Length; i++)
            {
                var clip = array[i];

                if (clip != clip_ && clip.InRange(start, end, out var type))
                {
                    if (type == RangeType.StartEnd)
                    {
                        return false;
                    }
                    else if (type == RangeType.Start)
                    {
                        start = clip.End;

                        return true;
                    }
                    else if (type == RangeType.End)
                    {
                        end = clip.Start;

                        return true;
                    }

                    return false;
                }
            }

            return true;
        }

        public static bool InRange(this Scene self, Frame start, Frame end, int layer)
        {
            foreach (var clip in self.GetLayer(layer))
            {
                if (clip.InRange(start, end))
                {
                    return false;
                }
            }

            return true;
        }

        public static bool InRange(this Scene self, ClipElement clip_, Frame start, Frame end, int layer)
        {
            var array = self.GetLayer(layer).ToArray();

            for (var i = 0; i < array.Length; i++)
            {
                var clip = array[i];

                if (clip != clip_ && clip.InRange(start, end, out _))
                {
                    return false;
                }
            }

            return true;
        }

        // このクリップと被る場合はtrue
        public static bool InRange(this ClipElement self, Frame start, Frame end)
        {
            if (self.Start <= start && end <= self.End)
            {
                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                return true;
            }

            return false;
        }

        public static bool InRange(this ClipElement self, Frame start, Frame end, out RangeType type)
        {
            if (self.Start <= start && end <= self.End)
            {
                type = RangeType.StartEnd;

                return true;
            }
            else if (self.Start <= start && start < self.End)
            {
                type = RangeType.Start;

                return true;
            }
            else if (self.Start < end && end <= self.End)
            {
                type = RangeType.End;

                return true;
            }
            else if (start <= self.Start && self.End <= end)
            {
                type = RangeType.StartEnd;

                return true;
            }
            type = default;
            return false;
        }

        public static VideoEncoderSettings GetVideoSettings(this IRegisterdEncoding encoding)
        {
            return encoding is ISupportEncodingSettings sp
                ? sp.GetDefaultVideoSettings()
                : new VideoEncoderSettings(1920, 1080);
        }

        public static AudioEncoderSettings GetAudioSettings(this IRegisterdEncoding encoding)
        {
            return encoding is ISupportEncodingSettings sp
                ? sp.GetDefaultAudioSettings()
                : new AudioEncoderSettings(44100, 2);
        }

        public enum RangeType
        {
            StartEnd,
            Start,
            End
        }
    }
}