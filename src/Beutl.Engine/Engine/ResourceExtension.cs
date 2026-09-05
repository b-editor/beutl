namespace Beutl.Engine;

public static class ResourceExtension
{
    extension<T>(T? resource) where T : EngineObject.Resource
    {
        /// <summary>
        /// Takes the snapshot <see cref="Compare"/> tests a later state against.
        /// </summary>
        /// <remarks>
        /// What is captured is <see cref="EngineObject.Resource.Version"/>, so a change to that number
        /// invalidates the capture and nothing else does. A resource built by hand never reconciles, and no
        /// setter on a resource moves that number, so a caller that edits one - assigning one of its own
        /// properties, adding to, removing from, reordering, or replacing an entry of a resource list, or
        /// setting a property on a child already stored - bumps its version themselves, or every recording
        /// taken from it is replayed as if nothing had changed.
        /// </remarks>
        public (T Resource, int Version)? Capture()
        {
            if (resource == null)
                return null;
            return (resource, resource.Version);
        }

        /// <summary>
        /// Whether <paramref name="captured"/> still describes this resource.
        /// </summary>
        /// <remarks>
        /// This answers <see langword="false"/> for a change to
        /// <see cref="EngineObject.Resource.Version"/> and for no other change, so a caller that edits a
        /// hand-built resource - its own properties as much as its children - has to bump that version
        /// itself to be told about it.
        /// </remarks>
        public bool Compare((T Resource, int Version)? captured)
        {
            return ReferenceEquals(captured?.Resource, resource)
                   && ReferenceEquals(captured?.Resource.GetOriginal(), resource?.GetOriginal())
                   && captured?.Version == resource?.Version;
        }
    }
}
