using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Data.Property.Easing;

namespace BEditor.Models
{
    public static class TypeEarlyInitializer
    {
        public static bool IsInitialized => ObjectsIsInitialized && EffectsIsInitialized && EasingsIsInitialized;
        public static bool ObjectsIsInitialized { get; private set; }
        public static bool EffectsIsInitialized { get; private set; }
        public static bool EasingsIsInitialized { get; private set; }

        public static async ValueTask AllInitializeAsync()
        {
            var obj = ObjectsInitializeAsync();
            var ef = EffectsInitializeAsync();
            var ease = EasingsInitializeAsync();

            await obj;
            await ef;
            await ease;
        }
        public static async ValueTask ObjectsInitializeAsync()
        {
            if (ObjectsIsInitialized) return;

            ObjectsIsInitialized = true;
            await Task.Run(() =>
            {
                for (var i = 0; i < ObjectMetadata.LoadedObjects.Count; i++)
                {
                    ObjectMetadata.LoadedObjects[i].Type.TypeInitializer?.Invoke(null, null);
                }
            });
        }
        public static async ValueTask EffectsInitializeAsync()
        {
            if (EffectsIsInitialized) return;

            EffectsIsInitialized = true;
            await Task.Run(() =>
            {
                for (var i = 0; i < EffectMetadata.LoadedEffects.Count; i++)
                {
                    var item = EffectMetadata.LoadedEffects[i];
                    item.Type.TypeInitializer?.Invoke(null, null);

                    if(item.Children is not null)
                    {
                        foreach (var ef in item.Children)
                        {
                            ef.Type.TypeInitializer?.Invoke(null, null);
                        }
                    }
                }
            });
        }
        public static async ValueTask EasingsInitializeAsync()
        {
            if (EasingsIsInitialized) return;

            EasingsIsInitialized = true;
            await Task.Run(() =>
            {
                for (var i = 0; i < EasingMetadata.LoadedEasingFunc.Count; i++)
                {
                    EasingMetadata.LoadedEasingFunc[i].Type.TypeInitializer?.Invoke(null, null);
                }
            });
        }
    }
}