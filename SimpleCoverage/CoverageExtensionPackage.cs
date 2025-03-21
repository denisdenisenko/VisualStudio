using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;

namespace SimpleCoverage
{
    /// <summary>
    /// This is the main package class for the SimpleCoverage extension
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideToolWindow(typeof(CoverageToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    public sealed class CoverageExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// Package GUID string.
        /// </summary>
        public const string PackageGuidString = "50D25A2E-A87F-45DB-A5FF-D5732063E5D9";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread
            // Switch to the UI thread
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Create and show tool window
            await ShowToolWindowAsync();
        }

        /// <summary>
        /// Shows the coverage tool window
        /// </summary>
        private async Task ShowToolWindowAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            // Create the window if it doesn't exist yet
            var window = await FindToolWindowAsync(typeof(CoverageToolWindow), 0, true, DisposalToken);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create tool window");
            }
        }

        #endregion
    }
}