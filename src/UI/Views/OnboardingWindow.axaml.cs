using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ScreenSplitter.UI.Views;

public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}