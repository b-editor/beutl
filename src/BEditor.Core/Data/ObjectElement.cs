namespace BEditor.Data
{
    /// <summary>
    /// Represents a base class of the object.
    /// </summary>
    public abstract class ObjectElement : EffectElement
    {
        /// <summary>
        /// Filter a effect.
        /// </summary>
        public virtual bool EffectFilter(EffectElement effect) => true;
    }
}
