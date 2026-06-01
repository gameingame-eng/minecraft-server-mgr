using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.IO;
using System.Windows.Forms;

namespace McServerManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void RUnJavaAndDoyouthingy(object sender, RoutedEventArgs e)
    {
        Status.Text = "Status: Starting server...";
        await Task.Delay(5000);
        Status.Text = "Status: Server is running.";
    }

    private async void PleaseStopTheServer(object sender, RoutedEventArgs e)
    {
        Status.Text = "Status: Stopping server...";
        await Task.Delay(5000);
        Status.Text = "Status: Idle.";
    }
    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ServerFolderTextBox.Text = dialog.SelectedPath;
        }
    }
    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FindName("RamLabel") is TextBlock ramLabel && sender is Slider ramSlider)
        {
            ramLabel.Text = $"{(int)ramSlider.Value} GB";
        }
    }
}
