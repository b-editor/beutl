using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Collections.Specialized;
using System.Threading.Tasks;

using BEditor.Models.ManagePlugins;

using Reactive.Bindings;
using BEditor.Properties;
using BEditor.Plugin;

namespace BEditor.ViewModels.ManagePlugins
{
    public class ScheduleChangesViewModel : IDisposable
    {
        public record Schedule(string Name, string Description, string Type);

        public ScheduleChangesViewModel()
        {
            PluginChangeSchedule.Uninstall.CollectionChanged += Uninstall_CollectionChanged;
            PluginChangeSchedule.UpdateOrInstall.CollectionChanged += UpdateOrInstall_CollectionChanged;

            IsSelected = SelectedItem.Select(i => i is not null).ToReadOnlyReactivePropertySlim();

            Cancel.Where(_ => IsSelected.Value)
                .Subscribe(_ =>
                {
                    var value = SelectedItem.Value;

                    if (value.Type == Strings.Update || value.Type == Strings.Install)
                    {
                        var obj = PluginChangeSchedule.UpdateOrInstall
                            .FirstOrDefault(i => i.Target.Name == SelectedItem.Value.Name);

                        if (obj is not null)
                        {
                            PluginChangeSchedule.UpdateOrInstall.Remove(obj);
                        }
                    }
                    else
                    {
                        var obj = PluginChangeSchedule.Uninstall
                            .FirstOrDefault(i => i.PluginName == SelectedItem.Value.Name);

                        if (obj is not null)
                        {
                            PluginChangeSchedule.Uninstall.Remove(obj);
                        }
                    }
                });
        }

        public ReactiveCollection<Schedule> Schedules { get; } = new();

        public ReactiveProperty<Schedule> SelectedItem { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsSelected { get; }

        public ReactiveCommand Cancel { get; } = new();

        private void UpdateOrInstall_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems?[0] is PluginUpdateOrInstall obj)
            {
                Schedules.Add(new(obj.Target.Name, obj.Target.Description, obj.Type is PluginChangeType.Install ? Strings.Install : Strings.Update));
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove && e.OldItems?[0] is PluginUpdateOrInstall oldobj)
            {
                var value = Schedules.FirstOrDefault(i => i.Name == oldobj.Target.Name);
                if (value is not null)
                {
                    Schedules.Remove(value);
                }
            }
        }

        private void Uninstall_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action is NotifyCollectionChangedAction.Add && e.NewItems?[0] is PluginObject obj)
            {
                Schedules.Add(new(obj.PluginName, obj.Description, Strings.Uninstall));
            }
            else if (e.Action is NotifyCollectionChangedAction.Remove && e.OldItems?[0] is PluginObject oldobj)
            {
                var value = Schedules.FirstOrDefault(i => i.Name == oldobj.PluginName);
                if (value is not null)
                {
                    Schedules.Remove(value);
                }
            }
        }

        public void Dispose()
        {
            PluginChangeSchedule.Uninstall.CollectionChanged -= Uninstall_CollectionChanged;
            PluginChangeSchedule.UpdateOrInstall.CollectionChanged -= UpdateOrInstall_CollectionChanged;
        }
    }
}
