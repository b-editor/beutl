namespace Beutl.Engine;

public static class ResourceExtension
{
    extension<T>(T? resource) where T : EngineObject.Resource
    {
        /// <summary>
        /// Takes the snapshot <see cref="Compare"/> tests a later state against.
        /// </summary>
        /// <remarks>See <see cref="EngineObject.Resource.Version"/> for what invalidates it.</remarks>
        public (T Resource, int Version)? Capture()
        {
            if (resource == null)
                return null;
            return (resource, resource.Version);
        }

        /// <summary>
        /// Whether <paramref name="captured"/> still describes this resource.
        /// </summary>
        /// <remarks>See <see cref="EngineObject.Resource.Version"/> for what invalidates it.</remarks>
        public bool Compare((T Resource, int Version)? captured)
        {
            return ReferenceEquals(captured?.Resource, resource)
                   && captured?.Version == resource?.Version;
        }
    }
}
