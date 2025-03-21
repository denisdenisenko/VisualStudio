using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Windows.Input;
using System.IO;
using Microsoft.VisualStudio.Shell.Interop;
using System.Collections.ObjectModel;
using System.Linq;
using SimpleCoverage.CoverageAdornment;

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
    /// Model for coverage tree view item
    /// </summary>
    public class CoverageTreeItem
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public double Coverage { get; set; }
        public int TotalLines { get; set; }
        public int CoveredLines { get; set; }
        public CoverageTreeItemType ItemType { get; set; }
        public ObservableCollection<CoverageTreeItem> Children { get; } = new ObservableCollection<CoverageTreeItem>();
    }

    /// <summary>
    /// Type of coverage tree item
    /// </summary>
    public enum CoverageTreeItemType
    {
        Root,
        Project,
        File,
        Namespace,
        Class,
        Method
    }

    /// <summary>
    /// Control for the code coverage tool window
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
            // Initialize services
            coverageService = new CoverageService();
            adornmentManager = new CoverageAdornmentManager();
            fileCoverageDetails = new Dictionary<string, FileCoverageInfo>();
            isHighlightingEnabled = false;

            // Create UI components
            coverageTreeView = new TreeView
            {
                Margin = new Thickness(5)
            };
            coverageTreeView.MouseDoubleClick += CoverageTreeView_MouseDoubleClick;

            refreshButton = new Button
            {
                Content = "Refresh Coverage",
                Height = 30,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 120
            };
            refreshButton.Click += RefreshButton_Click;

            showHideButton = new Button
            {
                Content = "Show Highlighting",
                Height = 30,
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Left,
                Width = 130
            };
            showHideButton.Click += ShowHideButton_Click;

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

            // Create bottom panel with status and buttons
            var bottomPanel = new Grid();
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bottomPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Grid.SetColumn(statusText, 0);
            Grid.SetColumn(showHideButton, 1);
            Grid.SetColumn(refreshButton, 2);
            bottomPanel.Children.Add(statusText);
            bottomPanel.Children.Add(showHideButton);
            bottomPanel.Children.Add(refreshButton);

            Grid.SetRow(bottomPanel, 1);
            mainGrid.Children.Add(bottomPanel);

            Content = mainGrid;

            // Load initial data
            LoadCoverageData();
        }

        /// <summary>
        /// Handles double-click on a tree view item
        /// </summary>
        private void CoverageTreeView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var item = coverageTreeView.SelectedItem as TreeViewItem;
            if (item?.Tag is CoverageTreeItem coverageItem && !string.IsNullOrEmpty(coverageItem.Path))
            {
                // Only navigate to files
                if (coverageItem.ItemType == CoverageTreeItemType.File)
                {
                    OpenDocument(coverageItem.Path);
                }
            }
        }

        /// <summary>
        /// Handler for the show/hide button click event
        /// </summary>
        private void ShowHideButton_Click(object sender, RoutedEventArgs e)
        {
            if (isHighlightingEnabled)
            {
                // Disable highlighting
                adornmentManager.DeactivateCoverage();
                isHighlightingEnabled = false;
                showHideButton.Content = "Show Highlighting";
            }
            else
            {
                // Enable highlighting
                adornmentManager.ActivateCoverage();
                isHighlightingEnabled = true;
                showHideButton.Content = "Hide Highlighting";
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
                    var menuItem = new TreeViewItem
                    {
                        Header = "Access via: Tools > Code Coverage (MSTest), Window > Code Coverage (MSTest), or Tools > Show Code Coverage (MSTest)"
                    };

                    coverageTreeView.Items.Add(infoItem);
                    coverageTreeView.Items.Add(helpItem);
                    coverageTreeView.Items.Add(menuItem);

                    // Disable highlighting
                    adornmentManager.DeactivateCoverage();
                    isHighlightingEnabled = false;
                    showHideButton.Content = "Show Highlighting";

                    return;
                }

                // Load file coverage details
                LoadFileCoverageDetails();

                // Display coverage data
                DisplayCoverageData(coverageData);
                statusText.Text = $"Found coverage data for {coverageData.Count} projects";

                // If highlighting was enabled, update it
                if (isHighlightingEnabled)
                {
                    adornmentManager.ActivateCoverage();
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Loads file coverage details
        /// </summary>
        private void LoadFileCoverageDetails()
        {
            fileCoverageDetails.Clear();

            // Copy coverage details from the service to our local dictionary
            var fileInfos = typeof(CoverageService).GetField("fileCoverageData",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (fileInfos != null)
            {
                var coverageData = fileInfos.GetValue(coverageService) as Dictionary<string, FileCoverageInfo>;
                if (coverageData != null)
                {
                    foreach (var entry in coverageData)
                    {
                        fileCoverageDetails[entry.Key] = entry.Value;
                    }
                }
            }
        }

        /// <summary>
        /// Displays coverage data in the tree view
        /// </summary>
        private void DisplayCoverageData(Dictionary<string, double> coverageData)
        {
            var rootItem = new TreeViewItem
            {
                Header = CreateHeaderWithCoverage("MSTest Coverage Results",
                    coverageData.Values.Average(),
                    fileCoverageDetails.Values.Sum(f => f.TotalLines),
                    fileCoverageDetails.Values.Sum(f => f.CoveredLines)),
                IsExpanded = true
            };

            // Create a root coverage item for the tag
            var rootCoverageItem = new CoverageTreeItem
            {
                Name = "MSTest Coverage Results",
                Coverage = coverageData.Values.Average(),
                TotalLines = fileCoverageDetails.Values.Sum(f => f.TotalLines),
                CoveredLines = fileCoverageDetails.Values.Sum(f => f.CoveredLines),
                ItemType = CoverageTreeItemType.Root
            };
            rootItem.Tag = rootCoverageItem;

            // Group files by project
            var filesByProject = new Dictionary<string, List<FileCoverageInfo>>();
            foreach (var file in fileCoverageDetails.Values)
            {
                string projectName = GetProjectName(file.FilePath);
                if (!filesByProject.ContainsKey(projectName))
                {
                    filesByProject[projectName] = new List<FileCoverageInfo>();
                }
                filesByProject[projectName].Add(file);
            }

            // Add projects
            foreach (var projectEntry in filesByProject)
            {
                var projectFiles = projectEntry.Value;
                var projectTotalLines = projectFiles.Sum(f => f.TotalLines);
                var projectCoveredLines = projectFiles.Sum(f => f.CoveredLines);
                var projectCoverage = projectTotalLines > 0 ?
                    (projectCoveredLines / (double)projectTotalLines) * 100.0 : 0.0;

                var projectItem = new TreeViewItem
                {
                    Header = CreateHeaderWithCoverage(projectEntry.Key, projectCoverage, projectTotalLines, projectCoveredLines),
                    IsExpanded = false
                };

                // Create project coverage item for the tag
                var projectCoverageItem = new CoverageTreeItem
                {
                    Name = projectEntry.Key,
                    Coverage = projectCoverage,
                    TotalLines = projectTotalLines,
                    CoveredLines = projectCoveredLines,
                    ItemType = CoverageTreeItemType.Project
                };
                projectItem.Tag = projectCoverageItem;

                // Add to root item's children
                rootCoverageItem.Children.Add(projectCoverageItem);

                // Add files to project
                foreach (var file in projectFiles.OrderBy(f => Path.GetFileName(f.FilePath)))
                {
                    var fileName = Path.GetFileName(file.FilePath);
                    var fileItem = new TreeViewItem
                    {
                        Header = CreateHeaderWithCoverage(fileName, file.CoveragePercentage, file.TotalLines, file.CoveredLines),
                        IsExpanded = false
                    };

                    // Create file coverage item for the tag
                    var fileCoverageItem = new CoverageTreeItem
                    {
                        Name = fileName,
                        Path = file.FilePath,
                        Coverage = file.CoveragePercentage,
                        TotalLines = file.TotalLines,
                        CoveredLines = file.CoveredLines,
                        ItemType = CoverageTreeItemType.File
                    };
                    fileItem.Tag = fileCoverageItem;

                    // Add to project's children
                    projectCoverageItem.Children.Add(fileCoverageItem);

                    // Add file details - lines by coverage status
                    AddFileDetailsToItem(fileItem, file);

                    projectItem.Items.Add(fileItem);
                }

                rootItem.Items.Add(projectItem);
            }

            coverageTreeView.Items.Add(rootItem);
        }

        /// <summary>
        /// Adds file details to a tree view item
        /// </summary>
        private void AddFileDetailsToItem(TreeViewItem fileItem, FileCoverageInfo file)
        {
            // Sort line numbers
            var coveredLines = file.LinesCovered
                .Where(l => l.Value)
                .Select(l => l.Key)
                .OrderBy(n => n)
                .ToList();

            var uncoveredLines = file.LinesCovered
                .Where(l => !l.Value)
                .Select(l => l.Key)
                .OrderBy(n => n)
                .ToList();

            // Add covered lines item
            if (coveredLines.Count > 0)
            {
                var coveredLinesItem = new TreeViewItem
                {
                    Header = $"Covered Lines ({coveredLines.Count})",
                    Foreground = Brushes.Green,
                    IsExpanded = false
                };

                // Group lines into ranges for easier visualization
                var ranges = GroupLinesIntoRanges(coveredLines);
                foreach (var range in ranges)
                {
                    coveredLinesItem.Items.Add(new TreeViewItem
                    {
                        Header = range,
                        Foreground = Brushes.Green
                    });
                }

                fileItem.Items.Add(coveredLinesItem);
            }

            // Add uncovered lines item
            if (uncoveredLines.Count > 0)
            {
                var uncoveredLinesItem = new TreeViewItem
                {
                    Header = $"Uncovered Lines ({uncoveredLines.Count})",
                    Foreground = Brushes.Red,
                    IsExpanded = false
                };

                // Group lines into ranges for easier visualization
                var ranges = GroupLinesIntoRanges(uncoveredLines);
                foreach (var range in ranges)
                {
                    uncoveredLinesItem.Items.Add(new TreeViewItem
                    {
                        Header = range,
                        Foreground = Brushes.Red
                    });
                }

                fileItem.Items.Add(uncoveredLinesItem);
            }
        }

        /// <summary>
        /// Groups a list of line numbers into ranges for easier display
        /// </summary>
        private List<string> GroupLinesIntoRanges(List<int> lines)
        {
            var result = new List<string>();
            if (lines.Count == 0)
                return result;

            int rangeStart = lines[0];
            int rangeEnd = lines[0];

            for (int i = 1; i < lines.Count; i++)
            {
                if (lines[i] == rangeEnd + 1)
                {
                    // Continue the range
                    rangeEnd = lines[i];
                }
                else
                {
                    // End the current range and start a new one
                    if (rangeStart == rangeEnd)
                    {
                        result.Add($"Line {rangeStart}");
                    }
                    else
                    {
                        result.Add($"Lines {rangeStart}-{rangeEnd}");
                    }

                    rangeStart = lines[i];
                    rangeEnd = lines[i];
                }
            }

            // Add the last range
            if (rangeStart == rangeEnd)
            {
                result.Add($"Line {rangeStart}");
            }
            else
            {
                result.Add($"Lines {rangeStart}-{rangeEnd}");
            }

            return result;
        }

        /// <summary>
        /// Creates a header with coverage information
        /// </summary>
        private StackPanel CreateHeaderWithCoverage(string text, double coverage, int totalLines, int coveredLines)
        {
            var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

            var textBlock = new TextBlock
            {
                Text = text,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetCoverageColor(coverage)
            };

            var coverageBlock = new TextBlock
            {
                Text = $" - {coverage:F2}% ({coveredLines}/{totalLines} lines)",
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = GetCoverageColor(coverage)
            };

            headerPanel.Children.Add(textBlock);
            headerPanel.Children.Add(coverageBlock);

            return headerPanel;
        }

        /// <summary>
        /// Gets the project name from a file path
        /// </summary>
        private string GetProjectName(string filePath)
        {
            try
            {
                var fileName = Path.GetFileName(filePath);
                var directory = Path.GetDirectoryName(filePath);

                if (string.IsNullOrEmpty(directory))
                    return "Unknown";

                // Try to find .csproj file in parent directories
                var currentDir = directory;
                while (!string.IsNullOrEmpty(currentDir))
                {
                    var projFiles = Directory.GetFiles(currentDir, "*.csproj");
                    if (projFiles.Length > 0)
                    {
                        return Path.GetFileNameWithoutExtension(projFiles[0]);
                    }

                    // Move up one directory
                    currentDir = Path.GetDirectoryName(currentDir);
                }

                // If no project file found, use the parent directory name
                return Path.GetFileName(directory);
            }
            catch
            {
                return "Unknown";
            }
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

        /// <summary>
        /// Opens a document in the editor
        /// </summary>
        private void OpenDocument(string filePath)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // Get the DTE service
                    var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                    if (dte != null)
                    {
                        // Open the document
                        dte.ItemOperations.OpenFile(filePath);
                    }
                }
                catch
                {
                    // Ignore errors when opening document
                }
            });
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
    }
}