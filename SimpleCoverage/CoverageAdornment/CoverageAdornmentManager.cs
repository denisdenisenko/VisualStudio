using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.IO;
using System.Windows;

namespace SimpleCoverage.CoverageAdornment
{
    /// <summary>
    /// Manages code coverage adornments across all editor windows
    /// </summary>
    [Export(typeof(CoverageAdornmentManager))]
    internal sealed class CoverageAdornmentManager
    {
        private readonly CoverageService coverageService;
        private readonly ConcurrentDictionary<IWpfTextView, bool> registeredViews;
        private bool isCoverageActive;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoverageAdornmentManager"/> class.
        /// </summary>
        [ImportingConstructor]
        public CoverageAdornmentManager()
        {
            coverageService = new CoverageService();
            registeredViews = new ConcurrentDictionary<IWpfTextView, bool>();
            isCoverageActive = false;
        }

        /// <summary>
        /// Registers a text view with the manager
        /// </summary>
        public void RegisterTextView(IWpfTextView textView)
        {
            registeredViews[textView] = true;
            textView.Closed += TextView_Closed;
        }

        /// <summary>
        /// Handles the text view closed event
        /// </summary>
        private void TextView_Closed(object sender, EventArgs e)
        {
            var textView = sender as IWpfTextView;
            if (textView != null)
            {
                textView.Closed -= TextView_Closed;
                registeredViews.TryRemove(textView, out _);
            }
        }

        /// <summary>
        /// Activates code coverage visualization
        /// </summary>
        public async void ActivateCoverage()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                await coverageService.GetCoverageDataAsync();
                isCoverageActive = true;
                RefreshAllViews();
            }
            catch
            {
                // Failed to activate
                isCoverageActive = false;
            }
        }

        /// <summary>
        /// Deactivates code coverage visualization
        /// </summary>
        public void DeactivateCoverage()
        {
            isCoverageActive = false;
            RefreshAllViews();
        }

        /// <summary>
        /// Refreshes all views
        /// </summary>
        public void RefreshAllViews()
        {
            foreach (var view in registeredViews.Keys)
            {
                RefreshView(view);
            }
        }

        /// <summary>
        /// Refreshes a single view
        /// </summary>
        public void RefreshView(IWpfTextView textView)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    // Trigger a redraw of the view
                    textView.VisualElement.InvalidateVisual();
                }
                catch
                {
                    // Ignore errors during refresh
                }
            });
        }

        /// <summary>
        /// Gets coverage information for a specific line in a file
        /// </summary>
        public CoverageStatus GetLineCoverageStatus(string filePath, int lineNumber)
        {
            if (!isCoverageActive)
                return CoverageStatus.NotCovered;

            try
            {
                var coverageInfo = coverageService.GetFileCoverage(filePath);
                if (coverageInfo == null)
                    return CoverageStatus.NotCovered;

                if (coverageInfo.LinesCovered.TryGetValue(lineNumber, out bool covered))
                {
                    return covered ? CoverageStatus.Covered : CoverageStatus.NotCovered;
                }

                return CoverageStatus.NotCovered;
            }
            catch
            {
                return CoverageStatus.NotCovered;
            }
        }

        /// <summary>
        /// Gets file coverage information
        /// </summary>
        public FileCoverageInfo GetFileCoverage(string filePath)
        {
            if (!isCoverageActive)
                return null;

            try
            {
                return coverageService.GetFileCoverage(filePath);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets whether code coverage is active
        /// </summary>
        public bool IsCoverageActive => isCoverageActive;
    }

    /// <summary>
    /// Represents the coverage status of a line
    /// </summary>
    public enum CoverageStatus
    {
        NotCovered,
        Covered
    }
}