// CameraObject.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that sets the camera for OpenGL.
    /// </summary>
    public sealed class CameraObject : ObjectElement
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> XProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.X, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> YProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Y, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Z"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> ZProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(Z),
            owner => owner.Z,
            (owner, obj) => owner.Z = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Z, 1024, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="TargetX"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> TargetXProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(TargetX),
            owner => owner.TargetX,
            (owner, obj) => owner.TargetX = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.TargetX, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="TargetY"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> TargetYProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(TargetY),
            owner => owner.TargetY,
            (owner, obj) => owner.TargetY = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.TargetY, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="TargetZ"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> TargetZProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(TargetZ),
            owner => owner.TargetZ,
            (owner, obj) => owner.TargetZ = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.TargetZ, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="ZNear"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> ZNearProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(ZNear),
            owner => owner.ZNear,
            (owner, obj) => owner.ZNear = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ZNear, 0.1F, min: 0.1F, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="ZFar"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> ZFarProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(ZFar),
            owner => owner.ZFar,
            (owner, obj) => owner.ZFar = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.ZFar, 20000, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Angle"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> AngleProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(Angle),
            owner => owner.Angle,
            (owner, obj) => owner.Angle = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Angle, 0, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Fov"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, EaseProperty> FovProperty = EditingProperty.RegisterDirect<EaseProperty, CameraObject>(
            nameof(Fov),
            owner => owner.Fov,
            (owner, obj) => owner.Fov = obj,
            EditingPropertyOptions<EaseProperty>.Create(new EasePropertyMetadata(Strings.Fov, 45, 179, 1, useOptional: true)).Serialize());

        /// <summary>
        /// Defines the <see cref="Mode"/> property.
        /// </summary>
        public static readonly DirectProperty<CameraObject, CheckProperty> ModeProperty = EditingProperty.RegisterDirect<CheckProperty, CameraObject>(
            nameof(Mode),
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj,
            EditingPropertyOptions<CheckProperty>.Create(new CheckPropertyMetadata(Strings.Perspective, true)).Serialize());

        /// <summary>
        /// Initializes a new instance of the <see cref="CameraObject"/> class.
        /// </summary>
        public CameraObject()
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Camera;

        /// <summary>
        /// Gets the X coordinate of the camera.
        /// </summary>
        [AllowNull]
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the Y coordinate of the camera.
        /// </summary>
        [AllowNull]
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the camera.
        /// </summary>
        [AllowNull]
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Gets the X coordinate of the camera's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetX { get; private set; }

        /// <summary>
        /// Gets  Y coordinate of the camera's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetY { get; private set; }

        /// <summary>
        /// Gets the Z coordinate of the camera's target position.
        /// </summary>
        [AllowNull]
        public EaseProperty TargetZ { get; private set; }

        /// <summary>
        /// Gets the area to be drawn by the camera.
        /// </summary>
        [AllowNull]
        public EaseProperty ZNear { get; private set; }

        /// <summary>
        /// Gets the area to be drawn by the camera.
        /// </summary>
        [AllowNull]
        public EaseProperty ZFar { get; private set; }

        /// <summary>
        /// Gets the camera angle.
        /// </summary>
        [AllowNull]
        public EaseProperty Angle { get; private set; }

        /// <summary>
        /// Gets the camera fov.
        /// </summary>
        [AllowNull]
        public EaseProperty Fov { get; private set; }

        /// <summary>
        /// Gets whether the camera is Perspective or not.
        /// </summary>
        [AllowNull]
        public CheckProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
            int frame = args.Frame;
            var scene = Parent!.Parent!;

            if (Mode.Value)
            {
                scene.GraphicsContext!.Camera =
                    new PerspectiveCamera(new(X[frame], Y[frame], Z[frame]), scene.Width / (float)scene.Height)
                    {
                        Far = ZFar[frame],
                        Near = ZNear[frame],
                        Fov = Fov[frame],
                        Target = new(TargetX[frame], TargetY[frame], TargetZ[frame]),
                    };
            }
            else
            {
                scene.GraphicsContext!.Camera =
                    new OrthographicCamera(new(X[frame], Y[frame], Z[frame]), scene.Width, scene.Height)
                    {
                        Far = ZFar[frame],
                        Near = ZNear[frame],
                        Fov = Fov[frame],
                        Target = new(TargetX[frame], TargetY[frame], TargetZ[frame]),
                    };
            }

            var list = Parent.Effect.Where(e => e.IsEnabled).ToArray();
            for (var i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                effect.Apply(args);
            }
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
            yield return ZNear;
            yield return ZFar;
            yield return Angle;
            yield return Fov;
            yield return Mode;
        }
    }
}