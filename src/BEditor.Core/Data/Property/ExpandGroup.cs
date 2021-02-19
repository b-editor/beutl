using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Data.Bindings;
using BEditor.Data.Property;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/> with expanders.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("IsExpanded = {IsExpanded}")]
    public abstract class ExpandGroup : Group, IEasingProperty, IBindable<bool>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _isExpandedArgs = new(nameof(IsExpanded));
        private bool _isOpen;
        private List<IObserver<bool>>? _list;

        private IDisposable? _bindDispose;
        private IBindable<bool>? _bindable;
        private string? _bindHint;
        #endregion


        private List<IObserver<bool>> Collection => _list ??= new();
        /// <summary>
        /// Gets or sets whether the expander is open
        /// </summary>
        [DataMember]
        public bool IsExpanded
        {
            get => _isOpen;
            set => SetValue(value, ref _isOpen, _isExpandedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._isOpen);
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
        bool IBindable<bool>.Value => IsExpanded;


        /// <summary>
        /// Initializes a new instance of the <see cref="ExpandGroup"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ExpandGroup(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }


        #region Methods

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            if (_bindHint is not null && this.GetBindable(_bindHint, out var b))
            {
                Bind(b);
            }
            _bindHint = null;
        }

        #region Ibindable

        /// <inheritdoc/>
        public void OnCompleted() { }
        /// <inheritdoc/>
        public void OnError(Exception error) { }
        /// <inheritdoc/>
        public void OnNext(bool value)
        {
            IsExpanded = value;
        }

        /// <inheritdoc/>
        public IDisposable Subscribe(IObserver<bool> observer)
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
        public void Bind(IBindable<bool>? bindable)
        {
            _bindDispose?.Dispose();
            _bindable = bindable;

            if (bindable is not null)
            {
                IsExpanded = bindable.Value;

                // bindableが変更時にthisが変更
                _bindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion
    }
}
