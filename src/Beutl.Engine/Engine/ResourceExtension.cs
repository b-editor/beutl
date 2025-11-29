namespace Beutl.Engine;

public static class ResourceExtension
{
    extension<T>(T? resource) where T : EngineObject.Resource
    {
        public (T Resource, int Version)? Capture()
        {
            if (resource == null)
                return null;
            return (resource, resource.Version);
        }

        public bool Compare((T Resource, int Version)? captured)
        {
            return ReferenceEquals(captured?.Resource, resource)
                   && ReferenceEquals(captured?.Resource.GetOriginal(), resource?.GetOriginal())
                   && captured?.Version == resource?.Version;
        }
    }
}
