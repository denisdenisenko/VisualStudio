using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SimpleCoverage.CoverageAdornment
{
    /// <summary>
    /// Text view creation listener for coverage highlighting
    /// </summary>
    [Export(typeof(IWpfTextViewCreationListener))]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CoverageAdornmentTextViewCreationListener : IWpfTextViewCreationListener
    {
        [Import]
        private CoverageAdornmentManager AdornmentManager { get; set; }

        /// <summary>
        /// Called when a text view is created
        /// </summary>
        public void TextViewCreated(IWpfTextView textView)
        {
            // Create the adornment
            new CoverageLineAdornment(textView, AdornmentManager);
        }
    }

    /// <summary>
    /// Adornment for highlighting covered/uncovered lines in the editor
    /// </summary>
    internal sealed class CoverageLineAdornment
    {
        private readonly IWpfTextView textView;
        private readonly IAdornmentLayer adornmentLayer;
        private readonly CoverageAdornmentManager adornmentManager;
        private readonly Dictionary<int, System.Windows.UIElement> elements;
        private readonly string filePath;
        private FileCoverageInfo fileCoverage;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageLineAdornment"/> class.
        /// </summary>
        public CoverageLineAdornment(IWpfTextView textView, CoverageAdornmentManager adornmentManager)
        {
            this.textView = textView;
            this.adornmentManager = adornmentManager;
            this.elements = new Dictionary<int, System.Windows.UIElement>();

            // Use the Text adornment layer (predefined)
            adornmentLayer = textView.GetAdornmentLayer(PredefinedAdornmentLayers.Text);

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

            // Register with the adornment manager
            adornmentManager.RegisterTextView(textView);

            // Set up events
            textView.LayoutChanged += TextView_LayoutChanged;
            textView.Closed += TextView_Closed;

            // Initial coverage check
            fileCoverage = adornmentManager.GetFileCoverage(filePath);
        }

        /// <summary>
        /// Handles the text view closed event
        /// </summary>
        private void TextView_Closed(object sender, EventArgs e)
        {
            textView.LayoutChanged -= TextView_LayoutChanged;
            textView.Closed -= TextView_Closed;

            // Clear adornments
            ClearAdornments();
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
            adornmentLayer.RemoveAllAdornments();
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
                    adornmentLayer.RemoveAdornment(element);
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

            // Get highlight color
            var color = GetHighlightColor(status);
            if (color == Brushes.Transparent) // Check if brush is transparent
                return;

            // Create the visual element for the line
            var rect = new Rectangle
            {
                Fill = color,
                Width = textView.ViewportWidth,
                Height = line.Height
            };

            // Create canvas for positioning
            var canvas = new Canvas { Width = textView.ViewportWidth, Height = line.Height };
            canvas.Children.Add(rect);

            // Add the adornment
            adornmentLayer.AddAdornment(
                AdornmentPositioningBehavior.TextRelative,
                line.Extent,
                null,
                canvas,
                (sender, e) => { elements.Remove(lineNumber); });

            // Store reference
            elements[lineNumber] = canvas;
        }

        /// <summary>
        /// Gets the highlight color for a coverage status
        /// </summary>
        private Brush GetHighlightColor(CoverageStatus status)
        {
            switch (status)
            {
                case CoverageStatus.Covered:
                    return new SolidColorBrush(Color.FromArgb(20, 0, 180, 0)); // Transparent green
                case CoverageStatus.NotCovered:
                    return new SolidColorBrush(Color.FromArgb(30, 220, 0, 0)); // Transparent red
                default:
                    return Brushes.Transparent;
            }
        }
    }
}