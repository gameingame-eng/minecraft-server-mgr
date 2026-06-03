using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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
    private int RamAllocation;
    private string ServerFilePath = string.Empty;
    private string ServerJarFileName = "server.jar";
    private Process? serverProcess;
    private readonly StringBuilder consoleBuffer = new();

    public MainWindow()
    {
        InitializeComponent();
        if (FindName("ramSlider") is Slider rs)
        {
            RamAllocation = (int)rs.Value;
        }
        else
        {
        RamAllocation = 0;
        }

        if (FindName("ServerJarNameTextBox") is System.Windows.Controls.TextBox jarNameBox)
        {
            ServerJarFileName = jarNameBox.Text.Trim();
        }

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

    private void AppendConsoleLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            consoleBuffer.AppendLine(line);
            if (FindName("ConsoleOutputTextBox") is System.Windows.Controls.TextBox consoleOutput)
            {
                consoleOutput.Text = consoleBuffer.ToString();
                consoleOutput.ScrollToEnd();
            }
        });
    }

    private async void RUnJavaAndDoyouthingy(object sender, RoutedEventArgs e)
    {
        if (serverProcess is { HasExited: false })
        {
            Status.Text = "Status: Server is already running.";
            return;
        }

        if (string.IsNullOrWhiteSpace(ServerFilePath) || !Directory.Exists(ServerFilePath))
        {
            Status.Text = "Status: Pick a valid server folder first.";
            return;
        }

        ServerJarFileName = FindName("ServerJarNameTextBox") is System.Windows.Controls.TextBox jarNameBox
            ? jarNameBox.Text.Trim()
            : ServerJarFileName;

        if (string.IsNullOrWhiteSpace(ServerJarFileName))
        {
            ServerJarFileName = "server.jar";
        }

        string serverJarPath = Path.Combine(ServerFilePath, ServerJarFileName);
        if (!File.Exists(serverJarPath))
        {
            Status.Text = $"Status: {ServerJarFileName} was not found in the selected folder.";
            AppendConsoleLine($"[BlockHost] Could not start server. Missing: {serverJarPath}");
            return;
        }

        RamAllocation = FindName("RamSlider") is Slider ramSlider ? (int)ramSlider.Value : RamAllocation;

        var psi = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-Xms{RamAllocation}G -Xmx{RamAllocation}G -jar \"{serverJarPath}\" nogui",
            WorkingDirectory = ServerFilePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        var process = new Process
        {
            StartInfo = psi,
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                AppendConsoleLine(args.Data);
            }
        };

        process.ErrorDataReceived += (_, args) =>
        {
            if (args.Data != null)
            {
                AppendConsoleLine($"[stderr] {args.Data}");
            }
        };

        process.Exited += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                Status.Text = "Status: Idle.";
                AppendConsoleLine("[BlockHost] Server process exited.");
                serverProcess = null;
            });
        };

        try
        {
            Status.Text = "Status: Starting server...";
            AppendConsoleLine("[BlockHost] Starting server...");

            if (!process.Start())
            {
                Status.Text = "Status: Failed to start server.";
                AppendConsoleLine("[BlockHost] Process start returned false.");
                return;
            }

            serverProcess = process;
            Status.Text = $"Status: Server running (PID {serverProcess.Id}).";
            AppendConsoleLine($"[BlockHost] Server started. PID: {serverProcess.Id}");

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch (System.Exception ex)
        {
            Status.Text = "Status: Failed to start server.";
            AppendConsoleLine($"[BlockHost] Start failed: {ex.Message}");
        }
    }

    private async void PleaseStopTheServer(object sender, RoutedEventArgs e)
    {
        if (serverProcess is not { HasExited: false })
        {
            Status.Text = "Status: Idle.";
            AppendConsoleLine("[BlockHost] No running server to stop.");
            return;
        }

        Status.Text = "Status: Stopping server...";
        AppendConsoleLine("[BlockHost] Sending stop command...");

        try
        {
            await serverProcess.StandardInput.WriteLineAsync("stop");
            await serverProcess.StandardInput.FlushAsync();

            bool exited = await Task.Run(() => serverProcess.WaitForExit(15000));
            if (!exited)
            {
                AppendConsoleLine("[BlockHost] Server did not exit in time. Killing process.");
                serverProcess.Kill(entireProcessTree: true);
            }
        }
        catch (System.Exception ex)
        {
            AppendConsoleLine($"[BlockHost] Stop failed: {ex.Message}");
            try
            {
                if (!serverProcess.HasExited)
                {
                    serverProcess.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Ignore secondary stop failures.
            }
        }
        finally
        {
            Status.Text = "Status: Idle.";
            serverProcess = null;
        }
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog();

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            ServerFolderTextBox.Text = dialog.SelectedPath;
            ServerFilePath = dialog.SelectedPath;
        }
    }

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FindName("RamLabel") is TextBlock ramLabel && sender is Slider ramSlider)
        {
            RamAllocation = (int)ramSlider.Value;
            ramLabel.Text = $"{RamAllocation} GB";
        }
    }
}
