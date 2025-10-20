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
    }
}
