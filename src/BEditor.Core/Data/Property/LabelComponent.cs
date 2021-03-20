using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a project that shows a string in the UI.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Text = {Text}")]
    public class LabelComponent : PropertyElement<PropertyElementMetadata>, IEasingProperty, IObservable<string>, IObserver<string>
    {
        private static readonly PropertyChangedEventArgs _textArgs = new(nameof(Text));
        private List<IObserver<string>>? _list;
        private string _text = "";

        private List<IObserver<string>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the string to be shown.
        /// </summary>
        [DataMember]
        public string Text
        {
            get => _text;
            set => SetValue(value, ref _text, _textArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._text);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {

        }
        /// <inheritdoc/>
        public void OnError(Exception error)
        {

        }
        /// <inheritdoc/>
        public void OnNext(string value)
        {
            Text = value;
        }
        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<string> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteString(nameof(Text), Text);
        }
    }

    /// <summary>
    /// The metadata of <see cref="LabelComponent"/>.
    /// </summary>
    public record LabelComponentMetadata() : PropertyElementMetadata(string.Empty), IPropertyBuilder<LabelComponent>
    {
        /// <inheritdoc/>
        public LabelComponent Build()
        {
            return new();
        }
    }
}
