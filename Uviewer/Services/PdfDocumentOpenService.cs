using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    internal static class PdfDocumentOpenService
    {
        public static async Task<bool> OpenAsync(
            Func<string?, bool, Task> loadAsync,
            Func<bool, Task<string?>> requestPasswordAsync)
        {
            string? password = null;
            bool usePdfium = false;
            bool isRetry = false;

            while (true)
            {
                try
                {
                    // Encryption does not imply an opening password: permissions-only
                    // PDFs have an empty user password and should open without a dialog.
                    await loadAsync(password, usePdfium);
                    return true;
                }
                catch (Exception ex) when (!usePdfium && IsWindowsPdfFailure(ex))
                {
                    // PDFium also handles encryption variants unsupported by Windows.
                    // Let the rendering backend validate the empty password first.
                    usePdfium = true;
                }
                catch (PdfiumException ex) when (usePdfium && ex.Error == PdfiumError.PasswordProtected)
                {
                    password = await requestPasswordAsync(isRetry);
                    if (password == null) return false;
                    isRetry = true;
                }
            }
        }

        private static bool IsWindowsPdfFailure(Exception exception) =>
            exception is not OperationCanceledException &&
            (exception is ExternalException ||
             exception.HResult == unchecked((int)0x8007052B) ||
             exception.HResult == unchecked((int)0x80004005) ||
             exception.HResult == unchecked((int)0x80048040));
    }
}
