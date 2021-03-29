using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

using BEditor.Data.Bindings;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a base class for grouping <see cref="PropertyElement"/> with expanders.
    /// </summary>
    [DebuggerDisplay("IsExpanded = {IsExpanded}")]
    public abstract class ExpandGroup : Group, IEasingProperty, IBindable<bool>
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _isExpandedArgs = new(nameof(IsExpanded));
        private bool _isOpen;
        private List<IObserver<bool>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<bool>? _bindable;
        private string? _targetHint;
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

        /// <summary>
        /// Gets or sets a value indicating whether the expander is open or not.
        /// </summary>
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
        public string? TargetHint
        {
            get => _bindable?.ToString("#");
            private set => _targetHint = value;
        }

        /// <inheritdoc/>
        bool IBindable<bool>.Value => IsExpanded;

        private List<IObserver<bool>> Collection => _list ??= new();

        #region Methods

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteBoolean(nameof(IsExpanded), IsExpanded);
            writer.WriteString(nameof(TargetHint), TargetHint);
            base.GetObjectData(writer);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            IsExpanded = element.TryGetProperty(nameof(IsExpanded), out var value) && value.GetBoolean();
            TargetHint = element.TryGetProperty(nameof(TargetHint), out var bind) ? bind.GetString() : null;
            base.SetObjectData(element);
        }

        /// <inheritdoc/>
        public void OnCompleted()
        {
        }

        /// <inheritdoc/>
        public void OnError(Exception error)
        {
        }

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

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            this.AutoLoad(ref _targetHint);
        }

        #endregion
    }
}
