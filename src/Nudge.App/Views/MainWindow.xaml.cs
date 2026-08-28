using System.Windows;
using Nudge.App.ViewModels;

namespace Nudge.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel shellViewModel)
    {
        InitializeComponent();

        DataContext = shellViewModel;
    }
}
