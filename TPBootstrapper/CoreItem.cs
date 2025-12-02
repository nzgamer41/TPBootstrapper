using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Threading;
using System.Threading.Tasks;

namespace TPBootstrapper;

public class CoreItem
{
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string DownloadUrl { get; set; } = "";
    public bool IsInstalled { get; set; }
    public bool NeedsUpdate { get; set; }
    public bool IsRequired { get; set; }
    public bool IsRedist { get; set; }
    public double Progress { get; set; }

    // runtime only
    public string? DownloadedFilePath { get; set; }

    public override string ToString()
    {
        string text = $"{Name} (v{Version})";

        if (IsRedist) return $"{text}  [DEPENDENCY]";
        if (IsInstalled) return $"{text}  [INSTALLED]";
        if (NeedsUpdate) return $"{text}  [NEEDS UPDATE]";
        if (IsRequired) return $"{text}  [REQUIRED]";

        return text;
    }

    // ============================
    // = Download entry point
    // ============================
    public async Task<bool> DownloadAsync(string installDir, IProgress<double>? progress, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(installDir);
            string destFile = Path.Combine(installDir, Path.GetFileName(DownloadUrl));

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("TPBootstrapper/1.0");

            using var resp = await http.GetAsync(DownloadUrl,
                                                 HttpCompletionOption.ResponseHeadersRead,
                                                 ct);

            resp.EnsureSuccessStatusCode();

            var total = resp.Content.Headers.ContentLength ?? -1;
            var canReport = total > 0;

            using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var dst = File.Create(destFile);

            var buffer = new byte[81920];
            long read = 0;
            int r;

            while ((r = await src.ReadAsync(buffer, 0, buffer.Length, ct)) > 0)
            {
                await dst.WriteAsync(buffer, 0, r, ct);
                read += r;

                if (canReport)
                {
                    double pct = (read * 100.0) / total;
                    progress?.Report(pct);
                    Progress = pct;
                }
            }

            progress?.Report(100);
            Progress = 100;
            DownloadedFilePath = destFile;
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ============================
    // = Extract ZIP contents
    // ============================
    public async Task<bool> ExtractAsync(string installDir, Action<string>? log = null)
    {
        if (DownloadedFilePath == null)
            return false;

        string zipPath = DownloadedFilePath;
        if (!File.Exists(zipPath))
            return false;

        try
        {
            log?.Invoke($"Extracting {Name}...");

            using FileStream zipStream = new FileStream(zipPath, FileMode.Open, FileAccess.Read);
            using ZipArchive archive = new ZipArchive(zipStream, ZipArchiveMode.Read);

            foreach (var entry in archive.Entries)
            {
                string entryPath = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

                bool isUI = (Name == "TeknoParrotUi");
                bool useFolderOverride = TryGetFolderOverride(out string folderOverride);

                string outputDir = installDir;

                if (isUI)
                {
                    // Extract in-place
                    outputDir = installDir;
                }
                else if (useFolderOverride)
                {
                    outputDir = Path.Combine(installDir, folderOverride);
                }

                // Handle directories
                if (entryPath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                {
                    Directory.CreateDirectory(Path.Combine(outputDir, entryPath));
                    continue;
                }

                // Construct output file
                string dest = Path.Combine(outputDir, entryPath);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

                // Replace existing file
                if (File.Exists(dest))
                {
                    try { File.Delete(dest); }
                    catch { File.Move(dest, dest + ".bak"); }
                }

                using var entryStream = entry.Open();
                using var destStream = File.Create(dest);
                await entryStream.CopyToAsync(destStream);

                log?.Invoke($"Extracted: {dest}");
            }

            IsInstalled = true;
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"ERROR extracting {zipPath}: {ex.Message}");
            return false;
        }
        finally
        {
            // remove the ZIP
            try { File.Delete(zipPath); }
            catch { }
        }
    }

    private bool TryGetFolderOverride(out string folder)
    {
        folder = Name switch
        {
            "TeknoParrot" => "TeknoParrot",
            "OpenSegaAPI" => "TeknoParrot",
            "OpenSndGaelco" => "TeknoParrot",
            "OpenSndVoyager" => "TeknoParrot",
            "TeknoParrotN2" => "N2",
            "SegaToolsTP" => "SegaTools",
            "OpenParrotWin32" => "OpenParrotWin32",
            "OpenParrotx64" => "OpenParrotx64",
            _ => ""
        };

        return folder != "";
    }
}
