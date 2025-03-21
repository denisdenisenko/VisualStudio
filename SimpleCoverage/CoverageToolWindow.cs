using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;

namespace SimpleCoverage
{
    /// <summary>
    /// This class implements the tool window for displaying code coverage
    /// </summary>
    [Guid("D9A59BBB-75C3-4D8A-BE5E-56F1D9B0E30B")]
    public class CoverageToolWindow : ToolWindowPane
    {
        private CoverageToolWindowControl control;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageToolWindow"/> class.
        /// </summary>
        public CoverageToolWindow() : base(null)
        {
            Caption = "Code Coverage (MSTest)";
            Content = control = new CoverageToolWindowControl();
        }
    }

    /// <summary>
    /// Control for the code coverage tool window
    /// </summary>
    public class CoverageToolWindowControl : UserControl
    {
        private readonly TreeView coverageTreeView;
        private readonly Button refreshButton;
        private readonly TextBlock statusText;
        private readonly CoverageService coverageService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageToolWindowControl"/> class.
        /// </summary>
        public CoverageToolWindowControl()
        {
            // Create UI components
            coverageTreeView = new TreeView
            {
                Margin = new Thickness(5)
            };

            refreshButton = new Button
            {
                Content = "Refresh Coverage",
                Height = 30,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 120
            };
            refreshButton.Click += RefreshButton_Click;

            statusText = new TextBlock
            {
                Text = "Ready",
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };

            // Create main layout
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Grid.SetRow(coverageTreeView, 0);
            mainGrid.Children.Add(coverageTreeView);

            // Create bottom panel with status and refresh button
            var bottomPanel = new Grid();
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(statusText, 0);
            Grid.SetColumn(refreshButton, 1);
            bottomPanel.Children.Add(statusText);
            bottomPanel.Children.Add(refreshButton);

            Grid.SetRow(bottomPanel, 1);
            mainGrid.Children.Add(bottomPanel);

            Content = mainGrid;

            // Initialize the coverage service
            coverageService = new CoverageService();

            // Load initial data
            LoadCoverageData();
        }

        /// <summary>
        /// Handler for the refresh button click event
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadCoverageData();
        }

        /// <summary>
        /// Loads and displays coverage data
        /// </summary>
        private async void LoadCoverageData()
        {
            try
            {
                statusText.Text = "Loading coverage data...";
                coverageTreeView.Items.Clear();

                // Get coverage data from the service
                var coverageData = await coverageService.GetCoverageDataAsync();

                if (coverageData.Count == 0)
                {
                    var message = "No MSTest coverage data found. Run tests with coverage collection.";
                    statusText.Text = message;

                    // Show information in the tree view
                    var infoItem = new TreeViewItem { Header = message };
                    var helpItem = new TreeViewItem
                    {
                        Header = "Run tests with: dotnet test --filter \"FullyQualifiedName~MSTest\" --collect:\"XPlat Code Coverage\""
                    };

                    coverageTreeView.Items.Add(infoItem);
                    coverageTreeView.Items.Add(helpItem);
                    return;
                }

                // Display coverage data
                DisplayCoverageData(coverageData);
                statusText.Text = $"Found coverage data for {coverageData.Count} projects";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Displays coverage data in the tree view
        /// </summary>
        private void DisplayCoverageData(Dictionary<string, double> coverageData)
        {
            var rootItem = new TreeViewItem
            {
                Header = "MSTest Coverage Results",
                IsExpanded = true
            };

            foreach (var item in coverageData.Keys)
            {
                var coverage = coverageData[item];
                var projectItem = new TreeViewItem
                {
                    Header = $"{item}: {coverage:F2}%",
                    Foreground = GetCoverageColor(coverage),
                    IsExpanded = true
                };

                rootItem.Items.Add(projectItem);
            }

            coverageTreeView.Items.Add(rootItem);
        }

        /// <summary>
        /// Gets a brush color based on the coverage percentage
        /// </summary>
        private Brush GetCoverageColor(double coverage)
        {
            if (coverage >= 80) return Brushes.Green;
            if (coverage >= 60) return Brushes.Orange;
            return Brushes.Red;
        }
    }
}