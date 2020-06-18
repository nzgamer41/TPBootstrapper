﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace TPBootstrapper
{
    [Serializable]
    public class CoreItem
    {
        public string name;
        public string version;
        public string dlLink;
        public bool isInstalled = false;
        public bool needsUpdate = false;
        public bool isRequired = false;
        //download variables need to be nonserialized as we don't want to save these to the cache
        [NonSerialized] private LogHelper Logging;
        [NonSerialized] private ProgressBar _pb;
        [NonSerialized] private string _downloadDir;
        [NonSerialized] private bool isDone;

        public override string ToString()
        {
            string retVal;
            retVal = name + " v" + version + " ";

            if (isInstalled)
            {
                retVal += "[INSTALLED] ";
            }
            else if (needsUpdate)
            {
                retVal += "[NEEDS UPDATE] ";
            }
            else
            {
                retVal += "[NOT INSTALLED] ";
            }

            if (isRequired)
            {
                retVal += "[REQUIRED] ";
            }

            return retVal;
        }

        private void wc_DownloadProgressChanged(object sender, DownloadProgressChangedEventArgs e)
        {
            // In case you don't have a progressBar Log the value instead 
            // Console.WriteLine(e.ProgressPercentage);
            _pb.Value = e.ProgressPercentage;
        }

        public void handleDownload(LogHelper l, ProgressBar pb, string downloadDir)
        {
            Logging = l;
            _downloadDir = downloadDir;
            WebClient wc = new WebClient();
            _pb = pb;
            //selected = listBoxCores.SelectedIndex;
            try
            {
                using (wc)
                {
                    wc.DownloadProgressChanged += wc_DownloadProgressChanged;
                    wc.DownloadFileCompleted += wc_DownloadFileCompleted;
                    wc.DownloadFileAsync(new Uri(dlLink), _downloadDir + "\\" + Path.GetFileName(dlLink));
                }
            }
            catch (Exception ex)
            {

            }
        }

        private void wc_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            if (e.Cancelled)
            {
                try
                {

                }
                catch
                {
                    // ignored
                }


                return;
            }

            if (e.Error != null) // We have an error! Retry a few times, then abort.
            {
                try
                {

                }
                catch
                {
                    // ignored
                }
                return;
            }

            else
            {
                Logging.WriteLine("File downloaded, extracting..");
                handleExtraction();
            }

        }

        private async void handleExtraction()
        {
            new Thread(() =>
            {

                Thread.CurrentThread.IsBackground = true;
                using (FileStream zipToOpen = new FileStream(_downloadDir + "\\" + Path.GetFileName(dlLink), System.IO.FileMode.Open))
                {
                    using (ZipArchive archive = new ZipArchive(zipToOpen))
                    {
                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            bool isUI = false;
                            bool isUsingFolderOverride = false;
                            string folderOverride = "";
                            var Name = entry.FullName;
                            switch (name)
                            {
                                case "TeknoParrot":
                                    isUsingFolderOverride = true;
                                    folderOverride = "TeknoParrot";
                                    break;
                                case "OpenSegaAPI":
                                    isUsingFolderOverride = true;
                                    folderOverride = "TeknoParrot";
                                    break;
                                case "TeknoParrotN2":
                                    isUsingFolderOverride = true;
                                    folderOverride = "N2";
                                    break;
                                case "SegaToolsTP":
                                    isUsingFolderOverride = true;
                                    folderOverride = "SegaTools";
                                    break;
                                case "OpenParrotWin32":
                                    isUsingFolderOverride = true;
                                    folderOverride = "OpenParrotWin32";
                                    break;
                                case "OpenParrotx64":
                                    isUsingFolderOverride = true;
                                    folderOverride = "OpenParrotx64";
                                    break;

                            }
                            if (name == "TeknoParrotUi")
                            {
                                isUI = true;
                                //quick check if the folder exists
                                if (!Directory.Exists(Path.Combine(_downloadDir, Name)))
                                {
                                    Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(_downloadDir, Name)));
                                }
                            }

                            if (!Directory.Exists(Path.Combine(_downloadDir, folderOverride)))
                            {
                                Directory.CreateDirectory(Path.Combine(_downloadDir, folderOverride));
                            }

                            // directory
                            if (Name.EndsWith("/"))
                            {
                                Name = isUsingFolderOverride ? Path.Combine(folderOverride, Name) : Name;
                                Name = Path.Combine(_downloadDir, Name);
                                Directory.CreateDirectory(Name);
                                Logging.WriteLine($"Updater directory entry: {Name}");
                                continue;
                            }

                            if (isUI)
                            {
                                Name = Path.Combine(_downloadDir, Name);
                            }

                            var dest = isUI ? Name : Path.Combine(_downloadDir, folderOverride) + "\\" + Name;
                            Logging.WriteLine($"Updater file: {Name} extracting to: {dest}");

                            try
                            {
                                if (File.Exists(dest))
                                    File.Delete(dest);
                            }
                            catch (UnauthorizedAccessException)
                            {
                                // couldn't delete, just move for now
                                File.Move(dest, dest + ".bak");
                            }

                            try
                            {
                                using (var entryStream = entry.Open())
                                using (var dll = File.Create(dest))
                                {
                                    entryStream.CopyTo(dll);
                                }
                            }
                            catch (Exception ex)
                            {
                                Logging.WriteLine(ex.Message);
                            }
                        }
                    }
                }

                isDone = true;
                Logging.WriteLine("Zip extracted");
            }).Start();

            while (!isDone)
            {
                await Task.Delay(25);
            }

            Logging.WriteLine(name + " Downloaded Successfully!");
            isInstalled = true;
            MainWindow mw = (MainWindow) Application.Current.MainWindow;
            mw.updateListBox();
            try
            {
                File.Delete(_downloadDir + "\\" + Path.GetFileName(dlLink));
            }
            catch
            {
                Logging.WriteLine("Failed to delete archive");
            }
            mw.cacheCoreList.Add((CoreItem)this);
            mw.saveCache();
        }
    }
}
