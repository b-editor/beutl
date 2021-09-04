using System;

using BEditor.Data;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class Plugin : PluginObject
    {
        public Plugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.MotionTrack";

        public override string Description => "";

        public override SettingRecord Settings { get; set; } = new();

        public override Guid Id { get; } = Guid.Parse("92B488ED-F31A-4F90-A645-021C9BC99C48");

        public static void Register()
        {
            PluginBuilder.Configure<Plugin>()
                .With(new EffectMetadata("モーショントラッキング")
                {
                    Children = new EffectMetadata[]
                    {
                        EffectMetadata.Create<Linker>("リンカー"),
                    },
                })
                .With(ObjectMetadata.Create<Tracker>("トラッカー"))
                .ConfigureServices(s => s.AddSingleton<TrackingService>())
                .SetCustomMenu("BEditor.Extensions.MotionTrack", new ICustomMenu[]
                {
                    new CustomMenu("メモリ解放", () =>
                    {
                        var service = ServicesLocator.Current.Provider.GetService<TrackingService>();
                        if (service != null)
                        {
                            service.Saved.Clear();
                            GC.Collect();
                            GC.WaitForFullGCComplete();
                            GC.Collect();
                        }
                    })
                })
                .Register();
        }
    }
}
