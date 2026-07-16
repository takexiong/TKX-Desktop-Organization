using System.Diagnostics;
using System.Windows;
using System.Windows.Navigation;

namespace DesktopOrganizer;

public partial class UpdateFailDialog : Window
{
    public UpdateFailDialog(string errorMessage)
    {
        InitializeComponent();
        ErrorText.Text = $"检查更新失败：\n{errorMessage}";
    }

    private void Link_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = e.Uri.AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch
        {
            // ignore
        }

        e.Handled = true;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
