using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using System;

namespace Uviewer.Dialogs
{
    public sealed partial class GoToLineDialog : ContentDialog
    {
        public string EnteredText => InputBox.Text;
        public string DialogTitle { get; }
        public string Placeholder { get; }
        public string CurrentLineText { get; set; }

        public GoToLineDialog(int currentLine, int totalLines, string title)
        {
            this.InitializeComponent();
            DialogTitle = title;
            Placeholder = $"1 - {totalLines}";
            CurrentLineText = currentLine > 0 ? currentLine.ToString() : "1";
            
            InputBox.SelectAll();
        }

    }
}
