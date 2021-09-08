using System;
using System.ComponentModel;
using System.IO;

using BEditor.Data;
using BEditor.Extensions.MotionTrack.Resources;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Extensions.MotionTrack
{
    public sealed class Plugin : PluginObject
    {
        private SettingRecord? settings;

        public Plugin(PluginConfig config) : base(config)
        {
        }

        public override string PluginName => "BEditor.Extensions.MotionTrack";

        public override string Description => Strings.PluginDescription;

        public override SettingRecord Settings
        {
            get => settings ??= SettingRecord.LoadFrom<CustomSettings>(Path.Combine(BaseDirectory, "settings.json")) ?? new CustomSettings(Algorithm.KCF);
            set => (settings = value).Save(Path.Combine(BaseDirectory, "settings.json"));
        }

        public override Guid Id { get; } = Guid.Parse("92B488ED-F31A-4F90-A645-021C9BC99C48");

        public static void Register()
        {
            PluginBuilder.Configure<Plugin>()
                .With(new EffectMetadata(Strings.MotionTracking)
                {
                    Children = new EffectMetadata[]
                    {
                        EffectMetadata.Create<Linker>(Strings.Linker),
                    },
                })
                .With(ObjectMetadata.Create<Tracker>(Strings.Tracker))
                .ConfigureServices(s => s.AddSingleton<TrackingService>())
                .SetCustomMenu("BEditor.Extensions.MotionTrack", new ICustomMenu[]
                {
                    new CustomMenu(Strings.ReleaseMemory, () =>
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

    public record CustomSettings(
        [property: DisplayName("アルゴリズム")]
        Algorithm Algorithm) : SettingRecord;

    public enum Algorithm
    {
        MIL,
        KCF,
        CSRT,
    }
}
