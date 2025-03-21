using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Formatting;

namespace SimpleCoverage
{
    /// <summary>
    /// CoverageLineAdornment adorns the editor with coverage highlighting
    /// </summary>
    internal sealed class CoverageLineAdornment
    {
        private readonly IWpfTextView textView;
        private readonly IAdornmentLayer adornmentLayer;
        private readonly CoverageAdornmentManager coverageManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageLineAdornment"/> class.
        /// </summary>
        /// <param name="textView">The text view to create the adornment for</param>
        /// <param name="coverageManager">The coverage manager</param>
        public CoverageLineAdornment(IWpfTextView textView, CoverageAdornmentManager coverageManager)
        {
            this.textView = textView ?? throw new ArgumentNullException(nameof(textView));
            this.coverageManager = coverageManager ?? throw new ArgumentNullException(nameof(coverageManager));

            // Use the Text adornment layer (predefined)
            adornmentLayer = textView.GetAdornmentLayer(PredefinedAdornmentLayers.Text);

            // Listen to layout and caret position changed events
            this.textView.LayoutChanged += OnLayoutChanged;
        }

        /// <summary>
        /// Handles the layout changed event
        /// </summary>
        private void OnLayoutChanged(object sender, TextViewLayoutChangedEventArgs e)
        {
            // Clear all existing adornments
            adornmentLayer.RemoveAllAdornments();

            // Get the filepath for the current document
            string filePath = GetDocumentFilePath();
            if (string.IsNullOrEmpty(filePath))
                return;

            // Add adornments for visible lines
            foreach (ITextViewLine line in textView.TextViewLines)
            {
                // Get line number (1-based)
                var lineNumber = textView.TextSnapshot.GetLineNumberFromPosition(line.Start.Position) + 1;

                // Get coverage status for this line
                var status = coverageManager.GetLineCoverageStatus(filePath, lineNumber);

                // Skip if not covered
                if (status == LineCoverageStatus.NotCovered)
                    continue;

                // Create the highlight
                CreateHighlight(line, status);
            }
        }

        /// <summary>
        /// Creates a highlight adornment for a line
        /// </summary>
        private void CreateHighlight(ITextViewLine line, LineCoverageStatus status)
        {
            // Get highlight color
            var color = GetHighlightColor(status);
            if (color == Brushes.Transparent) // Check if brush is transparent
                return;

            // Create highlight rectangle
            Rectangle rect = new Rectangle()
            {
                Fill = color,
                Opacity = 0.3,
                Width = textView.ViewportWidth,
                Height = line.Height
            };

            Canvas.SetLeft(rect, line.TextLeft);
            Canvas.SetTop(rect, line.TextTop);

            // Add the adornment to the layer
            adornmentLayer.AddAdornment(AdornmentPositioningBehavior.TextRelative, line.Extent, null, rect, null);
        }

        /// <summary>
        /// Gets the highlight color for a coverage status
        /// </summary>
        private Brush GetHighlightColor(LineCoverageStatus status)
        {
            switch (status)
            {
                case LineCoverageStatus.Covered:
                    return Brushes.LightGreen;
                case LineCoverageStatus.PartiallyCovered:
                    return Brushes.Yellow;
                case LineCoverageStatus.NotCovered:
                    return Brushes.Transparent;
                default:
                    return Brushes.Transparent;
            }
        }

        /// <summary>
        /// Gets the file path for the current document
        /// </summary>
        private string GetDocumentFilePath()
        {
            ITextDocument document;
            if (textView.TextDataModel.DocumentBuffer.Properties.TryGetProperty(typeof(ITextDocument), out document))
            {
                return document.FilePath;
            }

            return null;
        }
    }
}