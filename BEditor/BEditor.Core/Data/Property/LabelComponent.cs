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
        private static readonly PropertyChangedEventArgs textArgs = new(nameof(Text));
        private List<IObserver<string>> list;
        private string text;

        private List<IObserver<string>> Collection => list ??= new();
        [DataMember]
        public string Text
        {
            get => text;
            set => SetValue(value, ref text, textArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state.text);
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
