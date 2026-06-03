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
    private readonly (string Title, string Body)[] tutorialSteps =
    [
        (
            "Choose a server folder",
            "Pick the folder where your server files will live. This is where BlockHost will look for the server jar, world files, configs, mods, and plugins."
        ),
        (
            "Select your server software",
            "Choose Paper, Spigot, Bukkit, Fabric, Forge, NeoForge, or Vanilla. This decides whether the add-ons page should show plugins, mods, or no add-ons."
        ),
        (
            "Set your RAM",
            "Use the RAM slider before starting the server. A small vanilla server can start around 4 GB, while bigger or modded servers usually need more."
        ),
        (
            "Browse add-ons",
            "Use the Add-ons tab to search Modrinth or CurseForge. Plugin results are meant for Paper-style servers, and mod results are meant for Fabric, Forge, or NeoForge."
        )
    ];

    private int tutorialStepIndex;

    public MainWindow()
    {
        InitializeComponent();
        UpdateTutorialStep();
    }

    private void GoToHome_Click(object sender, RoutedEventArgs e)
    {
        WelcomeScreen.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
    }

    private void TutorialBack_Click(object sender, RoutedEventArgs e)
    {
        if (tutorialStepIndex > 0)
        {
            tutorialStepIndex--;
            UpdateTutorialStep();
        }
    }

    private void TutorialNext_Click(object sender, RoutedEventArgs e)
    {
        if (tutorialStepIndex >= tutorialSteps.Length - 1)
        {
            GoToHome_Click(sender, e);
            return;
        }

        tutorialStepIndex++;
        UpdateTutorialStep();
    }

    private void UpdateTutorialStep()
    {
        TutorialStepLabel.Text = $"Step {tutorialStepIndex + 1} of {tutorialSteps.Length}";
        TutorialTitle.Text = tutorialSteps[tutorialStepIndex].Title;
        TutorialBody.Text = tutorialSteps[tutorialStepIndex].Body;
        TutorialBackButton.IsEnabled = tutorialStepIndex > 0;
        TutorialNextButton.Content = tutorialStepIndex == tutorialSteps.Length - 1 ? "Go to Homepage" : "Next";
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
