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
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = CameraObject.XMetadata;
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = CameraObject.YMetadata;
        /// <summary>
        /// Represents <see cref="Z"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZMetadata = CameraObject.ZMetadata;
        /// <summary>
        /// Represents <see cref="TargetX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetXMetadata = CameraObject.TargetXMetadata;
        /// <summary>
        /// Represents <see cref="TargetY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetYMetadata = CameraObject.TargetYMetadata;
        /// <summary>
        /// Represents <see cref="TargetZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetZMetadata = CameraObject.TargetZMetadata;
        /// <summary>
        /// Represents <see cref="Gain"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata GainMetadata = new("Gain", 100, float.NaN, 0);

        /// <summary>
        /// Initializes a new instance of the <see cref="ListenerObject"/> class.
        /// </summary>
        public ListenerObject()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            TargetX = new(TargetXMetadata);
            TargetY = new(TargetYMetadata);
            TargetZ = new(TargetZMetadata);
            Gain = new(GainMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.Listener;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X,
            Y,
            Z,
            TargetX,
            TargetY,
            TargetZ,
            Gain
        };
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate of the listener.
        /// </summary>
        [DataMember]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the listener.
        /// </summary>
        [DataMember]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the listener.
        /// </summary>
        [DataMember]
        public EaseProperty Z { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the X coordinate of the listener's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Y coordinate of the listener's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Z coordinate of the listener's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetZ { get; private set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
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
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            TargetX.Load(TargetXMetadata);
            TargetY.Load(TargetYMetadata);
            TargetZ.Load(TargetZMetadata);
            Gain.Load(GainMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            X.Unload();
            Y.Unload();
            Z.Unload();
            TargetX.Unload();
            TargetY.Unload();
            TargetZ.Unload();
            Gain.Unload();
        }
    }
}
