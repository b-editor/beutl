using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia.Markup.Xaml.Templates;
using Avalonia.Threading;

using BEditor.Data;
using BEditor.Media;
using BEditor.Media.Encoding;
using BEditor.Media.PCM;
using BEditor.Models;
using BEditor.Primitive.Objects;
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
            LengthFrame.Value = Project.PreviewScene.TotalFrame;
            SelectedScene.Where(s => s.TotalFrame < LengthFrame.Value)
                .Subscribe(s => LengthFrame.Value = s.TotalFrame);
            StartTime = StartFrame.Select(i => ((Frame)i).ToTimeSpan(Project.Framerate))
                .ToReadOnlyReactivePropertySlim();
            LengthTime = LengthFrame.Select(i => ((Frame)i).ToTimeSpan(Project.Framerate))
                .ToReadOnlyReactivePropertySlim();

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
                        }),
                        new(Strings.AudioFile, new FileExtension[]
                        {
                            new("mp3"),
                            new("wav"),
                        }),
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
                    Maximum = { Value = LengthFrame.Value }
                };
                var dialogTask = dialog.ShowDialog<ButtonType>(App.GetMainWindow())
                    .ContinueWith(async type => t = await type is ButtonType.Cancel);

                await Task.Run(async () =>
                {
                    try
                    {
                        var scene = SelectedScene.Value;
                        var proj = Project;

                        var output = MediaBuilder.CreateContainer(File.Value)
                            .WithVideo(config =>
                            {
                                config.VideoWidth = SelectedScene.Value.Width;
                                config.VideoHeight = SelectedScene.Value.Height;
                                config.Framerate = Project.Framerate;
                                config.Bitrate = VideoBitrate.Value;
                                config.KeyframeRate = KeyframeRate.Value;
                            })
                            .WithAudio(config =>
                            {
                                config.Bitrate = AudioBitrate.Value;
                                config.Channels = 2;
                                config.SampleRate = Project.Samplingrate;
                            })
                            .UseMetadata(new()
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
                            })
                            .Create();

                        // 動画
                        for (Frame frame = StartFrame.Value; frame < LengthFrame.Value; frame++)
                        {
                            if (t)
                            {
                                output.Dispose();
                                return;
                            }

                            dialog.NowValue.Value = frame;

                            // UIスレッドだけでレンダリングできる
                            var img = await Dispatcher.UIThread.InvokeAsync(() => scene.Render(frame, RenderType.VideoOutput));
                            output.Video?.AddFrame(img);
                            img.Dispose();
                        }

                        // Audio
                        // Sample per frame
                        var spf = proj.Samplingrate / proj.Framerate;
                        var spf_time = TimeSpan.FromSeconds(spf / (double)proj.Samplingrate);
                        for (Frame frame = StartFrame.Value; frame < LengthFrame.Value; frame++)
                        {
                            using var buffer = new Sound<StereoPCMFloat>(proj.Samplingrate, spf);

                            foreach (var item in scene.GetFrame(frame).Select(i => i.Effect[0]).OfType<AudioObject>())
                            {
                                var rel_start = new Frame(frame - item.Parent.Start).ToTimeSpan(proj.Framerate);

                                if (item.Loaded is null) continue;

                                if (item.Loaded.Time >= rel_start + spf_time)
                                {
                                    using var sliced = item.Loaded.Slice(rel_start, spf_time);
                                    sliced.Gain(item.Volume[frame] / 100);
                                    using var resampled = sliced.Resamples(proj.Samplingrate);

                                    buffer.Add(resampled);
                                }
                            }

                            output.Audio?.AddFrame(buffer);
                        }

                        output.Dispose();

                        await Dispatcher.UIThread.InvokeAsync(dialog.Close);
                    }
                    catch (Exception e)
                    {
                        await AppModel.Current.Message.DialogAsync(Strings.FailedToSave);
                        App.Logger.LogError(e, Strings.FailedToSave);

                        await Dispatcher.UIThread.InvokeAsync(dialog.Close);
                    }
                    finally
                    {
                        AppModel.Current.AppStatus = Status.Edit;
                    }
                });

                await dialogTask;
            });
        }

        public MainWindowViewModel MainWindow { get; } = MainWindowViewModel.Current;
        public Project Project { get; } = AppModel.Current.Project;
        public ReactivePropertySlim<Scene> SelectedScene { get; } = new();

        #region Video
        public ReactivePropertySlim<int> VideoBitrate { get; } = new(5_000_000);
        public ReactivePropertySlim<int> KeyframeRate { get; } = new(12);
        #endregion

        #region Audio
        public ReactivePropertySlim<int> AudioBitrate { get; } = new(128_000);
        #endregion

        #region Metadata
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

        public ReactivePropertySlim<string> File { get; } = new();
        public ReactivePropertySlim<int> StartFrame { get; } = new();
        public ReactivePropertySlim<int> LengthFrame { get; } = new();
        public ReadOnlyReactivePropertySlim<TimeSpan> StartTime { get; }
        public ReadOnlyReactivePropertySlim<TimeSpan> LengthTime { get; }
        public ReactiveCommand SaveFileDialog { get; } = new();
        public ReactiveCommand Output { get; } = new();

        public record EnumTupple<T>(string Name, T Value);
    }
}