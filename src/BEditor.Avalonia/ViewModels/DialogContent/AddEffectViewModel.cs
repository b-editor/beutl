using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

using Reactive.Bindings;

namespace BEditor.ViewModels.DialogContent
{
    public sealed class AddEffectViewModel
    {
        private ClipElement? _selectedClip;

        public AddEffectViewModel()
        {
            Scene.Value = Project.PreviewScene;
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
                    msg.Snackbar(Strings.NoClipIsSelected);
                    return;
                }

                _selectedClip.AddEffect(Effect.Value.CreateFunc()).Execute();
            });
        }

        public Project Project { get; } = AppModel.Current.Project;

        public ReactiveProperty<EffectMetadata> Effect { get; } = new();

        public ReactiveProperty<Scene> Scene { get; } = new();

        public ReactiveProperty<string> ClipId { get; } = new();

        public ReactiveCommand Create { get; } = new();
    }
}