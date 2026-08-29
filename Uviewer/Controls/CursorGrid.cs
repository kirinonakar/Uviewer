using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace Uviewer.Controls
{
    public sealed class CursorGrid : Grid
    {
        internal void SetPointerCursor(InputCursor? cursor)
        {
            ProtectedCursor = cursor;
        }
    }
}
