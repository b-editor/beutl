using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Media;
using BEditor.Media.Audio;
using BEditor.Media.Encoding;
using BEditor.Media.Graphics;
using BEditor.Models;
using BEditor.Properties;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

using static BEditor.IMessage;

namespace BEditor.ViewModels
{
    public class VideoOutputViewModel
    {
        public VideoOutputViewModel()
        {
            SelectedScene.Value = Project.PreviewScene;
            VideoCodecs = Enum.GetValues<VideoCodec>().Select(i => new EnumTupple<VideoCodec>(i.ToString("g"), i)).ToArray();
            PixelFormats = Enum.GetValues<ImagePixelFormat>().Select(i => new EnumTupple<ImagePixelFormat>(i.ToString("g"), i)).ToArray();
            AudioCodecs = Enum.GetValues<AudioCodec>().Select(i => new EnumTupple<AudioCodec>(i.ToString("g"), i)).ToArray();
            Presets = Enum.GetValues<EncoderPreset>().Select(i => new EnumTupple<EncoderPreset>(i.ToString("g"), i)).ToArray();
            SampleFormats = Enum.GetValues<SampleFormat>().Select(i => new EnumTupple<SampleFormat>(i.ToString("g"), i)).ToArray();
            ContainerFormats = Enum.GetValues<ContainerFormat>().Select(i => new EnumTupple<ContainerFormat?>(i.ToString("g"), i)).ToList();
            ContainerFormats.Insert(0, SelectedContainerFormat.Value);

            SaveFileDialog.Subscribe(async () =>
            {
                var record = new SaveFileRecord
                {
                    Filters =
                    {
                        new(Strings.VideoFile, new FileExtension[]
                        {
                            new("mp4"),
                            new("avi"),
                        })
                    }
                };

                if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record))
                {
                    File.Value = record.FileName;
                }
            });

            Output.Subscribe(async () =>
            {
                var t = false;
                var dialog = new ProgressDialog(new ButtonType[] { ButtonType.Cancel })
                {
                    Maximum = { Value = SelectedScene.Value.TotalFrame }
                };
                var dialogTask = dialog.ShowDialog<ButtonType>(App.GetMainWindow())
                    .ContinueWith(async type => t = await type is ButtonType.Cancel);

                await Task.Run(async () =>
                {
                    try
                    {
                        var scene = SelectedScene.Value;
                        var proj = Project;

                        var builder = (SelectedContainerFormat.Value.Value is null ?
                            MediaBuilder.CreateContainer(File.Value) :
                            MediaBuilder.CreateContainer(File.Value, (ContainerFormat)SelectedContainerFormat.Value.Value))
                            .WithVideo(new(scene.Width, scene.Height, proj.Framerate, SelectedVideoCodec.Value.Value, SelectedPixelFormat.Value.Value)
                            {
                                EncoderPreset = SelectedPreset.Value.Value,
                                Bitrate = VideoBitrate.Value,
                                KeyframeRate = KeyframeRate.Value,
                            })
                            .WithAudio(new(proj.Samplingrate, 2, SelectedAudioCodec.Value.Value, SelectedSampleFormat.Value.Value)
                            {
                                Bitrate = AudioBitrate.Value
                            });

                        if (Validation.Value)
                        {
                            builder = builder.UseMetadata(new()
                            {
                                Title = Title.Value,
                                Author = Author.Value,
                                Album = Album.Value,
                                Year = Year.Value,
                                Genre = Genre.Value,
                                Description = Description.Value,
                                Language = Language.Value,
                                Copyright = Copyright.Value,
                                Rating = Rating.Value,
                                TrackNumber = TrackNumber.Value,
                            });
                        }

                        var output = builder.Create();

                        for (Frame frame = 0; frame < scene.TotalFrame; frame++)
                        {
                            if (t)
                            {
                                output.Dispose();
                                return;
                            }

                            dialog.NowValue.Value = frame;

                            Image<BGRA32>? img = null;

                            // UIスレッドだけでレンダリングできる
                            await Dispatcher.UIThread.InvokeAsync(() => img = scene.Render(frame, RenderType.VideoOutput));

                            if (img is not null)
                            {
                                output.Video?.AddFrame(ImageData.FromDrawing(img));
                                await img.DisposeAsync();
                            }
                        }

                        output?.Dispose();

                        await Dispatcher.UIThread.InvokeAsync(dialog.Close);
                    }
                    catch (Exception e)
                    {
                        AppModel.Current.Message?.Snackbar(Strings.FailedToSave);
                        App.Logger.LogError(e, Strings.FailedToSave);
                    }
                    finally
                    {
                        AppModel.Current.AppStatus = Status.Edit;
                    }
                });

                await dialogTask;
            });
        }

        public MainWindowViewModel MainWindow => MainWindowViewModel.Current;
        public Project Project => AppModel.Current.Project;
        public ReactivePropertySlim<Scene> SelectedScene { get; } = new();

        #region Video
        public EnumTupple<VideoCodec>[] VideoCodecs { get; }
        public ReactivePropertySlim<EnumTupple<VideoCodec>> SelectedVideoCodec { get; } = new(new("Default", VideoCodec.Default));
        public EnumTupple<EncoderPreset>[] Presets { get; }
        public ReactivePropertySlim<EnumTupple<EncoderPreset>> SelectedPreset { get; } = new(new("Medium", EncoderPreset.Medium));
        public EnumTupple<ImagePixelFormat>[] PixelFormats { get; }
        public ReactivePropertySlim<EnumTupple<ImagePixelFormat>> SelectedPixelFormat { get; } = new(new("Yuv420", ImagePixelFormat.Yuv420));
        public ReactivePropertySlim<int> VideoBitrate { get; } = new(5_000_000);
        public ReactivePropertySlim<int> KeyframeRate { get; } = new(12);
        #endregion

        #region Audio
        public EnumTupple<AudioCodec>[] AudioCodecs { get; }
        public ReactivePropertySlim<EnumTupple<AudioCodec>> SelectedAudioCodec { get; } = new(new("Default", AudioCodec.Default));
        public EnumTupple<SampleFormat>[] SampleFormats { get; }
        public ReactivePropertySlim<EnumTupple<SampleFormat>> SelectedSampleFormat { get; } = new(new("SignedWord", SampleFormat.SignedWord));
        public ReactivePropertySlim<int> AudioBitrate { get; } = new(128_000);
        #endregion

        #region Metadata
        public ReactivePropertySlim<bool> Validation { get; } = new(false);
        public ReactivePropertySlim<string> Title { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Author { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Album { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Year { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Genre { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Description { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Language { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Copyright { get; } = new(string.Empty);
        public ReactivePropertySlim<string> Rating { get; } = new(string.Empty);
        public ReactivePropertySlim<string> TrackNumber { get; } = new(string.Empty);
        #endregion

        public List<EnumTupple<ContainerFormat?>> ContainerFormats { get; }
        public ReactivePropertySlim<EnumTupple<ContainerFormat?>> SelectedContainerFormat { get; } = new(new("Default", null));
        public ReactivePropertySlim<string> File { get; } = new();
        public ReactiveCommand SaveFileDialog { get; } = new();
        public ReactiveCommand Output { get; } = new();

        public record EnumTupple<T>(string Name, T Value);
    }
}
