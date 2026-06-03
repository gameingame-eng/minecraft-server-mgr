using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Forms;

namespace McServerManager;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private static readonly HttpClient httpClient = new();

    private static readonly string settingsDirectory = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BlockHost");

    private static readonly string settingsFilePath = System.IO.Path.Combine(settingsDirectory, "settings.json");
    private static readonly string oldTextSettingsFilePath = System.IO.Path.Combine(settingsDirectory, "settings.txt");

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
            "Use the Add-ons tab to search Modrinth. Plugin results are meant for Paper-style servers, and mod results are meant for Fabric, Forge, or NeoForge."
        )
    ];

    private int tutorialStepIndex;
    private int ramAllocation;
    private string serverFilePath = string.Empty;
    private string serverJarFileName = "server.jar";
    private Process? serverProcess;
    private readonly StringBuilder consoleBuffer = new();
    private AppSettings appSettings = new();
    private bool isLoadingSettings = true;
    private bool settingsLoadedSuccessfully = true;

    public MainWindow()
    {
        InitializeComponent();

        ramAllocation = RamSlider is not null ? (int)RamSlider.Value : 0;
        serverJarFileName = string.IsNullOrWhiteSpace(ServerJarNameTextBox.Text)
            ? "server.jar"
            : ServerJarNameTextBox.Text.Trim();

        ApplyStartupState();
        Closing += MainWindow_Closing;
    }

    private void ApplyStartupState()
    {
        isLoadingSettings = true;

        appSettings = LoadSettings(out settingsLoadedSuccessfully);
        ApplyTheme(appSettings.Theme);
        ThemeComboBox.SelectedIndex = string.Equals(appSettings.Theme, "Dark", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        tutorialStepIndex = Math.Clamp(appSettings.TutorialStepIndex, 0, tutorialSteps.Length - 1);

        if (appSettings.HasCompletedTutorial)
        {
            ShowHomePage();
        }
        else
        {
            ShowWelcomeScreen();
            UpdateTutorialStep();
        }

        isLoadingSettings = false;
    }

    private void GoToHome_Click(object sender, RoutedEventArgs e)
    {
        appSettings.HasCompletedTutorial = true;
        appSettings.TutorialStepIndex = tutorialSteps.Length - 1;
        SaveSettings(appSettings);
        ShowHomePage();
    }

    private void TutorialBack_Click(object sender, RoutedEventArgs e)
    {
        if (tutorialStepIndex > 0)
        {
            tutorialStepIndex--;
            appSettings.TutorialStepIndex = tutorialStepIndex;
            SaveSettings(appSettings);
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
        appSettings.TutorialStepIndex = tutorialStepIndex;
        if (settingsLoadedSuccessfully)
        {
            SaveSettings(appSettings);
        }

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

    private void ShowWelcomeScreen()
    {
        WelcomeScreen.Visibility = Visibility.Visible;
        HomePage.Visibility = Visibility.Collapsed;
    }

    private void ShowHomePage()
    {
        WelcomeScreen.Visibility = Visibility.Collapsed;
        HomePage.Visibility = Visibility.Visible;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (isLoadingSettings || ThemeComboBox.SelectedItem is not ComboBoxItem selectedTheme)
        {
            return;
        }

        appSettings.Theme = selectedTheme.Tag?.ToString() ?? "Light";
        ApplyTheme(appSettings.Theme);
        SaveSettings(appSettings);
    }

    private async void SearchModrinth_Click(object sender, RoutedEventArgs e)
    {
        var isPluginSearch = AddonKindTabControl.SelectedIndex == 0;
        var resultsList = isPluginSearch ? PluginResultsListBox : ModResultsListBox;
        var query = AddonSearchTextBox.Text.Trim();
        var minecraftVersion = MinecraftVersionTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(query) || query == "Search Modrinth")
        {
            resultsList.Items.Clear();
            resultsList.Items.Add("Type a mod or plugin name before searching.");
            return;
        }

        resultsList.Items.Clear();
        resultsList.Items.Add("Searching Modrinth...");

        try
        {
            var results = await SearchModrinthAsync(query, minecraftVersion, isPluginSearch);

            resultsList.Items.Clear();
            if (results.Count == 0)
            {
                resultsList.Items.Add("No Modrinth results found.");
                return;
            }

            foreach (var result in results)
            {
                resultsList.Items.Add($"{result.Title} - {result.Description}");
            }
        }
        catch (Exception ex)
        {
            resultsList.Items.Clear();
            resultsList.Items.Add($"Modrinth search failed: {ex.Message}");
        }
    }

    private static async Task<List<ModrinthSearchResult>> SearchModrinthAsync(
        string query,
        string minecraftVersion,
        bool isPluginSearch)
    {
        var projectType = isPluginSearch ? "plugin" : "mod";
        var facets = new List<string[]>
        {
            new[] { $"project_type:{projectType}" }
        };

        if (!string.IsNullOrWhiteSpace(minecraftVersion))
        {
            facets.Add(new[] { $"versions:{minecraftVersion}" });
        }

        var url = "https://api.modrinth.com/v2/search" +
                  $"?query={Uri.EscapeDataString(query)}" +
                  $"&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}" +
                  "&limit=20";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var hits = document.RootElement.GetProperty("hits");
        var results = new List<ModrinthSearchResult>();

        foreach (var hit in hits.EnumerateArray())
        {
            results.Add(new ModrinthSearchResult(
                GetJsonString(hit, "title"),
                GetJsonString(hit, "description")));
        }

        return results;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is ComboBoxItem selectedTheme)
        {
            appSettings.Theme = selectedTheme.Tag?.ToString() ?? appSettings.Theme;
        }

        appSettings.TutorialStepIndex = tutorialStepIndex;
        SaveSettings(appSettings);
    }

    private void ApplyTheme(string theme)
    {
        if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            SetBrush("TextBrush", "#E7ECE8");
            SetBrush("MutedBrush", "#A7B0AA");
            SetBrush("AppBackgroundBrush", "#151A17");
            SetBrush("WelcomeBackgroundBrush", "#18221B");
            SetBrush("HeaderBackgroundBrush", "#1E2A22");
            SetBrush("HeaderBorderBrush", "#334239");
            SetBrush("PanelBrush", "#202821");
            SetBrush("BorderBrushSoft", "#38443B");
            SetBrush("InputBackgroundBrush", "#151B17");
            SetBrush("SecondaryButtonBackgroundBrush", "#28322B");
            SetBrush("ConsoleBackgroundBrush", "#161D18");
            SetBrush("AccentBrush", "#3D9256");
            SetBrush("AccentHoverBrush", "#4AA764");
            SetBrush("DangerBrush", "#C55B5B");
            return;
        }

        SetBrush("TextBrush", "#1F2A24");
        SetBrush("MutedBrush", "#66746B");
        SetBrush("AppBackgroundBrush", "#F4F6F3");
        SetBrush("WelcomeBackgroundBrush", "#E6F0E6");
        SetBrush("HeaderBackgroundBrush", "#E6F0E6");
        SetBrush("HeaderBorderBrush", "#CFE0D0");
        SetBrush("PanelBrush", "#FFFFFF");
        SetBrush("BorderBrushSoft", "#DDE5DC");
        SetBrush("InputBackgroundBrush", "#FFFFFF");
        SetBrush("SecondaryButtonBackgroundBrush", "#EEF3EE");
        SetBrush("ConsoleBackgroundBrush", "#F7F9F6");
        SetBrush("AccentBrush", "#2F7D46");
        SetBrush("AccentHoverBrush", "#27683B");
        SetBrush("DangerBrush", "#B84A4A");
    }

    private void SetBrush(string resourceKey, string color)
    {
        Resources[resourceKey] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }

    private static AppSettings LoadSettings(out bool loadedSuccessfully)
    {
        loadedSuccessfully = true;

        try
        {
            if (File.Exists(settingsFilePath))
            {
                var json = File.ReadAllText(settingsFilePath);
                using var document = JsonDocument.Parse(json);
                var root = document.RootElement;
                var settings = new AppSettings();

                if (root.TryGetProperty("HasCompletedTutorial", out var hasCompletedTutorial) &&
                    (hasCompletedTutorial.ValueKind == JsonValueKind.True || hasCompletedTutorial.ValueKind == JsonValueKind.False))
                {
                    settings.HasCompletedTutorial = hasCompletedTutorial.GetBoolean();
                }

                if (root.TryGetProperty("Theme", out var theme) &&
                    theme.ValueKind == JsonValueKind.String)
                {
                    settings.Theme = theme.GetString() ?? "Light";
                }

                if (root.TryGetProperty("TutorialStepIndex", out var tutorialStepIndex) &&
                    tutorialStepIndex.ValueKind == JsonValueKind.Number &&
                    tutorialStepIndex.TryGetInt32(out var stepIndex))
                {
                    settings.TutorialStepIndex = stepIndex;
                }

                return settings;
            }
        }
        catch
        {
            loadedSuccessfully = false;
        }

        return new AppSettings();
    }

    private static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(settingsDirectory);

        if (File.Exists(oldTextSettingsFilePath))
        {
            File.Delete(oldTextSettingsFilePath);
        }

        var json = JsonSerializer.Serialize(new
        {
            settings.HasCompletedTutorial,
            settings.Theme,
            settings.TutorialStepIndex
        }, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        File.WriteAllText(settingsFilePath, json);
    }

    private void AppendConsoleLine(string line)
    {
        Dispatcher.Invoke(() =>
        {
            consoleBuffer.AppendLine(line);
            ConsoleOutputTextBox.Text = consoleBuffer.ToString();
            ConsoleOutputTextBox.ScrollToEnd();
        });
    }

    private async void RUnJavaAndDoyouthingy(object sender, RoutedEventArgs e)
    {
        if (serverProcess is { HasExited: false })
        {
            Status.Text = "Status: Server is already running.";
            return;
        }

        if (string.IsNullOrWhiteSpace(serverFilePath) || !Directory.Exists(serverFilePath))
        {
            Status.Text = "Status: Pick a valid server folder first.";
            return;
        }

        serverJarFileName = string.IsNullOrWhiteSpace(ServerJarNameTextBox.Text)
            ? "server.jar"
            : ServerJarNameTextBox.Text.Trim();

        var serverJarPath = Path.Combine(serverFilePath, serverJarFileName);
        if (!File.Exists(serverJarPath))
        {
            Status.Text = $"Status: {serverJarFileName} was not found in the selected folder.";
            AppendConsoleLine($"[BlockHost] Could not start server. Missing: {serverJarPath}");
            return;
        }

        ramAllocation = (int)RamSlider.Value;

        var startInfo = new ProcessStartInfo
        {
            FileName = "java",
            Arguments = $"-Xms{ramAllocation}G -Xmx{ramAllocation}G -jar \"{serverJarPath}\" nogui",
            WorkingDirectory = serverFilePath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true
        };

        var process = new Process
        {
            StartInfo = startInfo,
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
        catch (Exception ex)
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

            var exited = await Task.Run(() => serverProcess.WaitForExit(15000));
            if (!exited)
            {
                AppendConsoleLine("[BlockHost] Server did not exit in time. Killing process.");
                serverProcess.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex)
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
            serverFilePath = dialog.SelectedPath;
        }
    }

    private void RamSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        ramAllocation = (int)RamSlider.Value;
        RamLabel.Text = $"{ramAllocation} GB";
    }
}

public sealed class AppSettings
{
    public bool HasCompletedTutorial { get; set; }
    public string Theme { get; set; } = "Light";
    public int TutorialStepIndex { get; set; }
}

public sealed record ModrinthSearchResult(string Title, string Description);
