using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a checkbox property.
    /// </summary>
    [DataContract]
    public class CheckProperty : PropertyElement<CheckPropertyMetadata>, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _checkedArgs = new(nameof(IsChecked));
        private bool _isChecked;
        private List<IObserver<bool>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<bool>? _bindable;
        private string? _bindHint;

        #endregion


        /// <summary>
        /// Initializes new instance of the <see cref="CheckProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _isChecked = metadata.DefaultIsChecked;
        }

        private List<IObserver<bool>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets the value of whether the item is checked or not.
        /// </summary>
        [DataMember]
        public bool IsChecked
        {
            get => _isChecked;
            set => SetValue(value, ref _isChecked, _checkedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._isChecked);
                    }
                    catch (Exception ex)
                    {
                        observer.OnError(ex);
                    }
                }
            });
        }
        /// <inheritdoc/>
        [DataMember]
        public string? BindHint
        {
            get => _bindable?.GetString();
            private set => _bindHint = value;
        }
        /// <inheritdoc/>
        public bool Value => IsChecked;


        #region Methods

        #region IBindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            IsChecked = value;
        }

        /// <inheritdoc/>
        /// <exception cref="ArgumentNullException"><paramref name="observer"/> is <see langword="null"/>.</exception>
        public IDisposable Subscribe(IObserver<bool> observer)
        {
            if (observer is null) throw new ArgumentNullException(nameof(observer));

            Collection.Add(observer);
            return Disposable.Create((observer, this), o =>
            {
                o.observer.OnCompleted();
                o.Item2.Collection.Remove(o.observer);
            });
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                IsChecked = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null)
            {
                if (this.GetBindable(_bindHint, out var b))
                {
                    Bind(b);
                }
            }
            _bindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsChecked:{IsChecked} Name:{PropertyMetadata?.Name})";
        /// <summary>
        /// Create a command to change whether it is checked or not.
        /// </summary>
        /// <param name="value">New value for IsChecked</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeIsChecked(bool value) => new ChangeCheckedCommand(this, value);

        #endregion


        private sealed class ChangeCheckedCommand : IRecordCommand
        {
            private readonly CheckProperty _Property;
            private readonly bool _Value;

            public ChangeCheckedCommand(CheckProperty property, bool value)
            {
                _Property = property;
                _Value = value;
            }

            public string Name => CommandName.ChangeIsChecked;

            public void Do() => _Property.IsChecked = _Value;
            public void Redo() => Do();
            public void Undo() => _Property.IsChecked = !_Value;
        }
    }

    /// <summary>
    /// Represents the metadata of a <see cref="CheckProperty"/>.
    /// </summary>
    public record CheckPropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="CheckPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultIsChecked">Default value for <see cref="CheckProperty.IsChecked"/>.</param>
        public CheckPropertyMetadata(string Name, bool DefaultIsChecked = false) : base(Name)
        {
            this.DefaultIsChecked = DefaultIsChecked;
        }

        /// <summary>
        /// Get the default value of <see cref="CheckProperty.IsChecked"/>.
        /// </summary>
        public bool DefaultIsChecked { get; init; }
    }
}
