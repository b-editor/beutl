using System.Threading;

namespace BEditor
{
    public class CustomSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            d?.Invoke(state);
        }
        public override void Send(SendOrPostCallback d, object? state)
        {
            d?.Invoke(state);
        }
    }
}