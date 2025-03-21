using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell.Interop;

namespace SimpleCoverage
{
    /// <summary>
    /// This is the main package class for the SimpleCoverage extension
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [Guid(PackageGuidString)]
    [ProvideToolWindow(typeof(CoverageToolWindow), Style = VsDockStyle.Tabbed, Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057")]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class CoverageExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// Package GUID string.
        /// </summary>
        public const string PackageGuidString = "50D25A2E-A87F-45DB-A5FF-D5732063E5D9";

        /// <summary>
        /// Command Set GUID
        /// </summary>
        public const string CommandSetGuidString = "1D325698-B13C-4F9D-B177-71CAEB3425D1";

        /// <summary>
        /// Command IDs
        /// </summary>
        private const int CommandIdToolsMenu = 0x0100;
        private const int CommandIdOtherWindowsMenu = 0x0101;

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited.
        /// </summary>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread
            // Switch to the UI thread
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Register the menu commands
            await RegisterCommandsAsync();
        }

        /// <summary>
        /// Registers the commands with the command service
        /// </summary>
        private async Task RegisterCommandsAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the OleMenuCommandService from the package
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (commandService == null)
            {
                return;
            }

            // Create command IDs
            var toolsMenuCommandID = new CommandID(new Guid(CommandSetGuidString), CommandIdToolsMenu);
            var otherWindowsMenuCommandID = new CommandID(new Guid(CommandSetGuidString), CommandIdOtherWindowsMenu);

            // Create the menu commands
            var toolsMenuCommand = new MenuCommand(ShowToolWindowHandler, toolsMenuCommandID);
            var otherWindowsMenuCommand = new MenuCommand(ShowToolWindowHandler, otherWindowsMenuCommandID);

            // Add commands to the service
            commandService.AddCommand(toolsMenuCommand);
            commandService.AddCommand(otherWindowsMenuCommand);
        }

        /// <summary>
        /// Handler for menu command execution
        /// </summary>
        private void ShowToolWindowHandler(object sender, EventArgs e)
        {
            _ = ShowToolWindowAsync();
        }

        /// <summary>
        /// Shows the coverage tool window
        /// </summary>
        private async Task ShowToolWindowAsync()
        {
            await JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get the window
            var window = await FindToolWindowAsync(typeof(CoverageToolWindow), 0, true, DisposalToken);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create tool window");
            }

            // Show the window
            var windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        #endregion
    }
}