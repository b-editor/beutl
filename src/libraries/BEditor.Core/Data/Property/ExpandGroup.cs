// ExpandGroup.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

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
    public abstract class ExpandGroup : Group, IBindable<bool>
    {
        private static readonly PropertyChangedEventArgs _isExpandedArgs = new(nameof(IsExpanded));
        private bool _isOpen;
        private List<IObserver<bool>>? _list;
        private IDisposable? _bindDispose;
        private IBindable<bool>? _bindable;
        private Guid? _targetID;

        /// <summary>
        /// Initializes a new instance of the <see cref="ExpandGroup"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        protected ExpandGroup(PropertyElementMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        }

        /// <summary>
        /// Gets or sets a value indicating whether the expander is open or not.
        /// </summary>
        public bool IsExpanded
        {
            get => _isOpen;
            set
            {
                if (SetAndRaise(value, ref _isOpen, _isExpandedArgs))
                {
                    foreach (var observer in Collection)
                    {
                        try
                        {
                            observer.OnNext(_isOpen);
                        }
                        catch (Exception ex)
                        {
                            observer.OnError(ex);
                        }
                    }
                }
            }
        }

        /// <inheritdoc/>
        public Guid? TargetID
        {
            get => _bindable?.Id;
            private set => _targetID = value;
        }

        /// <inheritdoc/>
        bool IBindable<bool>.Value => IsExpanded;

        private List<IObserver<bool>> Collection => _list ??= new();

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            writer.WriteBoolean(nameof(IsExpanded), IsExpanded);

            if (TargetID is not null)
            {
                writer.WriteString(nameof(TargetID), (Guid)TargetID);
            }

            base.GetObjectData(writer);
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            IsExpanded = element.TryGetProperty(nameof(IsExpanded), out var value) && value.GetBoolean();
            TargetID = element.TryGetProperty(nameof(TargetID), out var bind) && bind.TryGetGuid(out var guid) ? guid : null;
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
            this.AutoLoad(ref _targetID);
        }
    }
}