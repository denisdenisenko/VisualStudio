using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace SimpleCoverage
{
    /// <summary>
    /// Manages coverage adornments in the editor
    /// </summary>
    public class CoverageAdornmentManager
    {
        private Dictionary<string, FileCoverageInfo> _fileCoverageData;
        private bool _highlightingEnabled = false;

        /// <summary>
        /// Initializes a new coverage adornment manager
        /// </summary>
        public CoverageAdornmentManager()
        {
            _fileCoverageData = new Dictionary<string, FileCoverageInfo>();
        }

        /// <summary>
        /// Updates the coverage data
        /// </summary>
        /// <param name="fileCoverageData">The new coverage data</param>
        public void UpdateCoverageData(Dictionary<string, FileCoverageInfo> fileCoverageData)
        {
            _fileCoverageData = fileCoverageData ?? new Dictionary<string, FileCoverageInfo>();

            // If highlighting is enabled, refresh all windows
            if (_highlightingEnabled)
            {
                RefreshAllVisibleEditors();
            }
        }

        /// <summary>
        /// Activates coverage highlighting in all open editors
        /// </summary>
        public void ActivateCoverage()
        {
            _highlightingEnabled = true;
            RefreshAllVisibleEditors();
        }

        /// <summary>
        /// Deactivates coverage highlighting in all open editors
        /// </summary>
        public void DeactivateCoverage()
        {
            _highlightingEnabled = false;
            RefreshAllVisibleEditors();
        }

        /// <summary>
        /// Gets the coverage info for a specific line in a file
        /// </summary>
        /// <param name="filePath">The file path</param>
        /// <param name="lineNumber">The line number (1-based)</param>
        /// <returns>The coverage status of the line</returns>
        public LineCoverageStatus GetLineCoverageStatus(string filePath, int lineNumber)
        {
            if (!_highlightingEnabled || string.IsNullOrEmpty(filePath))
                return LineCoverageStatus.NotCovered;

            // Normalize file path
            filePath = filePath.Replace("/", "\\").ToLowerInvariant();

            // Find the file in our coverage data
            if (_fileCoverageData.TryGetValue(filePath, out var fileInfo))
            {
                // Check if we have coverage data for this line
                if (fileInfo.LineCoverage.TryGetValue(lineNumber, out var status))
                {
                    return status;
                }
            }

            return LineCoverageStatus.NotCovered;
        }

        /// <summary>
        /// Refreshes all visible editors to update coverage highlighting
        /// </summary>
        private void RefreshAllVisibleEditors()
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // Get all open text views
                    var componentModel = CoverageExtensionPackage.GetComponentModel();
                    var textEditorFactory = componentModel.GetService<IWpfTextViewFactoryService>();
                    if (textEditorFactory == null)
                        return;

                    // Go through all active text views
                    var textViews = textEditorFactory.TextViews.ToList();
                    foreach (var textView in textViews)
                    {
                        if (!textView.Properties.TryGetProperty(typeof(CoverageLineAdornment), out CoverageLineAdornment adornment))
                        {
                            // Create new adornment if necessary
                            adornment = new CoverageLineAdornment(textView, this);
                            textView.Properties.AddProperty(typeof(CoverageLineAdornment), adornment);
                        }

                        // Force a redraw of the view
                        textView.LayoutChanged += (sender, e) => { };
                        textView.VisualElement.InvalidateVisual();
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error refreshing editors: {ex.Message}");
            }
        }
    }
}