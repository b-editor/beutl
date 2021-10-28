using System;
using System.Collections.ObjectModel;

using BEditor.Data;
using BEditor.LangResources;
using BEditor.Models;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public sealed class AddEffectViewModel
    {
        private ClipElement? _selectedClip;

        public AddEffectViewModel()
        {
            Scene.Value = Project.CurrentScene;
            ClipId.Value = Scene.Value.SelectItem?.Id.ToString() ?? string.Empty;

            ClipId.SetValidateNotifyError(value =>
            {
                if (Guid.TryParse(value, out var id))
                {
                    _selectedClip = Scene.Value.Find(id);

                    if (_selectedClip is null)
                    {
                        return Strings.SpecifiedClipIsNotFound;
                    }
                }
                else
                {
                    return Strings.InvalidIdCanUseGUIDAndUUID;
                }

                return null;
            });

            Create.Subscribe(() =>
            {
                var msg = AppModel.Current.Message;
                if (_selectedClip is null)
                {
                    msg.Snackbar(Strings.NoClipIsSelected, string.Empty);
                    return;
                }

                _selectedClip.AddEffect(Effect.Value.CreateFunc()).Execute();
            });

            Scene.Subscribe(s =>
            {
                Clips.Clear();
                if (s is null) return;

                foreach (var item in s.Datas)
                {
                    Clips.Add(item.Id.ToString());
                }
            });
        }

        public Project Project { get; } = AppModel.Current.Project;

        public ReactiveProperty<EffectMetadata> Effect { get; } = new();

        public ReactiveProperty<Scene> Scene { get; } = new();

        public ReactiveProperty<string> ClipId { get; } = new();

        public ObservableCollection<string> Clips { get; } = new();

        public ReactiveCommand Create { get; } = new();
    }
}