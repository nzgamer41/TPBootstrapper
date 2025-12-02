using Avalonia.Controls;
using Avalonia.Interactivity;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Linq;
using System.Text; // added
using System.IO.Compression; // added for zip extraction
using AutoUpdaterDotNET;


namespace TPBootstrapper
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<CoreItem> _cores = new ObservableCollection<CoreItem>();
        private readonly HttpClient _httpClient = new HttpClient();
        private readonly string[] _components;
        private readonly string CacheFile;
        private string downloadDir = Directory.GetCurrentDirectory();
        private string tokens = "?client_id=Ov23livUHjCHB2WJMos2&client_secret=32b12d3750b8fb2479886879697dbf9a760ab1b6";

        public MainWindow()
        {
            InitializeComponent();
            CoresListBox.ItemsSource = _cores;
            CacheFile = Path.Combine(Directory.GetCurrentDirectory(), "tpcache.json");
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("TPBootstrapper/1.0 (compat)");
            var byteArray = new UTF8Encoding().GetBytes("Ov23livUHjCHB2WJMos2:32b12d3750b8fb2479886879697dbf9a760ab1b6");
            _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
            InstallDirTextBox.Text = downloadDir;
            // populate components from UpdaterRegistry
            _components = UpdaterRegistry.components.Select(c => c.name).ToArray();
            _ = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            if (!await CheckInternetAsync())
            {
                await ShowMessage("No internet connection detected. This app requires internet to function.");
            }
            checkForInstallUpdate();

            await LoadCacheAsync();
            await RefreshCoresAsync();
        }

        private void checkForInstallUpdate()
        {
                        //TODO: use autoupdater .net thing later
            //https://github.com/nzgamer41/TPBootstrapper/releases/latest/download/autoupdate.xml
            AutoUpdater.Start("https://github.com/nzgamer41/TPBootstrapper/releases/latest/download/autoupdate.xml");
        }

        private async Task<bool> CheckInternetAsync()
        {
            try
            {
                using var resp = await _httpClient.GetAsync("https://api.github.com/" + tokens, HttpCompletionOption.ResponseHeadersRead);
                return resp.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private async Task LoadCacheAsync()
        {
            try
            {
                if (File.Exists(CacheFile))
                {
                    var json = await File.ReadAllTextAsync(CacheFile);
                    var cached = JsonSerializer.Deserialize<List<CoreItem>>(json);
                    if (cached != null)
                    {
                        foreach (var c in cached)
                        {
                            // we'll preserve installed flags in cache but full refresh still occurs
                        }
                    }
                }
            }
            catch
            {
                // ignore cache errors
            }
        }

        private async Task SaveCacheAsync()
        {
            try
            {
                var json = JsonSerializer.Serialize(_cores);
                await File.WriteAllTextAsync(CacheFile, json);
            }
            catch
            {
                // ignore
            }
        }

        private async void BrowseBtn_Click(object? sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog();
            var result = await dlg.ShowAsync(this);
            if (!string.IsNullOrWhiteSpace(result))
            {
                downloadDir = result;
                InstallDirTextBox.Text = downloadDir;
            }
        }

        private async void RefreshBtn_Click(object? sender, RoutedEventArgs e)
        {
            await RefreshCoresAsync();
        }

        private async Task RefreshCoresAsync()
        {
            StatusText.Text = "Refreshing core list...";
            _cores.Clear();

            foreach (var comp in _components)
            {
                var core = await GetCoreInfoAsync(comp);
                if (core != null)
                {
                    _cores.Add(core);
                }
            }

            StatusText.Text = $"Loaded {_cores.Count} cores.";
            await SaveCacheAsync();
        }

        private async Task<CoreItem?> GetCoreInfoAsync(string component)
        {
            // Map component to owner/repo using UpdaterRegistry when available
            string owner = "teknogods";
            string repo = component;
            var uc = UpdaterRegistry.components.FirstOrDefault(x => string.Equals(x.name, component, StringComparison.OrdinalIgnoreCase));
            if (uc != null)
            {
                if (!string.IsNullOrEmpty(uc.userName))
                    owner = uc.userName;
                if (!string.IsNullOrEmpty(uc.reponame))
                    repo = uc.reponame;
                else
                    repo = uc.name;
            }
            // fallback special-case handling (kept for compatibility)
            if (string.Equals(component, "SegaToolsTP", StringComparison.OrdinalIgnoreCase))
            {
                owner = "nzgamer41";
                repo = component;
            }

            try
            {
                // 1) Get full releases list and prefer the release whose tag_name exactly matches the component name.
                var releasesUrl = $"https://api.github.com/repos/{owner}/{repo}/releases" + tokens;
                var resp = await _httpClient.GetAsync(releasesUrl);
                if (!resp.IsSuccessStatusCode)
                    return null;

                var listJson = await resp.Content.ReadAsStringAsync();
                using var listDoc = JsonDocument.Parse(listJson);
                var root = listDoc.RootElement;
                if (root.ValueKind != JsonValueKind.Array) return null;

                // First pass: find release with tag_name == component (case-insensitive)
                foreach (var release in root.EnumerateArray())
                {
                    if (!release.TryGetProperty("tag_name", out var tagEl)) continue;
                    var tag = tagEl.GetString() ?? "";
                    if (!string.Equals(tag, component, StringComparison.OrdinalIgnoreCase)) continue;

                    if (release.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                    {
                        var asset = SelectBestAsset(assets);
                        if (asset.HasValue)
                        {
                            var assetEl = asset.Value;
                            var assetUrl = assetEl.GetProperty("browser_download_url").GetString() ?? "";
                            var version = GetVersionFromRelease(release, assetEl, component);
                            var core = new CoreItem
                            {
                                Name = component,
                                DownloadUrl = assetUrl,
                                Version = version
                            };
                            if (string.Equals(component, "TeknoParrotUi", StringComparison.OrdinalIgnoreCase) || string.Equals(component, "TeknoParrotUI", StringComparison.OrdinalIgnoreCase))
                                core.IsRequired = true;
                            return core;
                        }
                    }
                }

                // 2) No matching-tag release — try the releases/latest endpoint
                var latestUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest" + tokens;
                resp = await _httpClient.GetAsync(latestUrl);
                if (resp.IsSuccessStatusCode)
                {
                    var latestJson = await resp.Content.ReadAsStringAsync();
                    using var latestDoc = JsonDocument.Parse(latestJson);
                    var latestRelease = latestDoc.RootElement;
                    if (latestRelease.TryGetProperty("assets", out var latestAssets) && latestAssets.GetArrayLength() > 0)
                    {
                        var asset = SelectBestAsset(latestAssets);
                        if (asset.HasValue)
                        {
                            var assetEl = asset.Value;
                            var assetUrl = assetEl.GetProperty("browser_download_url").GetString() ?? "";
                            var version = GetVersionFromRelease(latestRelease, assetEl, component);
                            var core = new CoreItem
                            {
                                Name = component,
                                DownloadUrl = assetUrl,
                                Version = version
                            };
                            if (string.Equals(component, "TeknoParrotUi", StringComparison.OrdinalIgnoreCase) || string.Equals(component, "TeknoParrotUI", StringComparison.OrdinalIgnoreCase))
                                core.IsRequired = true;
                            return core;
                        }
                    }
                }

                // 3) Final fallback: pick the first release from the list which has usable assets
                foreach (var release in root.EnumerateArray())
                {
                    if (!release.TryGetProperty("assets", out var assets2) || assets2.GetArrayLength() == 0) continue;
                    var asset2 = SelectBestAsset(assets2);
                    if (!asset2.HasValue) continue;
                    var assetEl2 = asset2.Value;
                    var assetUrl2 = assetEl2.GetProperty("browser_download_url").GetString();
                    var version2 = GetVersionFromRelease(release, assetEl2, component);
                    var core2 = new CoreItem
                    {
                        Name = component,
                        DownloadUrl = assetUrl2 ?? "",
                        Version = version2
                    };
                    if (string.Equals(component, "TeknoParrotUi", StringComparison.OrdinalIgnoreCase) || string.Equals(component, "TeknoParrotUI", StringComparison.OrdinalIgnoreCase))
                        core2.IsRequired = true;
                    return core2;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error fetching releases for {component}: {ex}");
            }

            return null;
        }

        // Attempt to derive a clean version string from release/tag/name/asset.
        private static string GetVersionFromRelease(JsonElement release, JsonElement? assetElement, string component)
        {
            // Try to find a numeric version in tag_name, then release name, then asset name.
            // We prefer numeric-only version substrings (e.g. 1.0.0.5) and return them as "vX.X.X..."
            string tagString = null;
            string nameString = null;
            string assetNameString = null;

            if (release.TryGetProperty("tag_name", out var tagEl))
                tagString = tagEl.GetString();
            if (release.TryGetProperty("name", out var nameEl))
                nameString = nameEl.GetString();
            if (assetElement.HasValue && assetElement.Value.TryGetProperty("name", out var assetNameEl))
                assetNameString = assetNameEl.GetString();

            // check tag first
            var v = ExtractVersionFromString(tagString);
            if (!string.IsNullOrEmpty(v))
                return v;

            // then release name
            v = ExtractVersionFromString(nameString);
            if (!string.IsNullOrEmpty(v))
                return v;

            // then asset name
            v = ExtractVersionFromString(assetNameString);
            if (!string.IsNullOrEmpty(v))
                return v;

            // no numeric version found — attempt to clean tag/name/asset by removing component text and returning remainder
            string[] candidates = new[] { tagString, nameString, assetNameString };
            foreach (var cand in candidates)
            {
                if (string.IsNullOrEmpty(cand)) continue;
                try
                {
                    var cleaned = Regex.Replace(cand, Regex.Escape(component), "", RegexOptions.IgnoreCase).Trim('_', '-', ' ', 'v', 'V');
                    if (!string.IsNullOrEmpty(cleaned))
                    {
                        if (Regex.IsMatch(cleaned, @"^\d"))
                            return "v" + cleaned;
                        return cleaned;
                    }
                }
                catch { }
            }

            // final fallback: return component name (should be rare)
            return component;
        }

        // Finds the first version-like substring (e.g. 1.2.3 or 1.2.3.4). Returns null if none found.
        private static string ExtractVersionFromString(string input)
        {
            if (string.IsNullOrEmpty(input)) return null;
            // Replace underscores with dots to handle patterns like RPCS3_1.0.0.5
            var normalized = input.Replace('_', '.');
            // match sequences like 1.0.0 or 1.0.0.5, optionally prefixed with 'v'
            var m = Regex.Match(normalized, @"v?(\d+(?:\.\d+){1,})", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                // return capture group without 'v'
                var grp = m.Groups[1].Value;
                return grp;
            }
            return null;
        }

        // Helper: choose the best asset from the assets array
        private static JsonElement? SelectBestAsset(JsonElement assets)
        {
            JsonElement? first = null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (first == null) first = asset;
                // prefer zip/7z first, then exe/msi, then dll, else first
                var lower = name.ToLowerInvariant();
                if (lower.EndsWith(".zip") || lower.EndsWith(".7z"))
                    return asset;
                if (lower.EndsWith(".exe") || lower.EndsWith(".msi"))
                    return asset;
                if (lower.EndsWith(".dll"))
                    return asset;
            }
            return first;
        }

        private void CoresListBox_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            var idx = CoresListBox.SelectedIndex;
            if (idx >= 0 && idx < _cores.Count)
            {
                var selected = _cores[idx];
                SelectedProgress.Value = selected.Progress;
                DownloadSelectedBtn.IsEnabled = true;
            }
            else
            {
                DownloadSelectedBtn.IsEnabled = false;
            }
        }

        private async void DownloadSelectedBtn_Click(object? sender, RoutedEventArgs e)
        {
            var idx = CoresListBox.SelectedIndex;
            if (idx < 0) return;

            var core = _cores[idx];
            await DownloadCoreAsync(core);
        }

        private async void FullInstallBtn_Click(object? sender, RoutedEventArgs e)
        {
            FullInstallBtn.IsEnabled = false;
            StatusText.Text = "Downloading all cores...";
            foreach (var c in _cores)
            {
                await DownloadCoreAsync(c);
            }

            StatusText.Text = "All downloads completed.";

            // After full install, attempt to launch TeknoParrotUi.exe from install dir, then close this window
            try
            {
                var exePath = Path.Combine(downloadDir, "TeknoParrotUi.exe");
                if (!File.Exists(exePath))
                    exePath = Path.Combine(downloadDir, "TeknoParrotUI.exe"); // try alternate casing

                if (File.Exists(exePath))
                {
                    var psi = new ProcessStartInfo(exePath) { UseShellExecute = true };
                    Process.Start(psi);
                }
            }
            catch
            {
                // non-fatal; ignore launch errors
            }

            // close bootstrapper
            try
            {
                this.Close();
            }
            catch
            {
                // best-effort
            }
        }

        private async Task DownloadCoreAsync(CoreItem core)
        {
            if (string.IsNullOrWhiteSpace(core.DownloadUrl))
            {
                StatusText.Text = $"No download URL for {core.Name}";
                return;
            }

            var destFile = Path.Combine(downloadDir, Path.GetFileName(new Uri(core.DownloadUrl).LocalPath));
            StatusText.Text = $"Downloading {core.Name} to {destFile}...";

            // Single progress instance shared between download and extraction
            IProgress<double> progress = new Progress<double>(p =>
            {
                // always update core progress and visible progress bar
                core.Progress = p;
                SelectedProgress.Value = p;
                // show a concise status with percent
                try
                {
                    var percentText = Math.Round(p, 0);
                    StatusText.Text = $"{core.Name}: {percentText}%";
                }
                catch { /* ignore UI update errors */ }
            });

            try
            {
                // download (reports to progress)
                await DownloadFileAsync(core.DownloadUrl, destFile, progress, CancellationToken.None);

                // determine component metadata if available
                var uc = UpdaterRegistry.components.FirstOrDefault(x => string.Equals(x.name, core.Name, StringComparison.OrdinalIgnoreCase));

                // Determine extraction target:
                // TeknoParrotUI -> install root
                // else -> folderOverride or per-component folder
                string extractTarget;
                if (string.Equals(core.Name, "TeknoParrotUi", StringComparison.OrdinalIgnoreCase) || string.Equals(core.Name, "TeknoParrotUI", StringComparison.OrdinalIgnoreCase))
                {
                    extractTarget = downloadDir;
                }
                else if (uc != null && !string.IsNullOrEmpty(uc.folderOverride))
                {
                    extractTarget = Path.Combine(downloadDir, uc.folderOverride);
                }
                else
                {
                    extractTarget = Path.Combine(downloadDir, core.Name);
                }

                Directory.CreateDirectory(extractTarget);

                // handle zip extraction (preserve internal folders) with progress
                if (destFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        using var fs = File.OpenRead(destFile);
                        using var zip = new ZipArchive(fs, ZipArchiveMode.Read);

                        // compute total uncompressed size of file entries
                        long totalBytes = 0;
                        foreach (var e in zip.Entries)
                        {
                            if (string.IsNullOrEmpty(e.Name)) continue;
                            try { totalBytes += e.Length; } catch { }
                        }

                        long extractedBytes = 0;
                        var extractTargetFull = Path.GetFullPath(extractTarget).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

                        foreach (var entry in zip.Entries)
                        {
                            var entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                            var outPath = Path.Combine(extractTarget, entryPath);
                            var fullOutPath = Path.GetFullPath(outPath);

                            // Safety: prevent path traversal outside extract target
                            if (!fullOutPath.StartsWith(extractTargetFull, StringComparison.OrdinalIgnoreCase))
                                continue;

                            if (string.IsNullOrEmpty(entry.Name))
                            {
                                Directory.CreateDirectory(fullOutPath);
                                continue;
                            }

                            Directory.CreateDirectory(Path.GetDirectoryName(fullOutPath) ?? extractTarget);

                            using var entryStream = entry.Open();
                            using var outFile = File.Create(fullOutPath);

                            // copy with progress
                            var buffer = new byte[81920];
                            int read;
                            while ((read = await entryStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await outFile.WriteAsync(buffer, 0, read);
                                extractedBytes += read;
                                if (totalBytes > 0)
                                {
                                    var percent = Math.Round((double)extractedBytes / totalBytes * 100, 2);
                                    progress.Report(percent);
                                }
                            }
                        }

                        // ensure progress shows 100% after extraction
                        progress.Report(100);

                        // write manual version if requested
                        if (uc != null && uc.manualVersion)
                        {
                            try { File.WriteAllText(Path.Combine(extractTarget, ".version"), core.Version ?? ""); } catch { }
                        }

                        StatusText.Text = $"Extracted {core.Name} to {extractTarget}";
                    }
                    catch (Exception ex)
                    {
                        StatusText.Text = $"Extraction failed for {core.Name}: {ex.Message}";
                    }

                    // delete archive
                    try { File.Delete(destFile); } catch { /* non-fatal */ }
                }
                else
                {
                    // non-zip: copy/move with progress using file sizes
                    try
                    {
                        var targetFile = Path.Combine(extractTarget, Path.GetFileName(destFile));
                        using (var src = File.OpenRead(destFile))
                        using (var dst = File.Create(targetFile))
                        {
                            var total = src.Length;
                            long copied = 0;
                            var buffer = new byte[81920];
                            int read;
                            while ((read = await src.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await dst.WriteAsync(buffer, 0, read);
                                copied += read;
                                if (total > 0)
                                {
                                    var percent = Math.Round((double)copied / total * 100, 2);
                                    progress.Report(percent);
                                }
                            }
                        }

                        try { File.Delete(destFile); } catch { /* non-fatal */ }

                        // manual version file
                        if (uc != null && uc.manualVersion)
                        {
                            try { File.WriteAllText(Path.Combine(extractTarget, ".version"), core.Version ?? ""); } catch { }
                        }

                        progress.Report(100);
                        destFile = Path.Combine(extractTarget, Path.GetFileName(destFile));
                    }
                    catch
                    {
                        // non-fatal
                    }
                }

                core.IsInstalled = true;
                core.Progress = 100;
                StatusText.Text = $"Downloaded {core.Name}";
                await SaveCacheAsync();

                // if executable, attempt to start (mimic original behaviour)
                if (destFile.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var psi = new ProcessStartInfo(destFile) { UseShellExecute = true };
                        Process.Start(psi);
                    }
                    catch
                    {
                        // non-fatal
                    }
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Download failed: {ex.Message}";
            }
        }

        private async Task DownloadFileAsync(string url, string destinationPath, IProgress<double> progress, CancellationToken ct)
        {
            using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1L;
            var canReport = total != -1;

            using var contentStream = await response.Content.ReadAsStreamAsync(ct);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? ".");
            using var fileStream = File.Create(destinationPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read, ct);
                totalRead += read;
                if (canReport)
                {
                    var percent = (int)Math.Round((double)totalRead / total * 100, 0);
                    progress.Report(percent);
                }
            }

            progress.Report(100);
        }

        private async Task ShowMessage(string msg)
        {
            var dlg = new Window
            {
                Content = new TextBlock { Text = msg, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Margin = new Avalonia.Thickness(10) },
                Width = 400,
                Height = 150,
                Title = "Message"
            };
            await dlg.ShowDialog(this);
        }
    }
}
