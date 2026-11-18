using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BedrockLauncher.Classes;

namespace BedrockLauncher.Pages.Settings.Versions
{
    /// <summary>
    /// Read-only details for one exact Bedrock package catalogue entry or imported local version.
    /// </summary>
    public sealed class VersionDetailsWindow : Window
    {
        private readonly MCVersion version;

        public VersionDetailsWindow(MCVersion version)
        {
            this.version = version ?? throw new ArgumentNullException(nameof(version));

            Title = "Minecraft version details";
            Width = 760;
            Height = 650;
            MinWidth = 620;
            MinHeight = 520;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            ResizeMode = ResizeMode.CanResize;
            Background = new SolidColorBrush(Color.FromRgb(31, 31, 31));
            Foreground = Brushes.White;

            var root = new DockPanel { Margin = new Thickness(24) };

            var footer = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 16, 0, 0)
            };

            var openFolder = new Button
            {
                Content = "Open version folder",
                MinWidth = 132,
                Margin = new Thickness(0, 0, 8, 0),
                Padding = new Thickness(12, 7, 12, 7),
                IsEnabled = version.IsCached
            };
            openFolder.Click += (_, _) => version.OpenDirectory();

            var close = new Button
            {
                Content = "Close",
                MinWidth = 84,
                Padding = new Thickness(12, 7, 12, 7),
                IsDefault = true
            };
            close.Click += (_, _) => Close();

            footer.Children.Add(openFolder);
            footer.Children.Add(close);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            var content = new StackPanel();
            scroll.Content = content;
            root.Children.Add(scroll);

            content.Children.Add(new TextBlock
            {
                Text = version.DisplayName,
                FontSize = 24,
                FontWeight = FontWeights.Bold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 5)
            });

            content.Children.Add(new TextBlock
            {
                Text = version.LibraryStatusText,
                FontSize = 14,
                Foreground = new SolidColorBrush(Color.FromRgb(160, 210, 160)),
                Margin = new Thickness(0, 0, 0, 18)
            });

            AddSection(content, "Version identity",
                ("Minecraft version", version.Name),
                ("Release channel", version.Type.ToString()),
                ("Package type", version.PackageType.ToString()),
                ("Architecture", version.Architecture),
                ("Catalogue / update ID", version.UUID),
                ("Package ID", version.PackageID),
                ("Required GDK package", version.RequiredGdkPackage?.FullName ??
                    (version.PackageType == BedrockLauncher.UpdateProcessor.Enums.PackageType.GDK
                        ? "Unresolved — this entry cannot be installed"
                        : "Not applicable to UWP")));

            AddSection(content, "Catalogue and availability",
                ("Availability", version.CatalogValidation?.Availability.ToString() ?? "Local import / not catalogued"),
                ("Catalogue source", version.LibrarySourceText),
                ("Reason", string.IsNullOrWhiteSpace(version.LibraryStatusReason)
                    ? "No additional availability warning."
                    : version.LibraryStatusReason));

            AddSection(content, "Local files",
                ("Cache state", version.IsCached ? "Cached locally" : "Not cached"),
                ("Version folder", version.GameDirectory),
                ("GDK package file", version.PackageType == BedrockLauncher.UpdateProcessor.Enums.PackageType.GDK
                    ? (version.GdkPackageFilePath ?? "No exact package path is available.")
                    : "Not applicable to UWP"));

            AddSection(content, "Package integrity and trust",
                ("UWP validation", "The launcher checks the extracted AppxManifest identity against the selected Minecraft family and architecture before promoting the extracted folder."),
                ("GDK validation", "The launcher keeps the selected exact package identity bound to its cache path; Gaming Services / Windows performs package validation during deployment."),
                ("Trusted upstream digest", "The current upstream catalogues do not provide a trusted, signed SHA-256 manifest. A hash calculated locally can detect later file changes, but cannot prove Microsoft authorship or package authenticity."));

            Content = root;
        }

        private static void AddSection(
            Panel parent,
            string heading,
            params (string Label, string Value)[] rows)
        {
            var section = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            section.Children.Add(new TextBlock
            {
                Text = heading,
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(130, 190, 235)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            foreach (var row in rows)
            {
                var item = new StackPanel { Margin = new Thickness(0, 0, 0, 9) };
                item.Children.Add(new TextBlock
                {
                    Text = row.Label,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(165, 165, 165))
                });
                item.Children.Add(new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(row.Value) ? "Not available" : row.Value,
                    FontSize = 13,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    IsTextSelectionEnabled = true
                });
                section.Children.Add(item);
            }

            section.Children.Add(new Border
            {
                Height = 1,
                Background = new SolidColorBrush(Color.FromRgb(65, 65, 65)),
                Margin = new Thickness(0, 2, 0, 0)
            });

            parent.Children.Add(section);
        }
    }
}
