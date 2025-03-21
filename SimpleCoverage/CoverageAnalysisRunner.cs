using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleCoverage
{
    /// <summary>
    /// Handles running code coverage analysis after tests complete
    /// </summary>
    public class CoverageAnalysisRunner
    {
        private readonly DTE2 _dte;
        private readonly CoverageAdornmentManager _adornmentManager;
        private readonly CoverageToolWindow _toolWindow;

        /// <summary>
        /// Initializes a new coverage analysis runner
        /// </summary>
        /// <param name="dte">The DTE service</param>
        /// <param name="adornmentManager">The coverage adornment manager</param>
        public CoverageAnalysisRunner(DTE2 dte, CoverageAdornmentManager adornmentManager)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = dte;
            _adornmentManager = adornmentManager;

            // Get the tool window
            _toolWindow = CoverageExtensionPackage.GetToolWindow();
        }

        /// <summary>
        /// Runs code coverage analysis asynchronously
        /// </summary>
        public async Task RunCoverageAnalysisAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                // Wait a bit for test files to be written to disk
                await Task.Delay(1000);

                string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
                string[] coverageFiles = FindCoverageFiles(solutionPath);

                if (coverageFiles.Length > 0)
                {
                    Debug.WriteLine($"Found {coverageFiles.Length} coverage files");

                    // Refresh the tool window with the latest coverage data
                    if (_toolWindow != null)
                    {
                        var control = _toolWindow.Content as CoverageToolWindowControl;
                        control?.RefreshCoverage();
                    }
                }
                else
                {
                    Debug.WriteLine("No coverage files found");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error running coverage analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds coverage files in the solution directory
        /// </summary>
        private string[] FindCoverageFiles(string baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory) || !Directory.Exists(baseDirectory))
                return Array.Empty<string>();

            // Look for coverage files in standard locations
            var coveragePatterns = new[] {
                "**/coverage.cobertura.xml",
                "**/coverage.opencover.xml",
                "**/TestResults/**/*.coverage",
                "**/TestResults/**/*.cobertura.xml",
                "**/TestResults/**/*.opencover.xml"
            };

            var result = coveragePatterns
                .SelectMany(pattern => Directory.GetFiles(baseDirectory, Path.GetFileName(pattern), SearchOption.AllDirectories))
                .Where(file => File.Exists(file))
                .OrderByDescending(File.GetLastWriteTime)
                .ToArray();

            return result;
        }
    }
}