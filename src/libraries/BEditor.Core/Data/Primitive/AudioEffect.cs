// AudioEffect.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Media;
using BEditor.Media.PCM;

namespace BEditor.Data.Primitive
{
    /// <summary>
    /// Represents an effect that can be added to an <see cref="AudioObject"/>.
    /// </summary>
    public abstract class AudioEffect : EffectElement
    {
        /// <inheritdoc cref="Apply(EffectApplyArgs{Sound{StereoPCMFloat}})"/>
        public abstract void Apply(EffectApplyArgs<Sound<StereoPCMFloat>> args);

        /// <inheritdoc/>
        public override void Apply(EffectApplyArgs args)
        {
        }
    }
}