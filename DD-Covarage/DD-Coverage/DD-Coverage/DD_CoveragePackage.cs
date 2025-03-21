using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Task = System.Threading.Tasks.Task;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Reflection;
using System.IO;
using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;

namespace DD_Coverage
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the
    /// IVsPackage interface and uses the registration attributes defined in the framework to
    /// register itself and its components with the shell. These attributes tell the pkgdef creation
    /// utility what data to put into .pkgdef file.
    /// </para>
    /// <para>
    /// To get loaded into VS, the package must be referred by &lt;Asset Type="Microsoft.VisualStudio.VsPackage" ...&gt; in .vsixmanifest file.
    /// </para>
    /// </remarks>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)] // This attribute is needed to ensure correct cleanup during uninstall
    [Guid(DD_CoveragePackage.PackageGuidString)]
    [ProvideToolWindow(typeof(CoverageToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = "3ae79031-e1bc-11d0-8f78-00a0c9110057", // This is the output window GUID
        Orientation = ToolWindowOrientation.Right,
        MultiInstances = false,
        Transient = false)]
    [ProvideAutoLoad("f1536ef8-92ec-443c-9ed7-fdadf150da82", PackageAutoLoadFlags.BackgroundLoad)] // GUID for SolutionExists
    [ProvideMenuResource("Menus.ctmenu", 1)]
    public sealed class DD_CoveragePackage : AsyncPackage
    {
        /// <summary>
        /// DD_CoveragePackage GUID string.
        /// </summary>
        public const string PackageGuidString = "07164fef-2624-4882-a468-49f2eb356c67";

        // Command set GUID
        private const string CommandSetGuidString = "07164fef-2624-4882-a468-49f2eb356c68";

        // Command ID
        private const int CommandId = 0x0100;

        private CoverageService coverageService;
        private string currentVersion;
        private const string VersionRegistryKey = "CurrentVersion";

        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to monitor for initialization cancellation, which can occur when VS is shutting down.</param>
        /// <param name="progress">A provider for progress updates.</param>
        /// <returns>A task representing the async work of package initialization, or an already completed task if there is none. Do not return null from this method.</returns>
        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            // When initialized asynchronously, the current thread may be a background thread at this point.
            // Do any initialization that requires the UI thread after switching to the UI thread.
            Debug.WriteLine("DD-Coverage extension initializing...");
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Check for updates
            await HandleUpdateIfNeededAsync();

            // Initialize the coverage service
            coverageService = new CoverageService(this);

            // Register commands
            await RegisterCommandsAsync();

            // Show tool window automatically at startup
            Debug.WriteLine("DD-Coverage showing tool window...");
            try
            {
                ShowToolWindow();
                Debug.WriteLine("DD-Coverage tool window shown successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing DD-Coverage tool window: {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if the extension has been updated and performs cleanup if needed
        /// </summary>
        private async Task HandleUpdateIfNeededAsync()
        {
            try
            {
                // Get current version from assembly
                currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                Debug.WriteLine($"DD-Coverage current version: {currentVersion}");

                // Switch to UI thread for settings access
                await this.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Read previous version from user settings
                string previousVersion = GetExtensionSetting(VersionRegistryKey);
                Debug.WriteLine($"DD-Coverage previous version: {previousVersion}");

                if (string.IsNullOrEmpty(previousVersion))
                {
                    // First install
                    Debug.WriteLine("DD-Coverage first install detected");
                    SaveExtensionSetting(VersionRegistryKey, currentVersion);
                }
                else if (previousVersion != currentVersion)
                {
                    // Update detected
                    Debug.WriteLine($"DD-Coverage update detected: {previousVersion} -> {currentVersion}");
                    await CleanupForUpdateAsync();
                    SaveExtensionSetting(VersionRegistryKey, currentVersion);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error handling DD-Coverage update: {ex.Message}");
            }
        }

        /// <summary>
        /// Performs cleanup operations needed when updating the extension
        /// </summary>
        private async Task CleanupForUpdateAsync()
        {
            try
            {
                // Remove any existing tool windows
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var existingWindow = FindToolWindow(typeof(CoverageToolWindow), 0, false);
                if (existingWindow != null)
                {
                    Debug.WriteLine("Removing existing tool window during update");
                    // Close the window if it's open
                    if (existingWindow.Frame != null)
                    {
                        IVsWindowFrame windowFrame = (IVsWindowFrame)existingWindow.Frame;
                        windowFrame.Hide();
                    }
                }

                // Clear any cached data files
                ClearCachedFiles();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error during DD-Coverage update cleanup: {ex.Message}");
            }
        }

        /// <summary>
        /// Clears any cached files used by the extension
        /// </summary>
        private void ClearCachedFiles()
        {
            try
            {
                string tempFolderPath = Path.Combine(Path.GetTempPath(), "DD-Coverage");
                if (Directory.Exists(tempFolderPath))
                {
                    Debug.WriteLine($"Cleaning up DD-Coverage temp folder: {tempFolderPath}");
                    Directory.Delete(tempFolderPath, true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing DD-Coverage cache: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets a setting value from the extension's storage
        /// </summary>
        private string GetExtensionSetting(string key)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Get the settings manager
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var userSettingsStore = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                // Read setting if it exists
                string collectionPath = "DD-Coverage";
                if (userSettingsStore.CollectionExists(collectionPath) &&
                    userSettingsStore.PropertyExists(collectionPath, key))
                {
                    return userSettingsStore.GetString(collectionPath, key);
                }

                return "";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error reading DD-Coverage setting {key}: {ex.Message}");
                return "";
            }
        }

        /// <summary>
        /// Saves a setting value to the extension's storage
        /// </summary>
        private void SaveExtensionSetting(string key, string value)
        {
            try
            {
                ThreadHelper.ThrowIfNotOnUIThread();

                // Get the settings manager
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                var writableSettingsStore = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                // Create collection if it doesn't exist
                string collectionPath = "DD-Coverage";
                if (!writableSettingsStore.CollectionExists(collectionPath))
                {
                    writableSettingsStore.CreateCollection(collectionPath);
                }

                // Save the setting
                writableSettingsStore.SetString(collectionPath, key, value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error saving DD-Coverage setting {key}: {ex.Message}");
            }
        }

        private async Task RegisterCommandsAsync()
        {
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as IMenuCommandService;
            if (commandService == null)
            {
                Debug.WriteLine("DD-Coverage: IMenuCommandService not available");
                return;
            }

            try
            {
                var commandSet = new Guid(CommandSetGuidString);

                // Command in the Tools menu
                var menuCommandID = new CommandID(commandSet, CommandId);
                var menuItem = new MenuCommand(this.Execute, menuCommandID);
                commandService.AddCommand(menuItem);

                // Command in the View > Other Windows menu
                var showToolWindowCmdID = new CommandID(commandSet, 0x0101);
                var showToolWindowCmd = new MenuCommand(this.Execute, showToolWindowCmdID);
                commandService.AddCommand(showToolWindowCmd);

                Debug.WriteLine("DD-Coverage commands registered successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DD-Coverage command registration error: {ex.Message}");
            }
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                ShowToolWindow();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error showing DD-Coverage tool window: {ex.Message}");
            }
        }

        public void ShowToolWindow()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // First try getting existing window
            var window = this.FindToolWindow(typeof(CoverageToolWindow), 0, false);

            // If window doesn't exist yet, create it
            if (window == null)
            {
                window = this.FindToolWindow(typeof(CoverageToolWindow), 0, true);
                if (window?.Frame == null)
                {
                    throw new NotSupportedException("Cannot create DD-Coverage tool window");
                }
            }

            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());

            Debug.WriteLine("DD-Coverage tool window shown via direct command");
        }

        #endregion
    }
}
