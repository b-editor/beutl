using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Reactive.Disposables;
using System.Runtime.Serialization;
using System.Text.Json;
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


        /// <summary>
        /// Initializes a new instance of the <see cref="ExpandGroup"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ExpandGroup(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }


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


        #region Methods

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _bindHint);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteBoolean(nameof(IsExpanded), IsExpanded);
            writer.WriteString(nameof(BindHint), BindHint);
            base.GetObjectData(writer);
        }

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
            return BindingHelper.Subscribe(Collection, observer, IsExpanded);
        }

        /// <inheritdoc/>
        public void Bind(IBindable<bool>? bindable)
        {
            IsExpanded = this.Bind(bindable, out _bindable, ref _bindDispose);
        }

        #endregion
    }
}
