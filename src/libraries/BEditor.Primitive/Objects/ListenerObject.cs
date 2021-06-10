// ListenerObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that sets the listener for OpenAL.
    /// </summary>
    [Obsolete("Please do not use this class.")]
    public sealed class ListenerObject : ObjectElement
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> XProperty = CameraObject.XProperty.WithOwner<ListenerObject>(
            owner => owner.X,
            (owner, obj) => owner.X = obj);

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> YProperty = CameraObject.YProperty.WithOwner<ListenerObject>(
            owner => owner.Y,
            (owner, obj) => owner.Y = obj);

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> ZProperty = CameraObject.ZProperty.WithOwner<ListenerObject>(
            owner => owner.Z,
            (owner, obj) => owner.Z = obj);

        /// <summary>
        /// Defines the <see cref="TargetX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> TargetXProperty = CameraObject.TargetXProperty.WithOwner<ListenerObject>(
            owner => owner.TargetX,
            (owner, obj) => owner.TargetX = obj);

        /// <summary>
        /// Defines the <see cref="TargetY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> TargetYProperty = CameraObject.TargetYProperty.WithOwner<ListenerObject>(
            owner => owner.TargetY,
            (owner, obj) => owner.TargetY = obj);

        /// <summary>
        /// Defines the <see cref="TargetZ"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> TargetZProperty = CameraObject.TargetZProperty.WithOwner<ListenerObject>(
            owner => owner.TargetZ,
            (owner, obj) => owner.TargetZ = obj);

        /// <summary>
        /// Defines the <see cref="Gain"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> GainProperty = EditingProperty.RegisterDirect<EaseProperty, ListenerObject>(
            nameof(Gain),
            owner => owner.Gain,
            (owner, obj) => owner.Gain = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata("Gain", 100, min: 0)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenerObject"/> class.
        /// </summary>
        public ListenerObject()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Listener;

        /// <summary>
        /// Gets the X coordinate of the listener.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the listener.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the listener.
        /// </summary>
        [AllowNull]
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the X coordinate of the listener's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetX { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the listener's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetY { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the listener's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetZ { get; private set; }

        /// <summary>
        /// Gets the gain.
        /// </summary>
        [AllowNull]
        public EaseProperty Gain { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            var context = Parent.Parent.AudioContext;
            var f = args.Frame;

            context!.Position = new(X[f], Y[f], Z[f]);
            context.Target = new(TargetX[f], TargetY[f], TargetZ[f]);
            context.Gain = Gain[f] / 100f;
        }

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> GetProperties()
        {
            yield return X;
            yield return Y;
            yield return Z;
            yield return TargetX;
            yield return TargetY;
            yield return TargetZ;
            yield return Gain;
        }
    }
}