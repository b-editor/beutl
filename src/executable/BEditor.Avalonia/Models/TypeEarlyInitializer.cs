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
            await Task.Run(() =>
            {
                ObjectsInitialize();
                EffectsInitialize();
                EasingsInitialize();
            });
        }

        public static void ObjectsInitialize()
        {
            if (ObjectsIsInitialized) return;

            for (var i = 0; i < ObjectMetadata.LoadedObjects.Count; i++)
            {
                ObjectMetadata.LoadedObjects[i].Type.TypeInitializer?.Invoke(null, null);
            }

            ObjectsIsInitialized = true;
        }

        public static void EffectsInitialize()
        {
            if (EffectsIsInitialized) return;

            for (var i = 0; i < EffectMetadata.LoadedEffects.Count; i++)
            {
                var item = EffectMetadata.LoadedEffects[i];
                item.Type.TypeInitializer?.Invoke(null, null);

                if (item.Children is not null)
                {
                    foreach (var ef in item.Children)
                    {
                        ef.Type.TypeInitializer?.Invoke(null, null);
                    }
                }
            }

            EffectsIsInitialized = true;
        }

        public static void EasingsInitialize()
        {
            if (EasingsIsInitialized) return;

            for (var i = 0; i < EasingMetadata.LoadedEasingFunc.Count; i++)
            {
                EasingMetadata.LoadedEasingFunc[i].Type.TypeInitializer?.Invoke(null, null);
            }

            EasingsIsInitialized = true;
        }
    }
}