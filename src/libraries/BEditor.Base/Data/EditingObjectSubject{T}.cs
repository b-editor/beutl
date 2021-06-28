// EditingObjectSubject{T}.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace BEditor.Data
{
    internal sealed class EditingObjectSubject<T> : SubjectBase<T>
    {
        private readonly EditingProperty<T> _property;
        private readonly List<IObserver<T>> _list = new();
        private bool _isDisposed;
        private IEditingObject? _object;

        public EditingObjectSubject(IEditingObject o, EditingProperty<T> property)
        {
            _object = o;
            _property = property;
            o.PropertyChanged += Object_PropertyChanged;
        }

        public override bool HasObservers { get; }

        public override bool IsDisposed => _isDisposed;

        public override void Dispose()
        {
            if (!_isDisposed)
            {
                _object!.PropertyChanged -= Object_PropertyChanged;
                _list.Clear();
                _object = null;
                _isDisposed = true;
            }
        }

        public override void OnCompleted()
        {
        }

        public override void OnError(Exception error)
        {
        }

        public override void OnNext(T value)
        {
            _object?.SetValue(_property, value);
        }

        public override IDisposable Subscribe(IObserver<T> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));
            if (_isDisposed) throw new ObjectDisposedException(nameof(EditingObjectSubject<T>));

            _list.Add(observer);

            try
            {
                observer.OnNext(_object!.GetValue(_property));
            }
            catch (Exception ex)
            {
                observer.OnError(ex);
            }

            return Disposable.Create((observer, _list), o =>
            {
                o.observer.OnCompleted();
                o._list.Remove(o.observer);
            });
        }

        private void Object_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != _property.Name) return;
            if (_isDisposed) throw new ObjectDisposedException(nameof(EditingObjectSubject<T>));

            var value = _object!.GetValue(_property);
            foreach (var item in _list)
            {
                try
                {
                    item.OnNext(value);
                }
                catch (Exception ex)
                {
                    item.OnError(ex);
                }
            }
        }
    }
}