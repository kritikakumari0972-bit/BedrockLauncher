using BedrockLauncher.Handlers;
using BedrockLauncher.ViewModels;
using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace BedrockLauncher.Pages.Settings.Versions
{
    public partial class VersionsPage : Page
    {
        private bool hasInitalized = false;
        private string searchText = string.Empty;
        private bool showUnavailable = false;
        private bool cachedOnly = false;

        public VersionsPage()
        {
            this.DataContext = MainDataModel.Default;
            InitializeComponent();
        }

        private void Page_Initialized(object sender, EventArgs e)
        {

        }

        /// <summary>
        /// Opens Mojang's official, time-limited Bedrock trial for Windows.
        /// The trial is installed and entitlement-checked by Microsoft's official flow;
        /// this launcher does not host or unlock Minecraft game packages.
        /// </summary>
        private void OfficialTrialButton_Click(object sender, RoutedEventArgs e)
        {
            const string officialTrialUrl = "https://www.minecraft.net/en-us/free-trial";

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = officialTrialUrl,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"Could not open the official Minecraft free-trial page: {ex}");

                System.Windows.MessageBox.Show(
                    $"The official trial page could not be opened automatically.\n\nOpen this address in your browser:\n{officialTrialUrl}",
                    "Minecraft Free Trial",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void RefreshVersionView()
        {
            this.Dispatcher.Invoke(() =>
            {
                if (VersionsList != null)
                    Handlers.FilterSortingHandler.Refresh(VersionsList.ItemsSource);
            });
        }

        private void RefreshVersionsList(object sender, RoutedEventArgs e)
        {
            Properties.LauncherSettings.Default.Save();
            RefreshVersionView();
        }

        public void RefreshVersionRows() => this.Dispatcher.Invoke(() =>
        {
            foreach (var version in MainDataModel.Default.Versions)
                version.UpdateFolderSize();

            if (VersionsList != null)
                Handlers.FilterSortingHandler.Refresh(VersionsList.ItemsSource);
        });

        private async void PageHost_Loaded(object sender, RoutedEventArgs e)
        {
            if (!hasInitalized)
            {
                if (MainDataModel.Default.Versions.Count == 0)
                    await Task.Run(Program.OnApplicationRefresh);

                foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
                RefreshVersionsList(sender, e);
                hasInitalized = true;
            }
        }

        private async void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(Program.OnApplicationRefresh);
            foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
            RefreshVersionsList(sender, e);
        }

        private void CollectionViewSource_Filter(object sender, FilterEventArgs e)
        {
            if (!(e.Item is BedrockLauncher.Classes.MCVersion version))
            {
                e.Accepted = false;
                return;
            }

            bool isCatalogueEntry =
                !version.IsCustom &&
                version.CatalogValidation != null &&
                version.CatalogValidation.Source != BedrockLauncher.UpdateProcessor.Enums.VersionCatalogSource.Unknown;

            // Preserve locally imported versions, but don't expose synthetic "Latest Release" selector rows here.
            if (!isCatalogueEntry)
            {
                e.Accepted = version.IsCustom && version.IsInstalled &&
                             MatchesSearch(version);
                return;
            }

            var settings = Properties.LauncherSettings.Default;

            if (version.IsRelease && !settings.ShowReleases) { e.Accepted = false; return; }
            if (version.IsBeta && !settings.ShowBetas) { e.Accepted = false; return; }
            if (version.IsPreview && !settings.ShowPreviews) { e.Accepted = false; return; }
            if (version.PackageType == BedrockLauncher.UpdateProcessor.Enums.PackageType.UWP && !settings.ShowUWP) { e.Accepted = false; return; }
            if (version.PackageType == BedrockLauncher.UpdateProcessor.Enums.PackageType.GDK && !settings.ShowGDK) { e.Accepted = false; return; }

            if (cachedOnly && !version.IsCached) { e.Accepted = false; return; }

            if (!showUnavailable &&
                (version.CatalogValidation.Availability == BedrockLauncher.UpdateProcessor.Enums.VersionAvailability.Unavailable ||
                 version.CatalogValidation.Availability == BedrockLauncher.UpdateProcessor.Enums.VersionAvailability.UnsupportedArchitecture))
            {
                e.Accepted = false;
                return;
            }

            e.Accepted = MatchesSearch(version);
        }

        private bool MatchesSearch(BedrockLauncher.Classes.MCVersion version)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            string query = searchText.Trim();
            return (version.DisplayName?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (version.Name?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (version.Architecture?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (version.PackageTypeString?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                   (version.UUID?.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            searchText = SearchBox?.Text ?? string.Empty;
            RefreshVersionView();
        }

        private void LibraryFilter_Changed(object sender, RoutedEventArgs e)
        {
            showUnavailable = ShowUnavailableCheckBox?.IsChecked == true;
            cachedOnly = CachedOnlyCheckBox?.IsChecked == true;
            RefreshVersionView();
        }

        /// <summary>
        /// Adds the selected catalogue version to the current profile and selects it on the Play page.
        /// The existing Play action remains responsible for the real UWP/GDK package install and launch.
        /// </summary>
        public void AddVersionToPlay(BedrockLauncher.Classes.MCVersion version)
        {
            BedrockLauncher.Classes.BLInstallation installation =
                GetOrCreateInstallationForVersion(version);

            if (installation == null)
                return;

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToPlayScreen();
        }

        /// <summary>
        /// Installs the exact selected version for the active profile without launching Minecraft.
        /// UWP and GDK use their existing package handlers and the profile's own save-data directory.
        /// </summary>
        public async Task InstallVersion(BedrockLauncher.Classes.MCVersion version)
        {
            BedrockLauncher.Classes.BLInstallation installation =
                GetOrCreateInstallationForVersion(version);

            if (installation == null)
                return;

            var config = MainDataModel.Default.Config;
            var profile = config.CurrentProfile;
            string installationDataPath =
                MainDataModel.Default.FilePaths.GetInstallationPackageDataPath(
                    profile.UUID,
                    installation.DirectoryName_Full);

            bool installed;
            if (version.PackageType == BedrockLauncher.UpdateProcessor.Enums.PackageType.GDK)
            {
                installed = await MainDataModel.Default.PackageManager.InstallGdkPackage(
                    version,
                    installationDataPath);
            }
            else
            {
                installed = await MainDataModel.Default.PackageManager.InstallPackage(
                    version,
                    installationDataPath);
            }

            if (!installed)
                return;

            version.UpdateFolderSize();
            RefreshVersionRows();

            if (System.Windows.Application.Current.MainWindow is MainWindow mainWindow)
                mainWindow.NavigateToPlayScreen();
        }

        private BedrockLauncher.Classes.BLInstallation GetOrCreateInstallationForVersion(
            BedrockLauncher.Classes.MCVersion version)
        {
            if (version == null || !version.CanAddToPlay)
                return null;

            var config = MainDataModel.Default.Config;
            var profile = config.CurrentProfile;
            if (profile?.Installations == null)
                return null;

            BedrockLauncher.Classes.BLInstallation installation =
                profile.Installations.FirstOrDefault(x =>
                    x.VersioningMode == BedrockLauncher.Enums.VersioningMode.None &&
                    string.Equals(x.VersionUUID, version.UUID, StringComparison.OrdinalIgnoreCase));

            if (installation == null)
            {
                string baseName = $"Minecraft {version.Name} ({version.PackageType})";
                string installationName = baseName;
                int suffix = 2;

                while (profile.Installations.Any(x =>
                    string.Equals(x.DisplayName, installationName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(x.DirectoryName, installationName, StringComparison.OrdinalIgnoreCase)))
                {
                    installationName = $"{baseName} ({suffix++})";
                }

                config.Installation_Create(
                    installationName,
                    version,
                    installationName);

                installation = profile.Installations.FirstOrDefault(x =>
                    x.VersioningMode == BedrockLauncher.Enums.VersioningMode.None &&
                    string.Equals(x.VersionUUID, version.UUID, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.DisplayName, installationName, StringComparison.OrdinalIgnoreCase));
            }

            if (installation != null)
                config.CurrentInstallationUUID = installation.InstallationUUID;

            return installation;
        }

        private async void AddButton_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog()
            {
                Filter = "Minecraft Packages (*.appx;*.msix;*.msixbundle)|*.appx;*.msix;*.msixbundle|APPX Files (*.appx)|*.appx|MSIX Files (*.msix;*.msixbundle)|*.msix;*.msixbundle"
            };

            if (ofd.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string fileToImport = ofd.FileName;
                await MainDataModel.Default.PackageManager.AddPackage(fileToImport);
                await Task.Run(Program.OnApplicationRefresh);
                foreach (var ver in MainDataModel.Default.Versions) ver.UpdateFolderSize();
            }
        }
    }
}