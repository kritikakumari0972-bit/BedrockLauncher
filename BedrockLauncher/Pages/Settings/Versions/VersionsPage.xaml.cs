using BedrockLauncher.Handlers;
using BedrockLauncher.ViewModels;
using System;
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