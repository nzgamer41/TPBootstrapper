﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Octokit;
using Ookii.Dialogs.Wpf;
using Application = System.Windows.Application;
using Path = System.IO.Path;
using System.IO.Compression;
using FileMode = System.IO.FileMode;
using System.Runtime.InteropServices;
using AutoUpdaterDotNET;
using System.Diagnostics;
using System.Dynamic;

namespace TPBootstrapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public static LogHelper Logging = new LogHelper(true);
        private int selected = 0;
        string[] listOfComponents = {"TeknoParrotUi","OpenSegaAPI","TeknoParrot","TeknoParrotN2","OpenParrotWin32","OpenParrotx64", "OpenSndGaelco", "OpenSndVoyager"};
        private string downloadDir = Directory.GetCurrentDirectory();
        List<CoreItem> coreList = new List<CoreItem>();
        public List<CoreItem> cacheCoreList = new List<CoreItem>();
        List<ProgressBar> pbList = new List<ProgressBar>();
        private bool isDone;
        private CoreItem currentDl;
        public MainWindow()
        {
            InitializeComponent();

            if (!checkInternetConnection())
            {
                MessageBox.Show("Sorry, but this app requires an active internet connection to work.","No internet detected!");
            }
#if !DEBUG
            checkForInstallUpdate();
#endif  
            checkForCores();

            addRedists();
        }
        //Creating the extern function...  
        [DllImport("wininet.dll")]
        private extern static bool InternetGetConnectedState(out int Description, int ReservedValue);
        //Creating a function that uses the API function...  

        private bool checkInternetConnection()
        {
            int Desc;
            return InternetGetConnectedState(out Desc, 0);
        }
        // 
        private async void initSetup()
        {
            if (File.Exists(downloadDir + "\\tpcache.dat"))
            {
                cacheCoreList = ReadFromBinaryFile<List<CoreItem>>(downloadDir + "\\tpcache.dat");
            }
            if (cacheCoreList.Count > 0 && coreList.Count > 0)
            {
                foreach (CoreItem c in coreList)
                {
                    foreach (CoreItem cc in cacheCoreList)
                    {
                        if (cc.isInstalled && cc.version == c.version && cc.name == c.name)
                        {
                            c.isInstalled = true;
                        }
                        else if (cc.isInstalled && cc.name == c.name)
                        {
                            c.needsUpdate = true;
                        }
                    }
                }
                updateListBox();
            }
        }

        private void checkForInstallUpdate()
        {
            Logging.WriteLine("Checking for new TP Installer version...");
            //TODO: use autoupdater .net thing later
            //https://github.com/nzgamer41/TPBootstrapper/releases/latest/download/autoupdate.xml
            AutoUpdater.Start("https://github.com/nzgamer41/TPBootstrapper/releases/latest/download/autoupdate.xml");
        }

        private async void addRedists()
        {
            CoreItem directX = new CoreItem();
            directX.dlLink = "https://download.microsoft.com/download/1/7/1/1718CCC4-6315-4D8E-9543-8E28A4E18C4C/dxwebsetup.exe";
            directX.isRedist = true;
            directX.name = "DirectX Runtimes";
            directX.version = "9.0";
            //CoreItem vcr = new CoreItem();
            //vcr.dlLink = "http://nzgamer41.win/TeknoParrot/TPRedists/vcr.zip";
            //vcr.isRedist = true;
            //vcr.name = "Visual C++ Redistributibles (2005-2019)";
            //vcr.version = "N/A";
            coreList.Add(directX);
            //coreList.Add(vcr);
            ProgressBar pbdx = new ProgressBar();
            ListBoxItem lbdx = new ListBoxItem();
            pbdx.Height = 16;
            lbdx.Content = pbdx;
            pbList.Add(pbdx);
            listBoxCoresDl.Items.Add(lbdx);
            listBoxCores.Items.Add(directX.ToString());
            //ProgressBar pbvc = new ProgressBar();
            //ListBoxItem lbvc = new ListBoxItem();
            //pbvc.Height = 16;
            //lbvc.Content = pbvc;
            //pbList.Add(pbvc);
            //listBoxCoresDl.Items.Add(lbvc);
            //listBoxCores.Items.Add(vcr.ToString());

        }

        private async void checkForCores()
        {
            
            try
            {
                foreach (string s in listOfComponents)
                {
                    CoreItem temp = await Check(s);
                    if (temp.name != null)
                    {
                        coreList.Add(temp);
                    }

                    ProgressBar pb = new ProgressBar();
                    ListBoxItem lb = new ListBoxItem();
                    pb.Height = 16;
                    lb.Content = pb;
                    if (temp.name != null)
                    {
                        pbList.Add(pb);
                        listBoxCoresDl.Items.Add(lb);
                        listBoxCores.Items.Add(temp.ToString());
                    }
                }
            }
            catch (Octokit.RateLimitExceededException ex)
            {
                Logging.WriteLine("GitHub rate limit reached!");
                MessageBox.Show(
                    "Unfortunately you have reached the hourly rate limit on GitHub's side, you will need to wait an hour or two before running this again. There is nothing we (teknogods) can do about this for now.",
                    "GitHub rate limit exceeded!");
                Application.Current.Shutdown();
            }
        }

        public void updateListBox()
        {
            listBoxCores.Items.Clear();
            foreach (CoreItem c in coreList)
            {
                listBoxCores.Items.Add(c.ToString());
            }
        }

        private void buttonDlSelected_Click(object sender, RoutedEventArgs e)
        {
            if (listBoxCores.SelectedIndex > 0)
            {
                coreList[listBoxCores.SelectedIndex].handleDownload(Logging, pbList[listBoxCores.SelectedIndex], downloadDir);
            }
            else
            {
                MessageBox.Show("Please select a core to download, or click Full Install to download all cores and redists.");
            }
        }

        private void listBoxCores_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (listBoxCores.SelectedIndex != -1)
            {
                if (coreList[listBoxCores.SelectedIndex].isInstalled)
                {
                    buttonDlSelected.IsEnabled = false;
                }
                else if (textBoxInstallDir.Text != "")
                {
                    buttonDlSelected.IsEnabled = true;
                }
            }
        }

        public static async Task<CoreItem> Check(string componentToCheck, bool isLimited = false)
        {
            var github = new GitHubClient(new ProductHeaderValue("TPBootstrapper"));

            var versionPattern = @"\d{1}\.\d{1}\.\d{1}\.\d{1,3}";
            CoreItem temp = new CoreItem();
            temp.name = componentToCheck;
            //jump in first
            if (componentToCheck == "SegaToolsTP")
            {
                var releases = await github.Repository.Release.GetAll("nzgamer41", componentToCheck);
                var latest = releases[0];
                temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                var match = Regex.Match(latest.Name, versionPattern);
                temp.version = match.Groups[0].Value;
                return temp;
            }


            if (componentToCheck != "OpenParrotWin32" && componentToCheck != "OpenParrotx64" && componentToCheck != "TeknoParrot" && componentToCheck != "TeknoParrotN2")
            {
                var releases = await github.Repository.Release.GetAll("teknogods", componentToCheck);
                var latest = releases[0];
                temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                temp.version = latest.Name;
                if (componentToCheck == "TeknoParrotUi")
                {
                    temp.isRequired = true;
                }

                return temp;
            }
            else if (componentToCheck != "TeknoParrot" && componentToCheck != "TeknoParrotN2")
            {
                var releases = await github.Repository.Release.GetAll("teknogods", "OpenParrot");
                if (componentToCheck == "OpenParrotWin32")
                {
                    for (int i = 0; i < releases.Count; i++)
                    {
                        var latest = releases[i];
                        if (latest.TagName == "OpenParrotWin32")
                        {
                            temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                            var match = Regex.Match(latest.Name, versionPattern);
                            temp.version = match.Groups[0].Value;
                            return temp;
                        }
                    }
                }
                else if (componentToCheck == "OpenParrotx64")
                {
                    //checking openparrot64
                    for (int i = 0; i < releases.Count; i++)
                    {
                        var latest = releases[i];
                        if (latest.TagName == "OpenParrotx64")
                        {
                            temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                            var match = Regex.Match(latest.Name, versionPattern);
                            temp.version = match.Groups[0].Value;
                            return temp;
                        }
                    }
                }
            }
            else
            {
                var releases = await github.Repository.Release.GetAll("teknogods", "TeknoParrot");

                if (componentToCheck == "TeknoParrot")
                {
                    for (int i = 0; i < releases.Count; i++)
                    {
                        var latest = releases[i];
                        if (latest.TagName == "TeknoParrot")
                        {
                            if (latest.Assets.Count > 0)
                            {
                                temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                                var match = Regex.Match(latest.Name, versionPattern);
                                temp.version = match.Groups[0].Value;
                                return temp;
                            }
                        }
                    }
                }
                else if (componentToCheck == "TeknoParrotN2")
                {
                    for (int i = 0; i < releases.Count; i++)
                    {
                        var latest = releases[i];
                        if (latest.TagName == "TeknoParrotN2")
                        {
                            temp.dlLink = latest.Assets[0].BrowserDownloadUrl;
                            var match = Regex.Match(latest.Name, versionPattern);
                            temp.version = match.Groups[0].Value;
                            return temp;
                        }
                    }
                }
                else
                {
                    throw new Exception("Invalid request!");
                }

            }

            Logging.WriteLine("Checking for a new version failed!");
            return new CoreItem();
        }

        private void buttonBrowse_Click(object sender, RoutedEventArgs e)
        {
            VistaFolderBrowserDialog dlg = new VistaFolderBrowserDialog();
            dlg.SelectedPath = Directory.GetCurrentDirectory();
            if (dlg.ShowDialog() == true)
            {
                textBoxInstallDir.Text = dlg.SelectedPath;
                buttonDlSelected.IsEnabled = true;
                buttonFullInstall.IsEnabled = true;
                downloadDir = dlg.SelectedPath;
                initSetup();
            }
        }

        public void saveCache()
        {
            WriteToBinaryFile(downloadDir + "\\tpcache.dat", cacheCoreList, false);
        }

        /// <summary>
        /// Writes the given object instance to a binary file.
        /// <para>Object type (and all child types) must be decorated with the [Serializable] attribute.</para>
        /// <para>To prevent a variable from being serialized, decorate it with the [NonSerialized] attribute; cannot be applied to properties.</para>
        /// </summary>
        /// <typeparam name="T">The type of object being written to the binary file.</typeparam>
        /// <param name="filePath">The file path to write the object instance to.</param>
        /// <param name="objectToWrite">The object instance to write to the binary file.</param>
        /// <param name="append">If false the file will be overwritten if it already exists. If true the contents will be appended to the file.</param>
        public static void WriteToBinaryFile<T>(string filePath, T objectToWrite, bool append = false)
        {
            using (Stream stream = File.Open(filePath, append ? FileMode.Append : FileMode.Create))
            {
                var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                binaryFormatter.Serialize(stream, objectToWrite);
            }
        }

        /// <summary>
        /// Reads an object instance from a binary file.
        /// </summary>
        /// <typeparam name="T">The type of object to read from the binary file.</typeparam>
        /// <param name="filePath">The file path to read the object instance from.</param>
        /// <returns>Returns a new instance of the object read from the binary file.</returns>
        public static T ReadFromBinaryFile<T>(string filePath)
        {
            using (Stream stream = File.Open(filePath, FileMode.Open))
            {
                var binaryFormatter = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                return (T)binaryFormatter.Deserialize(stream);
            }
        }

        private void buttonFullInstall_Click(object sender, RoutedEventArgs e)
        {
            buttonFullInstall.IsEnabled = false;
            Logging.WriteLine("Downloading all cores for full install...");
            for (int i = 0; i < coreList.Count; i++)
            {
                coreList[i].handleDownload(Logging, pbList[i], downloadDir);
            }
            bool isDone = false;
            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                /* run your code here */
                while (!isDone)
                {
                    foreach (CoreItem c in coreList)
                    {
                        if (c.isInstalled)
                        {
                            isDone = true;
                            continue;
                        }
                        else
                        {
                            isDone = false;
                            break;
                        }
                    }
                }
                MessageBox.Show("TeknoParrot has downloaded successfully!", "Download complete");
                this.Dispatcher.Invoke(() =>
                {
                    Process.Start(downloadDir + ".\\TeknoParrotUi.exe");
                    this.Close();
                });
            }).Start();
        }
    }
}
