using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Media;

namespace BEditor.Data
{
    public partial class ClipElement
    {
        internal sealed class AddCommand : IRecordCommand
        {
            private readonly Scene Scene;
            public ClipElement Clip;

            public AddCommand(Scene scene, Frame startFrame, int layer, ObjectMetadata metadata)
            {
                Scene = scene ?? throw new ArgumentNullException(nameof(scene));
                if (Frame.Zero > startFrame) throw new ArgumentOutOfRangeException(nameof(startFrame));
                if (0 > layer) throw new ArgumentOutOfRangeException(nameof(layer));
                if (metadata is null) throw new ArgumentNullException(nameof(metadata));

                // 新しいidを取得
                int idmax = scene.NewId;

                // effects
                var list = new ObservableCollection<EffectElement>
                {
                    metadata.CreateFunc()
                };

                // オブジェクトの情報
                Clip = new ClipElement(idmax, list, startFrame, startFrame + 180, layer, scene);
            }

            public string Name => CommandName.AddClip;

            public void Do()
            {
                Clip.Load();
                Scene.Add(Clip);
                Scene.SetCurrentClip(Clip);
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                Scene.Remove(Clip);
                Clip.Unload();

                //存在する場合
                if (Scene.SelectItems.Contains(Clip))
                {
                    Scene.SelectItems.Remove(Clip);

                    if (Scene.SelectItem == Clip)
                    {
                        Scene.SelectItem = null;
                    }
                }
            }
        }
        internal sealed class RemoveCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;

            public RemoveCommand(ClipElement clip)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
            }

            public string Name => CommandName.RemoveClip;

            public void Do()
            {
                if (!_Clip.Parent.Remove(_Clip))
                {
                    //Message.Snackbar("削除できませんでした");
                }
                else
                {
                    _Clip.Unload();
                    //存在する場合
                    if (_Clip.Parent.SelectItems.Contains(_Clip))
                    {
                        _Clip.Parent.SelectItems.Remove(_Clip);

                        if (_Clip.Parent.SelectItem == _Clip)
                        {
                            if (_Clip.Parent.SelectItems.Count == 0)
                            {
                                _Clip.Parent.SelectItem = null;
                            }
                            else
                            {
                                _Clip.Parent.SelectItem = _Clip.Parent.SelectItems[0];
                            }
                        }
                    }
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                _Clip.Load();
                _Clip.Parent.Add(_Clip);
            }
        }
        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;
            private readonly Frame _ToFrame;
            private readonly Frame _FromFrame;
            private readonly int _ToLayer;
            private readonly int _FromLayer;
            private Scene Scene => _Clip.Parent;

            public MoveCommand(ClipElement clip, Frame toFrame, int toLayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > toFrame) ? throw new ArgumentOutOfRangeException(nameof(toFrame)) : toFrame;
                _FromFrame = clip.Start;
                _ToLayer = (0 > toLayer) ? throw new ArgumentOutOfRangeException(nameof(toLayer)) : toLayer;
                _FromLayer = clip.Layer;
            }
            public MoveCommand(ClipElement clip, Frame to, Frame from, int tolayer, int fromlayer)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _ToFrame = (Frame.Zero > to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
                _FromFrame = (Frame.Zero > from) ? throw new ArgumentOutOfRangeException(nameof(from)) : from;
                _ToLayer = (0 > tolayer) ? throw new ArgumentOutOfRangeException(nameof(tolayer)) : tolayer;
                _FromLayer = (0 > fromlayer) ? throw new ArgumentOutOfRangeException(nameof(fromlayer)) : fromlayer;
            }

            public string Name => CommandName.MoveClip;

            public void Do()
            {
                _Clip.MoveTo(_ToFrame);

                _Clip.Layer = _ToLayer;


                if (_Clip.End > Scene.TotalFrame)
                {
                    Scene.TotalFrame = _Clip.End;
                }
            }
            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _Clip.MoveTo(_FromFrame);

                _Clip.Layer = _FromLayer;
            }
        }
        private sealed class LengthChangeCommand : IRecordCommand
        {
            private readonly ClipElement _Clip;
            private readonly Frame _Start;
            private readonly Frame _End;
            private readonly Frame _OldStart;
            private readonly Frame _OldEnd;

            public LengthChangeCommand(ClipElement clip, Frame start, Frame end)
            {
                _Clip = clip ?? throw new ArgumentNullException(nameof(clip));
                _Start = (Frame.Zero > start) ? throw new ArgumentOutOfRangeException(nameof(start)) : start;
                _End = (Frame.Zero > end) ? throw new ArgumentOutOfRangeException(nameof(end)) : end;
                _OldStart = clip.Start;
                _OldEnd = clip.End;
            }

            public string Name => CommandName.ChangeLength;

            public void Do()
            {
                _Clip.Start = _Start;
                _Clip.End = _End;
            }
            public void Redo()
            {
                Do();
            }

            public void Undo()
            {
                _Clip.Start = _OldStart;
                _Clip.End = _OldEnd;
            }
        }
        private sealed class SplitCommand : IRecordCommand
        {
            public readonly ClipElement Before;
            public readonly ClipElement After;
            private readonly ClipElement Source;
            private readonly Scene Scene;

            public SplitCommand(ClipElement clip, Frame frame)
            {
                Source = clip;
                Scene = clip.Parent;
                Before = clip.Clone();
                After = clip.Clone();

                Before.End = frame;
                After.Start = frame;
            }

            public string Name => CommandName.SplitClip;

            public void Do()
            {
                After.Load();
                Before.Load();

                new RemoveCommand(Source).Do();
                After.Id = Scene.NewId;
                Scene.Add(After);
                Before.Id = Scene.NewId;
                Scene.Add(Before);
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                Before.Unload();
                After.Unload();
                Source.Load();

                Scene.Remove(Before);
                Scene.Remove(After);
                Scene.Add(Source);
            }
        }
    }
}
