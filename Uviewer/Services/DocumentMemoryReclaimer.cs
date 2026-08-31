using System;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal static class DocumentMemoryReclaimer
    {
        public static Task CollectAsync(bool reduceMemory) => Task.Run(() =>
        {
            // WinRT finalizers may need the UI dispatcher; never wait for them on it.
            GC.Collect();
            GC.WaitForPendingFinalizers();

            if (reduceMemory)
            {
                // A normal collection can leave gigabytes of empty committed regions
                // after decoding images. Return those regions only when entering tray idle.
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive,
                    blocking: true, compacting: true);
            }
        });
    }
}
