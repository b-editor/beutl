using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;


namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a button in the UI
    /// </summary>
    [DataContract]
    public class ButtonComponent : PropertyElement<PropertyElementMetadata>, IEasingProperty, IObservable<object>
    {
        private List<IObserver<object>>? _List;

        /// <summary>
        /// Initializes a new instance of the <see cref="ButtonComponent"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ButtonComponent(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        private List<IObserver<object>> Collection => _List ??= new();

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<object> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), state =>
            {
                state.observer.OnCompleted();
                state.Item2.Collection.Remove(state.observer);
            });
        }
        /// <summary>
        /// Execute the command after clicking the button.
        /// </summary>
        public void Execute()
        {
            var tmp = new object();
            foreach (var observer in Collection)
            {
                try
                {
                    observer.OnNext(tmp);
                }
                catch (Exception ex)
                {
                    observer.OnError(ex);
                }
            }
        }
    }
}
