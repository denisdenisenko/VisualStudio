using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.IO;

namespace SimpleCoverage
{
    /// <summary>
    /// Interaction logic for CoverageToolWindowControl.
    /// </summary>
    public class CoverageToolWindowControl : UserControl
    {
        private readonly TreeView coverageTreeView;
        private readonly Button refreshButton;
        private readonly Button showHideButton;
        private readonly TextBlock statusText;
        private readonly CoverageService coverageService;
        private readonly CoverageAdornmentManager adornmentManager;
        private Dictionary<string, FileCoverageInfo> fileCoverageDetails;
        private bool isHighlightingEnabled;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageToolWindowControl"/> class.
        /// </summary>
        public CoverageToolWindowControl()
        {
            // Create services
            coverageService = new CoverageService();

            // Get adornment manager from package
            adornmentManager = new CoverageAdornmentManager();

            // Set up layout
            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Create toolbar
            var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(5) };

            refreshButton = new Button
            {
                Content = "Refresh Coverage",
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 5, 0)
            };
            refreshButton.Click += RefreshButton_Click;

            showHideButton = new Button
            {
                Content = "Show Highlighting",
                Padding = new Thickness(5)
            };
            showHideButton.Click += ShowHideButton_Click;

            toolbar.Children.Add(refreshButton);
            toolbar.Children.Add(showHideButton);

            Grid.SetRow(toolbar, 0);
            grid.Children.Add(toolbar);

            // Create tree view
            coverageTreeView = new TreeView
            {
                Margin = new Thickness(5)
            };

            Grid.SetRow(coverageTreeView, 1);
            grid.Children.Add(coverageTreeView);

            // Create status text
            statusText = new TextBlock
            {
                Margin = new Thickness(5),
                Text = "No coverage data loaded."
            };

            Grid.SetRow(statusText, 2);
            grid.Children.Add(statusText);

            // Set content
            Content = grid;

            // Load coverage data
            LoadCoverageData();
        }

        /// <summary>
        /// Public method to refresh coverage data
        /// </summary>
        public void RefreshCoverage()
        {
            LoadCoverageData();

            // Enable highlighting by default when refreshing from external source
            if (!isHighlightingEnabled)
            {
                isHighlightingEnabled = true;
                adornmentManager.ActivateCoverage();
                showHideButton.Content = "Hide Highlighting";
            }
        }

        /// <summary>
        /// Handler for the show/hide button click event
        /// </summary>
        private void ShowHideButton_Click(object sender, RoutedEventArgs e)
        {
            isHighlightingEnabled = !isHighlightingEnabled;

            if (isHighlightingEnabled)
            {
                showHideButton.Content = "Hide Highlighting";
                adornmentManager.ActivateCoverage();
            }
            else
            {
                showHideButton.Content = "Show Highlighting";
                adornmentManager.DeactivateCoverage();
            }
        }

        /// <summary>
        /// Handler for the refresh button click event
        /// </summary>
        private void RefreshButton_Click(object sender, RoutedEventArgs e)
        {
            LoadCoverageData();
        }

        /// <summary>
        /// Loads coverage data and updates the UI
        /// </summary>
        private async void LoadCoverageData()
        {
            try
            {
                // Show loading status
                statusText.Text = "Loading coverage data...";

                // Clear tree view
                coverageTreeView.Items.Clear();

                // Load coverage data
                fileCoverageDetails = await coverageService.LoadCoverageDataAsync();

                // Update adornment manager
                adornmentManager.UpdateCoverageData(fileCoverageDetails);

                // If no data, show error
                if (fileCoverageDetails.Count == 0)
                {
                    statusText.Text = "No coverage data found. Run tests with code coverage to generate data.";
                    return;
                }

                // Get coverage file path
                string coverageFile = coverageService.CoverageFilePath;

                // Populate tree view
                var filesByDirectory = fileCoverageDetails
                    .GroupBy(f => Path.GetDirectoryName(f.Key))
                    .OrderBy(g => g.Key);

                foreach (var directoryGroup in filesByDirectory)
                {
                    string directoryName = directoryGroup.Key;

                    // Create directory node
                    var directoryNode = new TreeViewItem
                    {
                        Header = GetFriendlyDirectoryName(directoryName),
                        IsExpanded = directoryGroup.Count() < 5 // Only expand small directories
                    };

                    // Calculate directory coverage
                    int totalCoverableLines = 0;
                    int totalCoveredLines = 0;

                    foreach (var fileCoverage in directoryGroup)
                    {
                        totalCoverableLines += fileCoverage.Value.CoverableLines;
                        totalCoveredLines += fileCoverage.Value.CoveredLines;

                        // Create file node
                        var fileNode = new TreeViewItem
                        {
                            Header = FormatFileNode(fileCoverage.Value)
                        };

                        directoryNode.Items.Add(fileNode);
                    }

                    // Update directory header with coverage
                    double directoryCoverage = totalCoverableLines > 0
                        ? (double)totalCoveredLines / totalCoverableLines * 100
                        : 0;

                    directoryNode.Header = $"{GetFriendlyDirectoryName(directoryName)} ({directoryCoverage:F1}%)";

                    // Add directory node
                    coverageTreeView.Items.Add(directoryNode);
                }

                // Calculate total coverage
                int overallCoverableLines = fileCoverageDetails.Values.Sum(f => f.CoverableLines);
                int overallCoveredLines = fileCoverageDetails.Values.Sum(f => f.CoveredLines);
                double overallCoverage = overallCoverableLines > 0
                    ? (double)overallCoveredLines / overallCoverableLines * 100
                    : 0;

                // Update status text
                statusText.Text = $"Coverage: {overallCoverage:F1}% ({overallCoveredLines}/{overallCoverableLines} lines) - {Path.GetFileName(coverageFile)}";
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error loading coverage data: {ex.Message}";
            }
        }

        /// <summary>
        /// Formats a file node with coverage information
        /// </summary>
        private string FormatFileNode(FileCoverageInfo fileCoverage)
        {
            string fileName = Path.GetFileName(fileCoverage.FilePath);
            double coveragePercentage = fileCoverage.CoveragePercentage;

            return $"{fileName} ({coveragePercentage:F1}%)";
        }

        /// <summary>
        /// Gets a friendly directory name for display
        /// </summary>
        private string GetFriendlyDirectoryName(string directoryPath)
        {
            if (string.IsNullOrEmpty(directoryPath))
                return "(Unknown)";

            string directoryName = Path.GetFileName(directoryPath);

            if (string.IsNullOrEmpty(directoryName))
                return Path.GetPathRoot(directoryPath);

            return directoryName;
        }
    }
}