using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace BEditor.Models
{
    public static class UnitTestInvoker
    {
        public static bool IsUse { get; set; }

        public static void Invoke(Action action)
        {
            if (IsUse)
            {
                var thread = new Thread(() =>
                {
                    action?.Invoke();
                });

                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
            }
            else
            {
                action?.Invoke();
            }
        }
    }
}
