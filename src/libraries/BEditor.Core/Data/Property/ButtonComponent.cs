// ButtonComponent.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Reactive.Disposables;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a button in the UI.
    /// </summary>
    public class ButtonComponent : PropertyElement<ButtonComponentMetadata>, IEasingProperty, IObservable<object>
    {
        private List<IObserver<object>>? _list;

        /// <summary>
        /// Initializes a new instance of the <see cref="ButtonComponent"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ButtonComponent(ButtonComponentMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        private List<IObserver<object>> Collection => _list ??= new();

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