using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Core.Data.Bindings;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/> with expanders.
    /// </summary>
    [DataContract]
    public abstract class ExpandGroup : Group, IEasingProperty, IBindable<bool>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _IsExpandedArgs = new(nameof(IsExpanded));
        private bool _IsOpen;
        private List<IObserver<bool>>? _List;

        private IDisposable? _BindDispose;
        private IBindable<bool>? _Bindable;
        private string? _BindHint;
        #endregion


        private List<IObserver<bool>> Collection => _List ??= new();
        /// <summary>
        /// Gets or sets whether the expander is open
        /// </summary>
        [DataMember]
        public bool IsExpanded
        {
            get => _IsOpen;
            set => SetValue(value, ref _IsOpen, _IsExpandedArgs, this, state =>
            {
                foreach (var observer in state.Collection)
                {
                    try
                    {
                        observer.OnNext(state._IsOpen);
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
        public bool Value => IsExpanded;


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
            if (_BindHint is not null && this.GetBindable(_BindHint, out var b))
            {
                Bind(b);
            }
            _BindHint = null;
        }
        /// <inheritdoc/>
        public override string ToString() => $"(IsExpanded:{IsExpanded} Name:{PropertyMetadata?.Name})";

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
            _BindDispose?.Dispose();
            _Bindable = bindable;

            if (bindable is not null)
            {
                IsExpanded = bindable.Value;

                // bindableが変更時にthisが変更
                _BindDispose = bindable.Subscribe(this);
            }
        }

        #endregion

        #endregion
    }
}
