using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;

namespace BEditor.Core.Command
{
    public static class ExtensionCommand
    {
        public static void Execute(this IRecordCommand command) => CommandManager.Do(command);

        public static IRecordCommand CreateCheckCommand(this EffectElement self, bool value) => new EffectElement.CheckCommand(self, value);
        public static IRecordCommand CreateUpCommand(this EffectElement self) => new EffectElement.UpCommand(self);
        public static IRecordCommand CreateDownCommand(this EffectElement self) => new EffectElement.DownCommand(self);


        public static IRecordCommand CreateRemoveCommand(this ClipData self, EffectElement effect) => new EffectElement.RemoveCommand(effect);
        public static IRecordCommand CreateAddCommand(this ClipData self, EffectElement effect) => new EffectElement.AddCommand(effect, self);
        public static IRecordCommand CreateMoveCommand(this ClipData self, int to, int tolayer) => new ClipData.MoveCommand(self, to, tolayer);
        public static IRecordCommand CreateMoveCommand(this ClipData self, int to, int from, int tolayer, int fromlayer) => new ClipData.MoveCommand(self, to, from, tolayer, fromlayer);
        public static IRecordCommand CreateLengthChangeCommand(this ClipData elf, int start, int end) => new ClipData.LengthChangeCommand(elf, start, end);

        public static IRecordCommand CreateAddCommand(this Scene self, int addframe, int layer, ObjectMetadata metadata, out ClipData generatedClip)
        {
            _ = self ?? throw new ArgumentNullException(nameof(self));
            _ = (0 > addframe) ? throw new ArgumentOutOfRangeException(nameof(addframe)) : addframe;
            _ = (0 > layer) ? throw new ArgumentOutOfRangeException(nameof(layer)) : layer;
            _ = metadata ?? throw new ArgumentNullException(nameof(metadata));

            //新しいidを取得
            int idmax = self.NewId;

            //描画情報
            var list = new ObservableCollection<EffectElement>
                {
                    (EffectElement)(metadata.CreateFunc?.Invoke() ?? Activator.CreateInstance(metadata.Type))
                };

            //オブジェクトの情報
            generatedClip = new ClipData(idmax, list, addframe, addframe + 180, metadata.Type, layer, self);
            generatedClip.PropertyLoaded();

            return RecordCommand.Create(
                generatedClip,
                clip =>
                {
                    var scene = clip.Parent;
                    scene.Add(clip);
                    scene.SetCurrentClip(clip);
                },
                clip =>
                {
                    var scene = clip.Parent;
                    scene.Remove(clip);

                    //存在する場合
                    if (scene.SelectNames.Exists(x => x == clip.Name))
                    {
                        scene.SelectItems.Remove(clip);

                        if (scene.SelectName == clip.Name)
                        {
                            scene.SelectItem = null;
                        }
                    }
                });
        }
        public static IRecordCommand CreateRemoveCommand(this Scene self, ClipData clip) => new ClipData.RemoveCommand(clip);

        internal static void ExecuteLoaded(this PropertyElement property, PropertyElementMetadata metadata)
        {
            property.PropertyLoaded();
            property.PropertyMetadata = metadata;
        }
        internal static void ExecuteLoaded<T>(this PropertyElement<T> property, T metadata) where T : PropertyElementMetadata
        {
            property.PropertyLoaded();
            property.PropertyMetadata = metadata;
        }
    }
}
