using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
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
                resultsList.Items.Add(result);
            }
        }
        catch (Exception ex)
        {
            resultsList.Items.Clear();
            resultsList.Items.Add($"Modrinth search failed: {ex.Message}");
        }
    }

    private async void InstallSelectedAddon_Click(object sender, RoutedEventArgs e)
    {
        var isPluginInstall = AddonKindTabControl.SelectedIndex == 0;
        var resultsList = isPluginInstall ? PluginResultsListBox : ModResultsListBox;

        if (resultsList.SelectedItem is not ModrinthSearchResult selectedResult)
        {
            AppendConsoleLine("[BlockHost] Select a Modrinth result before installing.");
            return;
        }

        if (!TryGetServerFolder(out var serverFolder))
        {
            return;
        }

        var minecraftVersion = MinecraftVersionTextBox.Text.Trim();
        var loader = GetSelectedModrinthLoader(isPluginInstall);
        var installFolder = Path.Combine(serverFolder, isPluginInstall ? "plugins" : "mods");

        try
        {
            AppendConsoleLine($"[BlockHost] Installing {selectedResult.Title} from Modrinth...");
            Directory.CreateDirectory(installFolder);

            var downloadedPath = await DownloadLatestModrinthFileAsync(
                selectedResult.ProjectId,
                minecraftVersion,
                loader,
                installFolder);

            AppendConsoleLine($"[BlockHost] Installed: {downloadedPath}");
            Status.Text = $"Status: Installed {selectedResult.Title}.";
        }
        catch (Exception ex)
        {
            AppendConsoleLine($"[BlockHost] Install failed: {ex.Message}");
            Status.Text = "Status: Add-on install failed.";
        }
    }

    private async void DownloadServerSoftware_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetServerFolder(out var serverFolder))
        {
            return;
        }

        var minecraftVersion = MinecraftVersionTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(minecraftVersion))
        {
            Status.Text = "Status: Enter a Minecraft version first.";
            return;
        }

        try
        {
            var software = GetSelectedServerSoftware();
            AppendConsoleLine($"[BlockHost] Downloading {software} server for Minecraft {minecraftVersion}...");

            var downloadedPath = software switch
            {
                "paper" => await DownloadPaperServerAsync(minecraftVersion, serverFolder),
                "fabric" => await DownloadFabricServerAsync(minecraftVersion, serverFolder),
                "vanilla" => await DownloadVanillaServerAsync(minecraftVersion, serverFolder),
                "forge" => await DownloadForgeServerAsync(minecraftVersion, serverFolder),
                "neoforge" => await DownloadNeoForgeServerAsync(minecraftVersion, serverFolder),
                _ => throw new NotSupportedException($"Unknown server software: {software}")
            };

            ServerJarNameTextBox.Text = Path.GetFileName(downloadedPath);
            serverJarFileName = ServerJarNameTextBox.Text;
            AppendConsoleLine($"[BlockHost] Downloaded server jar: {downloadedPath}");
            Status.Text = $"Status: Downloaded {software} server.";
        }
        catch (Exception ex)
        {
            AppendConsoleLine($"[BlockHost] Server download failed: {ex.Message}");
            Status.Text = "Status: Server download failed.";
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
                GetJsonString(hit, "project_id"),
                GetJsonString(hit, "title"),
                GetJsonString(hit, "description")));
        }

        return results;
    }

    private static async Task<string> DownloadLatestModrinthFileAsync(
        string projectId,
        string minecraftVersion,
        string loader,
        string installFolder)
    {
        var url = $"https://api.modrinth.com/v2/project/{Uri.EscapeDataString(projectId)}/version";
        var query = new List<string>();

        if (!string.IsNullOrWhiteSpace(minecraftVersion))
        {
            query.Add($"game_versions={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { minecraftVersion }))}");
        }

        if (!string.IsNullOrWhiteSpace(loader))
        {
            query.Add($"loaders={Uri.EscapeDataString(JsonSerializer.Serialize(new[] { loader }))}");
        }

        if (query.Count > 0)
        {
            url += "?" + string.Join("&", query);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var versions = document.RootElement;
        if (versions.ValueKind != JsonValueKind.Array || versions.GetArrayLength() == 0)
        {
            throw new InvalidOperationException("No compatible Modrinth version was found.");
        }

        foreach (var version in versions.EnumerateArray())
        {
            if (!version.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            JsonElement? selectedFile = null;
            foreach (var file in files.EnumerateArray())
            {
                if (file.TryGetProperty("primary", out var primary) && primary.ValueKind == JsonValueKind.True)
                {
                    selectedFile = file;
                    break;
                }

                selectedFile ??= file;
            }

            if (selectedFile is not JsonElement fileElement)
            {
                continue;
            }

            var fileUrl = GetJsonString(fileElement, "url");
            var fileName = GetJsonString(fileElement, "filename");
            if (string.IsNullOrWhiteSpace(fileUrl) || string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            return await DownloadFileAsync(fileUrl, installFolder, fileName);
        }

        throw new InvalidOperationException("No downloadable file was found for the compatible Modrinth version.");
    }

    private static async Task<string> DownloadPaperServerAsync(string minecraftVersion, string serverFolder)
    {
        var supportedVersions = await GetPaperSupportedVersionsAsync();
        var resolvedVersion = ResolvePaperVersion(minecraftVersion, supportedVersions);

        using var buildsRequest = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://api.papermc.io/v2/projects/paper/versions/{Uri.EscapeDataString(resolvedVersion)}/builds");
        buildsRequest.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var buildsResponse = await httpClient.SendAsync(buildsRequest);
        buildsResponse.EnsureSuccessStatusCode();

        var json = await buildsResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var builds = document.RootElement.GetProperty("builds");
        if (builds.ValueKind != JsonValueKind.Array || builds.GetArrayLength() == 0)
        {
            throw new InvalidOperationException($"No Paper builds were found for Minecraft {resolvedVersion}.");
        }

        var latestBuild = GetLatestPaperBuild(builds);
        var fileName = $"paper-{resolvedVersion}-{latestBuild}.jar";
        var downloadUrl =
            $"https://api.papermc.io/v2/projects/paper/versions/{Uri.EscapeDataString(resolvedVersion)}/builds/{latestBuild}/downloads/{fileName}";

        return await DownloadFileAsync(downloadUrl, serverFolder, fileName);
    }

    private static async Task<List<string>> GetPaperSupportedVersionsAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.papermc.io/v2/projects/paper");
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var versions = document.RootElement.GetProperty("versions");
        var supportedVersions = new List<string>();

        foreach (var version in versions.EnumerateArray())
        {
            if (version.ValueKind == JsonValueKind.String)
            {
                var value = version.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    supportedVersions.Add(value);
                }
            }
        }

        return supportedVersions;
    }

    private static string ResolvePaperVersion(string requestedVersion, IReadOnlyCollection<string> supportedVersions)
    {
        if (supportedVersions.Contains(requestedVersion, StringComparer.OrdinalIgnoreCase))
        {
            return requestedVersion;
        }

        var requestedComparable = ParseVersionKey(requestedVersion);
        var sameLineVersions = supportedVersions
            .Where(version => SameVersionLine(version, requestedVersion))
            .Select(version => new { Version = version, Key = ParseVersionKey(version) })
            .Where(entry => entry.Key <= requestedComparable)
            .OrderByDescending(entry => entry.Key)
            .Select(entry => entry.Version)
            .ToList();

        if (sameLineVersions.Count > 0)
        {
            return sameLineVersions[0];
        }

        var newestSupported = supportedVersions
            .Select(version => new { Version = version, Key = ParseVersionKey(version) })
            .OrderByDescending(entry => entry.Key)
            .Take(5)
            .Select(entry => entry.Version)
            .ToArray();

        var message = newestSupported.Length == 0
            ? "Paper does not currently publish any server builds."
            : $"Paper currently publishes versions like {string.Join(", ", newestSupported)}.";

        throw new InvalidOperationException($"Paper does not publish a build for {requestedVersion}. {message}");
    }

    private static bool SameVersionLine(string candidateVersion, string requestedVersion)
    {
        var candidateParts = candidateVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var requestedParts = requestedVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (candidateParts.Length < 2 || requestedParts.Length < 2)
        {
            return false;
        }

        return string.Equals(candidateParts[0], requestedParts[0], StringComparison.OrdinalIgnoreCase) &&
               string.Equals(candidateParts[1], requestedParts[1], StringComparison.OrdinalIgnoreCase);
    }

    private static Version ParseVersionKey(string version)
    {
        var parts = version.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var major = parts.Length > 0 && int.TryParse(parts[0], out var majorPart) ? majorPart : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var minorPart) ? minorPart : 0;
        var build = parts.Length > 2 && int.TryParse(parts[2], out var buildPart) ? buildPart : 0;
        var revision = parts.Length > 3 && int.TryParse(parts[3], out var revisionPart) ? revisionPart : 0;
        return new Version(major, minor, build, revision);
    }

    private static int GetLatestPaperBuild(JsonElement builds)
    {
        var latestBuild = int.MinValue;

        foreach (var build in builds.EnumerateArray())
        {
            if (build.TryGetProperty("build", out var buildNumberElement) &&
                buildNumberElement.TryGetInt32(out var buildNumber) &&
                buildNumber > latestBuild)
            {
                latestBuild = buildNumber;
            }
        }

        if (latestBuild == int.MinValue)
        {
            throw new InvalidOperationException("Paper returned builds without any build numbers.");
        }

        return latestBuild;
    }

    private static async Task<string> DownloadFabricServerAsync(string minecraftVersion, string serverFolder)
    {
        var loaderVersion = await GetLatestFabricComponentVersionAsync("loader");
        var installerVersion = await GetLatestFabricComponentVersionAsync("installer");
        var fileName = $"fabric-server-{minecraftVersion}.jar";
        var downloadUrl =
            $"https://meta.fabricmc.net/v2/versions/loader/{Uri.EscapeDataString(minecraftVersion)}/{Uri.EscapeDataString(loaderVersion)}/{Uri.EscapeDataString(installerVersion)}/server/jar";

        return await DownloadFileAsync(downloadUrl, serverFolder, fileName);
    }

    private static async Task<string> DownloadVanillaServerAsync(string minecraftVersion, string serverFolder)
    {
        using var manifestRequest = new HttpRequestMessage(
            HttpMethod.Get,
            "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json");
        manifestRequest.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var manifestResponse = await httpClient.SendAsync(manifestRequest);
        manifestResponse.EnsureSuccessStatusCode();

        var manifestJson = await manifestResponse.Content.ReadAsStringAsync();
        using var manifestDocument = JsonDocument.Parse(manifestJson);

        string versionMetadataUrl = "";
        foreach (var version in manifestDocument.RootElement.GetProperty("versions").EnumerateArray())
        {
            if (string.Equals(GetJsonString(version, "id"), minecraftVersion, StringComparison.OrdinalIgnoreCase))
            {
                versionMetadataUrl = GetJsonString(version, "url");
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(versionMetadataUrl))
        {
            throw new InvalidOperationException("That Minecraft version was not found in Mojang's version manifest.");
        }

        using var versionRequest = new HttpRequestMessage(HttpMethod.Get, versionMetadataUrl);
        versionRequest.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var versionResponse = await httpClient.SendAsync(versionRequest);
        versionResponse.EnsureSuccessStatusCode();

        var versionJson = await versionResponse.Content.ReadAsStringAsync();
        using var versionDocument = JsonDocument.Parse(versionJson);
        var downloadUrl = GetJsonString(
            versionDocument.RootElement.GetProperty("downloads").GetProperty("server"),
            "url");

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new InvalidOperationException("Mojang did not provide a server jar for that version.");
        }

        return await DownloadFileAsync(downloadUrl, serverFolder, $"vanilla-{minecraftVersion}.jar");
    }

    private static async Task<string> DownloadForgeServerAsync(string minecraftVersion, string serverFolder)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json");
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var promos = document.RootElement.GetProperty("promos");
        var promoKey = $"{minecraftVersion}-latest";

        if (!promos.TryGetProperty(promoKey, out var forgeVersionElement) ||
            forgeVersionElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException("No Forge latest build was found for that Minecraft version.");
        }

        var forgeVersion = forgeVersionElement.GetString() ?? "";
        var mavenVersion = $"{minecraftVersion}-{forgeVersion}";
        var fileName = $"forge-{mavenVersion}-installer.jar";
        var downloadUrl =
            $"https://maven.minecraftforge.net/net/minecraftforge/forge/{Uri.EscapeDataString(mavenVersion)}/{Uri.EscapeDataString(fileName)}";

        return await DownloadFileAsync(downloadUrl, serverFolder, fileName);
    }

    private static async Task<string> DownloadNeoForgeServerAsync(string minecraftVersion, string serverFolder)
    {
        var prefix = GetNeoForgeVersionPrefix(minecraftVersion);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml");
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var metadata = XDocument.Parse(await response.Content.ReadAsStringAsync());
        var versions = metadata
            .Descendants("version")
            .Select(version => version.Value)
            .Where(version => version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (versions.Count == 0)
        {
            throw new InvalidOperationException("No NeoForge build was found for that Minecraft version.");
        }

        var neoForgeVersion = versions[^1];
        var fileName = $"neoforge-{neoForgeVersion}-installer.jar";
        var downloadUrl =
            $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{Uri.EscapeDataString(neoForgeVersion)}/{Uri.EscapeDataString(fileName)}";

        return await DownloadFileAsync(downloadUrl, serverFolder, fileName);
    }

    private static string GetNeoForgeVersionPrefix(string minecraftVersion)
    {
        var parts = minecraftVersion.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] != "1")
        {
            throw new InvalidOperationException("NeoForge version matching expects Minecraft versions like 1.21.1.");
        }

        var minor = parts[1];
        var patch = parts.Length >= 3 ? parts[2] : "0";
        return $"{minor}.{patch}.";
    }

    private static async Task<string> GetLatestFabricComponentVersionAsync(string component)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"https://meta.fabricmc.net/v2/versions/{component}");
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);

        foreach (var version in document.RootElement.EnumerateArray())
        {
            if (!version.TryGetProperty("stable", out var stable) || stable.ValueKind != JsonValueKind.False)
            {
                return GetJsonString(version, "version");
            }
        }

        throw new InvalidOperationException($"No Fabric {component} version was found.");
    }

    private static async Task<string> DownloadFileAsync(string url, string targetFolder, string fileName)
    {
        Directory.CreateDirectory(targetFolder);

        var safeFileName = Path.GetFileName(fileName);
        var targetPath = Path.Combine(targetFolder, safeFileName);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("BlockHost/0.1");

        using var response = await httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();

        await using var sourceStream = await response.Content.ReadAsStreamAsync();
        await using var destinationStream = File.Create(targetPath);
        await sourceStream.CopyToAsync(destinationStream);

        return targetPath;
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String
            ? property.GetString() ?? ""
            : "";
    }

    private bool TryGetServerFolder(out string serverFolder)
    {
        serverFolder = serverFilePath;

        if (string.IsNullOrWhiteSpace(serverFolder))
        {
            serverFolder = ServerFolderTextBox.Text.Trim();
        }

        if (string.IsNullOrWhiteSpace(serverFolder) || serverFolder == "No folder selected")
        {
            Status.Text = "Status: Choose a server folder first.";
            AppendConsoleLine("[BlockHost] Choose a server folder first.");
            return false;
        }

        Directory.CreateDirectory(serverFolder);
        serverFilePath = serverFolder;
        return true;
    }

    private string GetSelectedServerSoftware()
    {
        if (ServerSoftwareComboBox.SelectedItem is ComboBoxItem item)
        {
            var tag = item.Tag?.ToString();
            if (!string.IsNullOrWhiteSpace(tag))
            {
                return tag;
            }

            var content = item.Content?.ToString()?.ToLowerInvariant() ?? "";
            if (content.Contains("fabric"))
            {
                return "fabric";
            }

            if (content.Contains("forge") && !content.Contains("neo"))
            {
                return "forge";
            }

            if (content.Contains("neoforge"))
            {
                return "neoforge";
            }

            if (content.Contains("vanilla"))
            {
                return "vanilla";
            }
        }

        return "paper";
    }

    private string GetSelectedModrinthLoader(bool isPluginSearch)
    {
        if (isPluginSearch)
        {
            return "paper";
        }

        var software = GetSelectedServerSoftware();
        return software is "forge" or "neoforge" or "fabric" ? software : "";
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
        if (sender is not Slider ramSlider)
        {
            return;
        }

        ramAllocation = (int)ramSlider.Value;

        if (FindName("RamLabel") is TextBlock ramLabel)
        {
            ramLabel.Text = $"{ramAllocation} GB";
        }
    }
}

public sealed class AppSettings
{
    public bool HasCompletedTutorial { get; set; }
    public string Theme { get; set; } = "Light";
    public int TutorialStepIndex { get; set; }
}

public sealed record ModrinthSearchResult(string ProjectId, string Title, string Description)
{
    public override string ToString()
    {
        return $"{Title} - {Description}";
    }
}
