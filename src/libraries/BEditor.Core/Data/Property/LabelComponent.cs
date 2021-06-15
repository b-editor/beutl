// LabelComponent.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Text.Json;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a project that shows a string in the UI.
    /// </summary>
    [DebuggerDisplay("Text = {Text}")]
    public class LabelComponent : PropertyElement<PropertyElementMetadata>, IEasingProperty, IObservable<string>, IObserver<string>
    {
        private static readonly PropertyChangedEventArgs _textArgs = new(nameof(Text));
        private List<IObserver<string>>? _list;
        private string _text = string.Empty;

        /// <summary>
        /// Gets or sets the string to be shown.
        /// </summary>
        public string Text
        {
            get => _text;
            set
            {
                if (SetAndRaise(value, ref _text, _textArgs))
                {
                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(_text);
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                        }
                    }
                }
            }
        }

        private List<IObserver<string>> Collection => _list ??= new();

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

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Text = element.TryGetProperty(nameof(Text), out var value) ? value.GetString() ?? string.Empty : string.Empty;
        }
    }
}