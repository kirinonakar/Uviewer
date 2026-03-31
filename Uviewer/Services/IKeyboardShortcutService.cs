using Microsoft.UI.Xaml.Input;
using System.Threading.Tasks;

namespace Uviewer.Services
{
    public interface IKeyboardShortcutService
    {
        Task HandlePreviewKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions);
        Task HandleKeyDownAsync(object sender, KeyRoutedEventArgs e, IKeyboardShortcutActions actions);
    }
}
