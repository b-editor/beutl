using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;

namespace BEditor.Core.Data.Primitive.Components
{
    [DataContract(Namespace = "")]
    public class LabelComponent : ComponentElement<PropertyElementMetadata>, IEasingProperty, IObservable<string>, IObserver<string>
    {
        private static readonly PropertyChangedEventArgs textArgs = new(nameof(Text));
        private List<IObserver<string>> list;
        private string text;

        private List<IObserver<string>> Collection => list ??= new();
        [DataMember]
        public string Text
        {
            get => text;
            set => SetValue(value, ref text, textArgs, () =>
            {
                foreach (var observer in Collection)
                {
                    try
                    {
                        observer.OnNext(text);
                        observer.OnCompleted();
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
            Collection.Add(observer);
            return Disposable.Create(() => Collection.Remove(observer));
        }
        public override string ToString() => $"(Text:{Text} Name:{PropertyMetadata?.Name})";
    }
}
