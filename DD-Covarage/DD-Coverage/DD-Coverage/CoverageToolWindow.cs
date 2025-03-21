using Microsoft.VisualStudio.Shell;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Threading;
using System.Runtime.InteropServices;
using System.Reflection;

namespace DD_Coverage
{
    [Guid("B55E3C8C-0296-4F2F-8F21-A5F59FC5529E")]
    public class CoverageToolWindow : ToolWindowPane
    {
        private CoverageToolWindowControl control;

        public CoverageToolWindow() : base(null)
        {
            this.Caption = "Test Coverage";
            this.Content = control = new CoverageToolWindowControl();
        }
    }

    public partial class CoverageToolWindowControl : UserControl
    {
        public CoverageService coverageService;
        private readonly TreeView coverageTreeView;
        private readonly ProgressBar progressBar;
        private readonly TextBlock statusText;
        private readonly TextBlock versionText;
        private readonly string currentVersion;

        public CoverageToolWindowControl()
        {
            // Get version
            try
            {
                currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            }
            catch
            {
                currentVersion = "1.1";
            }

            // Initialize controls
            statusText = new TextBlock
            {
                Text = "Ready",
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };

            versionText = new TextBlock
            {
                Text = $"v{currentVersion}",
                Margin = new Thickness(5),
                HorizontalAlignment = HorizontalAlignment.Right,
                Foreground = Brushes.Gray,
                FontSize = 10
            };

            progressBar = new ProgressBar
            {
                Height = 20,
                Margin = new Thickness(5),
                Visibility = Visibility.Collapsed
            };

            coverageTreeView = new TreeView
            {
                Margin = new Thickness(5)
            };

            // Initialize service
            coverageService = new CoverageService(Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.IVsPackage)) as IServiceProvider);

            // Initialize UI
            InitializeComponent();

            // Load initial data
            LoadCoverageData();
        }

        private void InitializeComponent()
        {
            var mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            mainGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var statusGrid = new Grid();
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusGrid.Children.Add(statusText);
            statusGrid.Children.Add(progressBar);
            statusGrid.Children.Add(versionText);
            Grid.SetColumn(progressBar, 1);
            Grid.SetColumn(versionText, 2);

            // Refresh button
            var refreshButton = new Button
            {
                Content = "Refresh Coverage",
                Height = 30,
                Margin = new Thickness(5),
                VerticalAlignment = VerticalAlignment.Center
            };
            refreshButton.Click += (s, e) => LoadCoverageData();

            var buttonGrid = new Grid();
            buttonGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            buttonGrid.Children.Add(refreshButton);

            // Add elements to main grid
            Grid.SetRow(statusGrid, 0);
            Grid.SetRow(coverageTreeView, 1);
            Grid.SetRow(buttonGrid, 2);

            mainGrid.Children.Add(statusGrid);
            mainGrid.Children.Add(coverageTreeView);
            mainGrid.Children.Add(buttonGrid);

            this.Content = mainGrid;
        }

        public void RefreshCoverage()
        {
            LoadCoverageData();
        }

        private async void LoadCoverageData()
        {
            try
            {
                statusText.Text = "Analyzing coverage...";
                progressBar.Visibility = Visibility.Visible;
                coverageTreeView.Items.Clear();

                var result = await coverageService.AnalyzeCoverageAsync();
                if (result.Success)
                {
                    DisplayCoverageData(result.CoverageData);
                    statusText.Text = "Coverage analysis completed";
                }
                else
                {
                    statusText.Text = result.Message;
                }
            }
            catch (Exception ex)
            {
                statusText.Text = $"Error: {ex.Message}";
            }
            finally
            {
                progressBar.Visibility = Visibility.Collapsed;
            }
        }

        private void DisplayCoverageData(Dictionary<string, double> coverageData)
        {
            var rootNode = new TreeViewItem
            {
                Header = "Coverage Results",
                IsExpanded = true
            };

            foreach (var item in coverageData)
            {
                var node = new TreeViewItem
                {
                    Header = $"{item.Key}: {item.Value:F2}%",
                    Foreground = GetCoverageColor(item.Value)
                };
                rootNode.Items.Add(node);
            }

            coverageTreeView.Items.Add(rootNode);
        }

        private Brush GetCoverageColor(double coverage)
        {
            if (coverage >= 80) return Brushes.Green;
            if (coverage >= 60) return Brushes.Orange;
            return Brushes.Red;
        }
    }
}