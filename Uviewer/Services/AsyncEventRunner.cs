using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace Uviewer.Services;

internal static class AsyncEventRunner
{
    // Invoke the delegate inside the boundary so synchronous failures are caught too.
    internal static async Task RunAsync(Func<Task> operation,
        Action<string, string, string>? showNotification = null,
        [CallerMemberName] string context = "")
    {
        try
        {
            await operation();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"{context}: {ex}");
            try { showNotification?.Invoke(ex.Message, "\uE783", "Red"); }
            catch (Exception notificationError) { Debug.WriteLine(notificationError); }
        }
    }
}
