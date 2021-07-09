using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Input;

using BEditor.ViewModels;

using CommandNames = BEditor.Properties.CommandName;
using KeyGesture = Avalonia.Input.KeyGesture;

namespace BEditor.Models
{
    public record KeyBindingCommand(string Name, string CommandName, ICommand Command);

    public sealed class KeyBindingModel
    {
        private KeyGesture? _keyGesture;

        static KeyBindingModel()
        {
            var file = Path.Combine(ServicesLocator.GetUserFolder(), "keybindings.json");
            if (!File.Exists(file))
            {
                Bindings = new(CreateDefault());
            }
            else
            {
                using var reader = new StreamReader(file);
                Bindings = new(JsonSerializer.Deserialize<IEnumerable<KeyBindingModel>>(reader.ReadToEnd(), Packaging.PackageFile._serializerOptions) ?? CreateDefault());
            }
        }

        public KeyBindingModel()
        {
        }

        public KeyBindingModel(string key, string command)
        {
            Key = key;
            CommandName = command;
        }

        public static ObservableCollection<KeyBindingModel> Bindings { get; }

        public static KeyBindingCommand[] AllCommands { get; } =
        {
            new("OPEN", CommandNames.OPEN, MainWindowViewModel.Current.Open),
            new("SAVE", CommandNames.SAVE, MainWindowViewModel.Current.Save),
            new("UNDO", CommandNames.UNDO, MainWindowViewModel.Current.Undo),
            new("REDO", CommandNames.REDO, MainWindowViewModel.Current.Redo),
            new("REMOVE", CommandNames.REMOVE, MainWindowViewModel.Current.Remove),
            new("COPY", CommandNames.COPY, MainWindowViewModel.Current.Copy),
            new("CUT", CommandNames.CUT, MainWindowViewModel.Current.Cut),
            new("PASTE", CommandNames.PASTE, MainWindowViewModel.Current.Paste),
            new("NEW", CommandNames.NEW, MainWindowViewModel.Current.New),
            new("IMAGE_OUT", CommandNames.IMAGE_OUT, MainWindowViewModel.Current.ImageOutput),
            new("VIDEO_OUT", CommandNames.VIDEO_OUT, MainWindowViewModel.Current.VideoOutput),
            new("PLAY_PAUSE", CommandNames.PLAY_PAUSE, MainWindowViewModel.Current.Previewer.PlayPause),
            new("PREVIOUS", CommandNames.PREVIOUS, MainWindowViewModel.Current.Previewer.MoveToPrevious),
            new("NEXT", CommandNames.NEXT, MainWindowViewModel.Current.Previewer.MoveToNext),
            new("TOP", CommandNames.TOP, MainWindowViewModel.Current.Previewer.MoveToTop),
            new("END", CommandNames.END, MainWindowViewModel.Current.Previewer.MoveToEnd),
        };

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("command")]
        public string CommandName { get; set; } = string.Empty;

        [JsonIgnore]
        public KeyBindingCommand? Command => Array.Find(AllCommands, i => i.Name == CommandName);

        public static void Save()
        {
            using var reader = new StreamWriter(Path.Combine(ServicesLocator.GetUserFolder(), "keybindings.json"));
            var json = JsonSerializer.Serialize(Bindings, Packaging.PackageFile._serializerOptions);
            reader.Write(json);
        }

        public static async Task SaveAsync()
        {
            await using var stream = new FileStream(Path.Combine(ServicesLocator.GetUserFolder(), "keybindings.json"), FileMode.Create);
            await JsonSerializer.SerializeAsync(stream, Bindings, Packaging.PackageFile._serializerOptions);
        }

        public KeyGesture ToKeyGesture()
        {
            if (_keyGesture is not null && _keyGesture.ToString() == Key)
            {
                return _keyGesture;
            }
            else
            {
                return _keyGesture = KeyGesture.Parse(Key);
            }
        }

        private static IEnumerable<KeyBindingModel> CreateDefault()
        {
            yield return new("Ctrl+O", "OPEN");
            yield return new("Ctrl+S", "SAVE");
            yield return new("Ctrl+Z", "UNDO");
            yield return new("Ctrl+Y", "REDO");
            yield return new("Delete", "REMOVE");
            yield return new("Ctrl+C", "COPY");
            yield return new("Ctrl+X", "CUT");
            yield return new("Ctrl+V", "PASTE");
            yield return new("Ctrl+N", "NEW");
            yield return new("Space", "PLAY_PAUSE");
            yield return new("Left", "PREVIOUS");
            yield return new("Right", "NEXT");
            yield return new("F10", "TOP");
            yield return new("F12", "END");
            yield return new("F7", "IMAGE_OUT");
            yield return new("F8", "VIDEO_OUT");
        }
    }
}