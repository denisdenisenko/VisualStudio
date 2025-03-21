using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SimpleCoverage.CoverageAdornment
{
    /// <summary>
    /// Visual element that shows code coverage in the editor margin
    /// </summary>
    internal sealed class CoverageMarginElement : Canvas, IWpfTextViewMargin
    {
        private const double MarginWidth = 7.0;

        private readonly IWpfTextView textView;
        private readonly CoverageAdornmentManager adornmentManager;
        private readonly Dictionary<int, UIElement> elements;
        private readonly string filePath;
        private bool isDisposed;
        private FileCoverageInfo fileCoverage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageMarginElement"/> class.
        /// </summary>
        public CoverageMarginElement(IWpfTextView textView, CoverageAdornmentManager adornmentManager)
        {
            this.textView = textView;
            this.adornmentManager = adornmentManager;
            this.elements = new Dictionary<int, UIElement>();

            // Get current file path
            ITextDocument document;
            if (textView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out document))
            {
                filePath = document.FilePath;
            }
            else
            {
                filePath = string.Empty;
            }

            // Initial coverage check
            fileCoverage = adornmentManager.GetFileCoverage(filePath);

            // Register with the adornment manager
            adornmentManager.RegisterTextView(textView);

            // Set up events
            textView.LayoutChanged += TextView_LayoutChanged;

            // Initial UI setup
            Width = MarginWidth;
            ClipToBounds = true;
            Background = Brushes.Transparent;
        }

        /// <summary>
        /// Handles layout changes in the text view
        /// </summary>
        private void TextView_LayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // Check if coverage is activated
            if (!adornmentManager.IsCoverageActive)
            {
                ClearAdornments();
                return;
            }

            // Check if file coverage is available
            fileCoverage = adornmentManager.GetFileCoverage(filePath);
            if (fileCoverage == null)
            {
                ClearAdornments();
                return;
            }

            // Update adornments for visible lines
            UpdateAdornments();
        }

        /// <summary>
        /// Clears all adornments
        /// </summary>
        private void ClearAdornments()
        {
            Children.Clear();
            elements.Clear();
        }

        /// <summary>
        /// Updates adornments for visible lines
        /// </summary>
        private void UpdateAdornments()
        {
            foreach (var line in textView.TextViewLines)
            {
                var lineNumber = line.Start.GetContainingLine().LineNumber + 1;
                CreateAdornmentForLine(line, lineNumber);
            }

            // Remove elements for lines that are no longer visible
            var visibleLines = new HashSet<int>();
            foreach (var line in textView.TextViewLines)
            {
                visibleLines.Add(line.Start.GetContainingLine().LineNumber + 1);
            }

            var keysToRemove = new List<int>();
            foreach (var key in elements.Keys)
            {
                if (!visibleLines.Contains(key))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                if (elements.TryGetValue(key, out var element))
                {
                    Children.Remove(element);
                    elements.Remove(key);
                }
            }
        }

        /// <summary>
        /// Creates an adornment for a single line
        /// </summary>
        private void CreateAdornmentForLine(ITextViewLine line, int lineNumber)
        {
            // Skip if the line is already adorned
            if (elements.ContainsKey(lineNumber))
                return;

            // Check line coverage status
            var status = adornmentManager.GetLineCoverageStatus(filePath, lineNumber);

            // Create the visual element for the line
            var rect = new System.Windows.Shapes.Rectangle
            {
                Width = MarginWidth,
                Height = line.Height,
                Fill = GetCoverageColor(status)
            };

            // Set tooltip
            string tooltipText = status == CoverageStatus.Covered
                ? "Line is covered by tests"
                : "Line is not covered by tests";

            ToolTipService.SetToolTip(rect, tooltipText);

            // Position the element
            Canvas.SetTop(rect, line.Top);
            Canvas.SetLeft(rect, 0);

            // Add to the visual tree
            Children.Add(rect);
            elements[lineNumber] = rect;
        }

        /// <summary>
        /// Gets the color for a coverage status
        /// </summary>
        private Brush GetCoverageColor(CoverageStatus status)
        {
            switch (status)
            {
                case CoverageStatus.Covered:
                    return new SolidColorBrush(Color.FromRgb(0, 180, 0));
                case CoverageStatus.NotCovered:
                    return new SolidColorBrush(Color.FromRgb(220, 0, 0));
                default:
                    return Brushes.Transparent;
            }
        }

        #region IWpfTextViewMargin Members

        /// <summary>
        /// Gets the <see cref="FrameworkElement"/> that renders the margin.
        /// </summary>
        public FrameworkElement VisualElement => this;

        /// <summary>
        /// Gets the size of the margin.
        /// </summary>
        public double MarginSize => MarginWidth;

        /// <summary>
        /// Gets a value indicating whether the margin is enabled.
        /// </summary>
        public bool Enabled => true;

        /// <summary>
        /// Gets the <see cref="ITextViewMargin"/> with the specified margin name.
        /// </summary>
        public ITextViewMargin GetTextViewMargin(string marginName)
        {
            return string.Compare(marginName, "CoverageAdornment", StringComparison.OrdinalIgnoreCase) == 0
                ? this
                : null;
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Releases all resources used by this object.
        /// </summary>
        public void Dispose()
        {
            if (!isDisposed)
            {
                GC.SuppressFinalize(this);

                if (textView != null)
                {
                    textView.LayoutChanged -= TextView_LayoutChanged;
                }

                ClearAdornments();

                isDisposed = true;
            }
        }

        #endregion
    }
}