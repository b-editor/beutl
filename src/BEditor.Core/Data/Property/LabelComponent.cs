using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a project that shows a string in the UI.
    /// </summary>
    [DataContract]
    public class LabelComponent : PropertyElement<PropertyElementMetadata>, IEasingProperty, IObservable<string>, IObserver<string>
    {
        private static readonly PropertyChangedEventArgs _TextArgs = new(nameof(Text));
        private List<IObserver<string>>? _List;
        private string _Text = "";

        private List<IObserver<string>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets the string to be shown.
        /// </summary>
        [DataMember]
        public string Text
        {
            get => _Text;
            set => SetValue(value, ref _Text, _TextArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._Text);
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
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";
    }
}
