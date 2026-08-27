namespace Beutl.Engine;

public static class ResourceExtension
{
    extension<T>(T? resource) where T : EngineObject.Resource
    {
        /// <remarks>
        /// The captured version is <see cref="EngineObject.Resource.EffectiveVersion"/>, so a recording
        /// taken from a detached resource is also dropped when the caller reaches past the setters and
        /// edits the resource list itself or a child already in it.
        /// </remarks>
        public (T Resource, int Version)? Capture()
        {
            if (resource == null)
                return null;
            return (resource, resource.EffectiveVersion);
        }

        public bool Compare((T Resource, int Version)? captured)
        {
            return ReferenceEquals(captured?.Resource, resource)
                   && ReferenceEquals(captured?.Resource.GetOriginal(), resource?.GetOriginal())
                   && captured?.Version == resource?.EffectiveVersion;
        }
    }
}
