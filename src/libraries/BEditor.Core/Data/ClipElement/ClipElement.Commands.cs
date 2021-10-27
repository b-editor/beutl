// ClipElement.Commands.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

using BEditor.Command;
using BEditor.Media;
using BEditor.LangResources;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data of a clip to be placed in the timeline.
    /// </summary>
    public sealed partial class ClipElement
    {
        /// <summary>
        /// 指定したシーンに新しいクリップを追加するコマンドを表します.
        /// </summary>
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene _scene;

            /// <summary>
            /// Initializes a new instance of the <see cref="AddCommand"/> class.
            /// </summary>
            /// <param name="scene">新しいクリップを追加するシーンです.</param>
            /// <param name="startFrame">新しいクリップの開始フレームです.</param>
            /// <param name="layer">新しいクリップの配置レイヤーです.</param>
            /// <param name="metadata">新しいクリップのオブジェクトのメタデータです.</param>
            public AddCommand(Scene scene, Frame startFrame, int layer, ObjectMetadata metadata)
            {
                _scene = scene ?? throw new ArgumentNullException(nameof(scene));
                if (startFrame < Frame.Zero) throw new ArgumentOutOfRangeException(nameof(startFrame));
                if (layer < 0) throw new ArgumentOutOfRangeException(nameof(layer));
                if (metadata is null) throw new ArgumentNullException(nameof(metadata));

                // オブジェクトの情報
                Clip = new ClipElement(startFrame, startFrame + 180, layer, scene, metadata);
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="AddCommand"/> class.
            /// </summary>
            /// <param name="scene">新しいクリップを追加するシーンです.</param>
            /// <param name="startFrame">新しいクリップの開始フレームです.</param>
            /// <param name="layer">新しいクリップの配置レイヤーです.</param>
            /// <param name="obj">新しいクリップのオブジェクトです.</param>
            public AddCommand(Scene scene, Frame startFrame, int layer, ObjectElement obj)
            {
                _scene = scene ?? throw new ArgumentNullException(nameof(scene));
                if (startFrame < Frame.Zero) throw new ArgumentOutOfRangeException(nameof(startFrame));
                if (layer < 0) throw new ArgumentOutOfRangeException(nameof(layer));
                if (obj is null) throw new ArgumentNullException(nameof(obj));

                // オブジェクトの情報
                Clip = new ClipElement(startFrame, startFrame + 180, layer, scene, obj);
            }

            /// <summary>
            /// Gets the clip to add.
            /// </summary>
            public ClipElement Clip { get; }

            /// <inheritdoc/>
            public string Name => Strings.AddClip;

            /// <inheritdoc/>
            public void Do()
            {
                Clip.Load();
                _scene.Add(Clip);
                _scene.SelectItem = Clip;

                if (_scene.TotalFrame < Clip.End)
                {
                    _scene.TotalFrame = Clip.End;
                }
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                _scene.Remove(Clip);
                Clip.Unload();

                if (_scene.SelectItem == Clip)
                {
                    _scene.SelectItem = null;
                }
            }
        }

        /// <summary>
        /// クリップを削除するコマンドを表します.
        /// </summary>
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipElement _clip;

            /// <summary>
            /// Initializes a new instance of the <see cref="RemoveCommand"/> class.
            /// </summary>
            /// <param name="clip">削除するクリップです.</param>
            public RemoveCommand(ClipElement clip)
            {
                _clip = clip ?? throw new ArgumentNullException(nameof(clip));
            }

            /// <inheritdoc/>
            public string Name => Strings.RemoveClip;

            /// <inheritdoc/>
            public void Do()
            {
                if (!_clip.Parent.Remove(_clip))
                {
                    _clip.ServiceProvider?.GetService<IMessage>()?.Snackbar(Strings.FailedToRemove, string.Empty, IMessage.IconType.Error);
                }
                else
                {
                    _clip.Unload();

                    // 存在する場合
                    if (_clip.Parent.SelectItem == _clip)
                    {
                        _clip.Parent.SelectItem = null;
                    }
                }
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                var scene = _clip.Parent;
                _clip.Load();
                scene.Add(_clip);

                if (scene.TotalFrame < _clip.End)
                {
                    scene.TotalFrame = _clip.End;
                }
            }
        }

        /// <summary>
        /// クリップを移動するコマンドを表します.
        /// </summary>
        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private readonly Frame _newFrame;
            private readonly Frame _oldFrame;
            private readonly int _newLayer;
            private readonly int _oldLayer;

            /// <summary>
            /// Initializes a new instance of the <see cref="MoveCommand"/> class.
            /// </summary>
            /// <param name="clip">移動するクリップです.</param>
            /// <param name="newframe">新しい開始フレームです.</param>
            /// <param name="newlayer">新しい配置レイヤーです.</param>
            public MoveCommand(ClipElement clip, Frame newframe, int newlayer)
            {
                _clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _newFrame = (newframe < Frame.Zero) ? throw new ArgumentOutOfRangeException(nameof(newframe)) : newframe;
                _oldFrame = clip.Start;
                _newLayer = (newlayer < 0) ? throw new ArgumentOutOfRangeException(nameof(newlayer)) : newlayer;
                _oldLayer = clip.Layer;
            }

            /// <inheritdoc/>
            public string Name => Strings.MoveClip;

            private Scene Scene => _clip.Parent;

            /// <inheritdoc/>
            public void Do()
            {
                _clip.MoveTo(_newFrame);

                _clip.Layer = _newLayer;

                if (Scene.TotalFrame < _clip.End)
                {
                    Scene.TotalFrame = _clip.End;
                }

                _clip.Moved?.Invoke(_clip, new(_newLayer, _oldLayer, _newFrame, _oldFrame));
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                _clip.MoveTo(_oldFrame);

                _clip.Layer = _oldLayer;

                if (Scene.TotalFrame < _clip.End)
                {
                    Scene.TotalFrame = _clip.End;
                }

                _clip.Moved?.Invoke(_clip, new(_oldLayer, _newLayer, _oldFrame, _newFrame));
            }
        }

        /// <summary>
        /// クリップの長さを変更するコマンドを表します.
        /// </summary>
        private sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipElement _clip;
            private Frame _oldLength;
            private Frame _newLength;
            private ClipLengthChangeAnchor _anchor;

            public LengthChangeCommand(ClipElement clip, Frame length, ClipLengthChangeAnchor anchor)
            {
                _clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _anchor = anchor;
                _newLength = (length < Frame.Zero) ? throw new ArgumentOutOfRangeException(nameof(length)) : length;
                _oldLength = clip.Length;
            }

            /// <inheritdoc/>
            public string Name => Strings.ChangeLength;

            /// <inheritdoc/>
            public void Do()
            {
                var scene = _clip.Parent;

                // LengthChangingを発生させる
                var eventArgs = new ClipLengthChangingEventArgs(_anchor, _newLength, _oldLength);
                _clip.LengthChanging?.Invoke(_clip, eventArgs);
                _newLength = eventArgs.NewLength;
                _anchor = eventArgs.Anchor;

                if (_anchor == ClipLengthChangeAnchor.Start)
                {
                    _clip.End = _clip.Start + _newLength;
                }
                else
                {
                    _clip.Start = _clip.End - _newLength;
                }

                if (scene.TotalFrame < _clip.End)
                {
                    scene.TotalFrame = _clip.End;
                }

                _clip.LengthChanged?.Invoke(_clip, new(_anchor, _newLength, _oldLength));
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                var scene = _clip.Parent;

                // LengthChangingを発生させる
                var eventArgs = new ClipLengthChangingEventArgs(_anchor, _oldLength, _newLength);
                _clip.LengthChanging?.Invoke(_clip, eventArgs);
                _oldLength = eventArgs.NewLength;
                _anchor = eventArgs.Anchor;

                if (_anchor == ClipLengthChangeAnchor.Start)
                {
                    _clip.End = _clip.Start + _oldLength;
                }
                else
                {
                    _clip.Start = _clip.End - _oldLength;
                }

                if (scene.TotalFrame < _clip.End)
                {
                    scene.TotalFrame = _clip.End;
                }

                _clip.LengthChanged?.Invoke(_clip, new(_anchor, _oldLength, _newLength));
            }
        }

        /// <summary>
        /// クリップを分割するコマンドを表します.
        /// </summary>
        private sealed class SplitCommand : IRecordCommand
        {
            private readonly ClipElement _source;
            private readonly Scene _scene;

            /// <summary>
            /// Initializes a new instance of the <see cref="SplitCommand"/> class.
            /// </summary>
            /// <param name="clip">分割するクリップです.</param>
            /// <param name="frame">分割するフレームです.</param>
            public SplitCommand(ClipElement clip, Frame frame)
            {
                _source = clip;
                _scene = clip.Parent;
                Before = clip.Clone();
                After = clip.Clone();

                Before.End = frame;
                After.Start = frame;
            }

            /// <summary>
            /// Gets the clip before the split frame.
            /// </summary>
            public ClipElement Before { get; }

            /// <summary>
            /// Gets the clip after the split frame.
            /// </summary>
            public ClipElement After { get; }

            /// <inheritdoc/>
            public string Name => Strings.SplitClip;

            /// <inheritdoc/>
            public void Do()
            {
                After.Load();
                Before.Load();
                _source.Splitted?.Invoke(_source, new ClipSplittedEventArgs(Before, After));

                new RemoveCommand(_source).Do();
                After.UpdateId();
                _scene.Add(After);
                Before.UpdateId();
                _scene.Add(Before);
            }

            /// <inheritdoc/>
            public void Redo()
            {
                Do();
            }

            /// <inheritdoc/>
            public void Undo()
            {
                Before.Unload();
                After.Unload();
                _source.Load();

                _scene.Remove(Before);
                _scene.Remove(After);
                _scene.Add(_source);
            }
        }
    }
}