using System.Collections.Generic;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that sets the listener for OpenAL.
    /// </summary>
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
        public static readonly DirectEditingProperty<ListenerObject, EaseProperty> GainProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, ListenerObject>(
            nameof(Gain),
            owner => owner.Gain,
            (owner, obj) => owner.Gain = obj,
            new EasePropertyMetadata("Gain", 100, Min: 0));

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenerObject"/> class.
        /// </summary>
#pragma warning disable CS8618
        public ListenerObject()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Listener;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
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

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate of the listener.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the listener.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the listener.
        /// </summary>
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the X coordinate of the listener's target position.
        /// </summary>
        public EaseProperty TargetX { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Y coordinate of the listener's target position.
        /// </summary>
        public EaseProperty TargetY { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Z coordinate of the listener's target position.
        /// </summary>
        public EaseProperty TargetZ { get; private set; }

        /// <summary>
        /// 
        /// </summary>
        public EaseProperty Gain { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            var context = Parent.Parent.AudioContext;
            var f = args.Frame;

            context!.Position = new(X[f], Y[f], Z[f]);
            context.Target = new(TargetX[f], TargetY[f], TargetZ[f]);
            context.Gain = Gain[f] / 100f;
        }
    }
}