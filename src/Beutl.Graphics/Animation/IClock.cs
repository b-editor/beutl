using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Beutl.Animation;

public interface IClock
{
    TimeSpan BeginTime { get; }

    TimeSpan DurationTime { get; }
    
    TimeSpan CurrentTime { get; }

    TimeSpan AudioStartTime { get; }

    IClock GlobalClock { get; }
}
