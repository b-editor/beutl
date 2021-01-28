using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a checkbox property.
    /// </summary>
    [DataContract]
    public class CheckProperty : PropertyElement<CheckPropertyMetadata>, IEasingProperty, IBindable<bool>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _CheckedArgs = new(nameof(IsChecked));
        private bool _IsChecked;
        private List<IObserver<bool>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<bool>? _Bindable;
        private string? _BindHint;

        #endregion


        /// <summary>
        /// Initializes new instance of the <see cref="CheckProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public CheckProperty(CheckPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            _IsChecked = metadata.DefaultIsChecked;
        }

        private List<IObserver<bool>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets the value of whether the item is checked or not.
        /// </summary>
        [DataMember]
        public bool IsChecked
        {
            get => _IsChecked;
            set => SetValue(value, ref _IsChecked, _CheckedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._IsChecked);
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
            get => _Bindable?.GetString();
            private set => _BindHint = value;
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
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                IsChecked = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_BindHint is not null)
            {
                if (this.GetBindable(_BindHint, out var b))
                {
                    Bind(b);
                }
            }
            _BindHint = null;
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
