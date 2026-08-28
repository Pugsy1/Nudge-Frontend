using System.Windows.Controls;

namespace Nudge.App.Views;

/// <summary>Code-behind for the library grid. Everything lives in <see cref="ViewModels.LibraryViewModel"/>; nothing here needs a live element.</summary>
public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();
}
