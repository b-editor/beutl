using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BEditor.Core.Data.Property
{
    [DataContract]
    public class LabelComponent : PropertyElement<PropertyElementMetadata>, IEasingProperty, IObservable<string>, IObserver<string>
    {
        private static readonly PropertyChangedEventArgs _TextArgs = new(nameof(Text));
        private List<IObserver<string>>? _List;
        private string _Text = "";

        private List<IObserver<string>> Collection => _List ??= new();
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

        public void OnCompleted()
        {

        }
        public void OnError(Exception error)
        {

        }
        public void OnNext(string value)
        {
            Text = value;
        }
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
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";
    }
}
