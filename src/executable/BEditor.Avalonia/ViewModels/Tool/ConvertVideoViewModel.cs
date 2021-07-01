using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;

using Avalonia.Threading;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Extensions;
using BEditor.Media;
using BEditor.Media.Decoding;
using BEditor.Media.Encoding;
using BEditor.Models;
using BEditor.Models.Tool;
using BEditor.Properties;
using BEditor.Views.DialogContent;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

using static BEditor.IMessage;

namespace BEditor.ViewModels.Tool
{
    public sealed class ConvertVideoViewModel
    {
        private readonly ConvertVideoSource _source;

        public ConvertVideoViewModel(ConvertVideoSource source)
        {
            _source = source;
            Width.Value = source.VideoInfo.FrameSize.Width;
            Height.Value = source.VideoInfo.FrameSize.Height;
            FrameRate.Value = source.VideoInfo.FrameRate;
            SampleRate.Value = source.AudioInfo.SampleRate;
            LengthFrame.Value = source.VideoInfo.NumberOfFrames;
            TotalFrame.Value = source.VideoInfo.NumberOfFrames;
            StartTime = StartFrame.Select(i => ((Frame)i).ToTimeSpan(_source.VideoInfo.FrameRate))
                .ToReadOnlyReactivePropertySlim();
            LengthTime = LengthFrame.Select(i => ((Frame)i).ToTimeSpan(_source.VideoInfo.FrameRate))
                .ToReadOnlyReactivePropertySlim();

            SaveFileDialog.Subscribe(async () =>
            {
                var record = new SaveFileRecord
                {
                    DefaultFileName = File.Value,
                    Filters =
                    {
                        new(Strings.VideoFile, EncodingRegistory.EnumerateEncodings()
                            .SelectMany(i => i.SupportExtensions())
                            .Distinct()
                            .Select(i => i.Trim('.'))
                            .ToArray())
                    }
                };

                if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record))
                {
                    File.Value = record.FileName;

                    var array = EncodingRegistory.GuessEncodings(File.Value);
                    Encoders.Clear();
                    if (array.Length is 0)
                    {
                        await AppModel.Current.Message.DialogAsync(Strings.EncoderNotFound, IconType.Error);
                        SelectedEncoder.Value = null;
                    }
                    else
                    {
                        Encoders.AddRangeOnScheduler(array);
                        SelectedEncoder.Value = array[0];
                    }
                }
            });

            OutputIsEnabled = SelectedEncoder.Select(i => i is not null)
                .ToReadOnlyReactivePropertySlim();

            AudioEncoderSettings = SelectedEncoder.Select(i => i?.GetAudioSettings()?.CodecOptions)
                .ToReadOnlyReactivePropertySlim();

            VideoEncoderSettings = SelectedEncoder.Select(i => i?.GetVideoSettings()?.CodecOptions)
                .ToReadOnlyReactivePropertySlim();

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
                        var videoSettings = await (GetVideoSettings?.Invoke() ?? Task.FromResult<Dictionary<string, object>?>(null));
                        var audioSettings = await (GetAudioSettings?.Invoke() ?? Task.FromResult<Dictionary<string, object>?>(null));

                        var input = MediaFile.Open(_source.File);

                        var output = MediaBuilder.CreateContainer(File.Value, SelectedEncoder.Value!)
                            .WithVideo(config =>
                            {
                                config.VideoWidth = Width.Value;
                                config.VideoHeight = Height.Value;
                                config.Framerate = FrameRate.Value;
                                config.Bitrate = VideoBitrate.Value;
                                config.KeyframeRate = KeyframeRate.Value;
                                if (videoSettings is not null)
                                {
                                    config.CodecOptions = videoSettings;
                                }
                            })
                            .WithAudio(config =>
                            {
                                config.Bitrate = AudioBitrate.Value;
                                config.Channels = 2;
                                config.SampleRate = SampleRate.Value;
                                if (audioSettings is not null)
                                {
                                    config.CodecOptions = audioSettings;
                                }
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

                            Image<BGRA32>? img = null;
                            if ((input.Video?.TryGetFrame(frame.ToTimeSpan(_source.VideoInfo.FrameRate), out img) ?? false) && img != null)
                            {
                                // リサイズ
                                if (img.Size != new Size(Width.Value, Height.Value))
                                {
                                    var tmp = img.Resize(Width.Value, Height.Value, Quality.High);
                                    img.Dispose();
                                    img = tmp;
                                }

                                output.Video?.AddFrame(img);
                                img.Dispose();
                            }
                        }

                        // Audio
                        var rate = _source.AudioInfo.SampleRate / _source.VideoInfo.FrameRate;
                        for (Frame frame = StartFrame.Value; frame < LengthFrame.Value; frame++)
                        {
                            if (t)
                            {
                                output.Dispose();
                                return;
                            }

                            dialog.NowValue.Value = frame;
                            var snd = input.Audio?.GetFrame(frame.ToTimeSpan(_source.VideoInfo.FrameRate), rate);

                            if (snd is not null)
                            {
                                output.Audio?.AddFrame(snd);
                                snd.Dispose();
                            }
                        }

                        output.Dispose();

                        await Dispatcher.UIThread.InvokeAsync(dialog.Close);
                    }
                    catch (NotSupportedException notsupport)
                    {
                        await AppModel.Current.Message.DialogAsync(Strings.NotSupportedFormats);
                        App.Logger.LogError(notsupport, Strings.NotSupportedFormats);

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

        #region Info
        public ReactivePropertySlim<int> Width { get; } = new();

        public ReactivePropertySlim<int> Height { get; } = new();

        public ReactivePropertySlim<int> FrameRate { get; } = new();

        public ReactivePropertySlim<int> SampleRate { get; } = new();
        #endregion

        #region Video
        public ReactivePropertySlim<int> VideoBitrate { get; } = new(5_000_000);

        public ReactivePropertySlim<int> KeyframeRate { get; } = new(12);

        public ReadOnlyReactivePropertySlim<Dictionary<string, object>?> VideoEncoderSettings { get; }
        #endregion

        #region Audio
        public ReactivePropertySlim<int> AudioBitrate { get; } = new(128_000);

        public ReadOnlyReactivePropertySlim<Dictionary<string, object>?> AudioEncoderSettings { get; }
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

        public ReactivePropertySlim<int> TotalFrame { get; } = new();

        public ReadOnlyReactivePropertySlim<TimeSpan> StartTime { get; }

        public ReadOnlyReactivePropertySlim<TimeSpan> LengthTime { get; }

        public ReactiveCollection<IRegisterdEncoding> Encoders { get; } = new();

        public ReactiveProperty<IRegisterdEncoding?> SelectedEncoder { get; } = new();

        public ReactiveCommand SaveFileDialog { get; } = new();

        public ReactiveCommand Output { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> OutputIsEnabled { get; }

        public Func<Task<Dictionary<string, object>?>>? GetAudioSettings { get; set; }

        public Func<Task<Dictionary<string, object>?>>? GetVideoSettings { get; set; }
    }
}