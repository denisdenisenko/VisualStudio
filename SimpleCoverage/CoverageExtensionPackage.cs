using Microsoft.VisualStudio.Shell;
using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.ComponentModel;
using System.ComponentModel.Design;
using Microsoft.VisualStudio.Shell.Interop;
using SimpleCoverage.CoverageAdornment;
using EnvDTE;
using EnvDTE80;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.ComponentModelHost;

namespace SimpleCoverage
{
    /// <summary>
    /// This is the main package class for the SimpleCoverage extension
    /// </summary>
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [InstalledProductRegistration("#110", "#112", "1.0", IconResourceID = 400)]
    [Guid(CoverageExtensionPackage.PackageGuidString)]
    [ProvideToolWindow(typeof(CoverageToolWindow),
        Style = VsDockStyle.Tabbed,
        Window = "DocumentWell",
        Orientation = ToolWindowOrientation.Right)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [ProvideService(typeof(CoverageAdornmentManager), IsAsyncQueryable = true)]
    [ProvideAutoLoad(UIContextGuids80.SolutionExists, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class CoverageExtensionPackage : AsyncPackage
    {
        /// <summary>
        /// CoverageExtensionPackage GUID string.
        /// </summary>
        public const string PackageGuidString = "d9a9e2fd-77f3-439a-8a02-9bd33b5285ed";

        /// <summary>
        /// Command set GUID
        /// </summary>
        public static readonly Guid CommandSet = new Guid("3fe6f668-4369-4be8-bb54-8ec7e97afd0c");

        /// <summary>
        /// Command IDs
        /// </summary>
        public const int CommandIdToolsMenu = 0x0100;
        public const int CommandIdOtherWindowsMenu = 0x0101;
        public const int CommandIdTestMenu = 0x0102;

        // Coverage adornment manager instance
        private CoverageAdornmentManager adornmentManager;

        // DTE for test running
        private DTE2 dte;

        // Test event tracker
        private TestEventTracker testEventTracker;

        // Coverage analysis runner
        private CoverageAnalysisRunner coverageRunner;

        // Static instance for accessing services
        private static CoverageExtensionPackage _instance;

        /// <summary>
        /// Gets the tool window
        /// </summary>
        public static CoverageToolWindow GetToolWindow()
        {
            if (_instance == null)
                return null;

            ThreadHelper.ThrowIfNotOnUIThread();

            // Find the tool window
            IVsWindowFrame windowFrame = null;

            try
            {
                windowFrame = _instance.FindToolWindow(typeof(CoverageToolWindow), 0, false)?.Frame as IVsWindowFrame;
            }
            catch
            {
                // Window might not be created yet
                return null;
            }

            if (windowFrame == null)
                return null;

            CoverageToolWindow window = null;

            // Get the window from the frame
            object obj = null;
            windowFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocView, out obj);
            window = obj as CoverageToolWindow;

            return window;
        }

        /// <summary>
        /// Gets the component model
        /// </summary>
        public static IComponentModel GetComponentModel()
        {
            if (_instance == null)
                return null;

            return Package.GetGlobalService(typeof(SComponentModel)) as IComponentModel;
        }

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
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            // Store the instance
            _instance = this;

            // Generate icon file if needed
            try
            {
                string iconPath = Path.Combine(Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location), "Resources", "CodeCoverageIcon.png");
                if (!File.Exists(iconPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
                    Resources.CodeCoverageIcon.SaveIconToFile(iconPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating icon: {ex.Message}");
            }

            // Get the DTE service
            dte = await GetServiceAsync(typeof(DTE)) as DTE2;

            // Create managers
            adornmentManager = new CoverageAdornmentManager();

            // Create event trackers
            testEventTracker = new TestEventTracker(dte);

            // Register command
            await RegisterCommandsAsync();

            // Hook up test events - do this after command registration
            testEventTracker.TestRunCompleted += TestEventTracker_TestRunCompleted;

            // Create the coverage runner - this depends on commands being registered first
            // so the tool window can be found
            coverageRunner = new CoverageAnalysisRunner(dte, adornmentManager);
        }

        /// <summary>
        /// Handles test run completion
        /// </summary>
        private void TestEventTracker_TestRunCompleted(object sender, EventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // Show the tool window
                    await ShowToolWindowAsync(typeof(CoverageToolWindow), 0, true, CancellationToken.None);

                    // Run coverage analysis
                    await coverageRunner.RunCoverageAnalysisAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error processing test completion: {ex.Message}");
                }
            });
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
            var toolsMenuCommandID = new CommandID(CommandSet, CommandIdToolsMenu);
            var otherWindowsMenuCommandID = new CommandID(CommandSet, CommandIdOtherWindowsMenu);
            var testMenuCommandID = new CommandID(CommandSet, CommandIdTestMenu);

            // Create the menu commands
            var toolsMenuCommand = new MenuCommand(ShowToolWindowHandler, toolsMenuCommandID);
            var otherWindowsMenuCommand = new MenuCommand(ShowToolWindowHandler, otherWindowsMenuCommandID);
            var testMenuCommand = new MenuCommand(ShowToolWindowHandler, testMenuCommandID);

            // Add commands to the service
            commandService.AddCommand(toolsMenuCommand);
            commandService.AddCommand(otherWindowsMenuCommand);
            commandService.AddCommand(testMenuCommand);
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

            // Get the control
            var coverageControl = (window as CoverageToolWindow)?.Content as CoverageToolWindowControl;
            if (coverageControl != null)
            {
                coverageControl.RefreshCoverage();
            }
        }
    }

    /// <summary>
    /// Tracks test run events from Visual Studio
    /// </summary>
    internal class TestEventTracker
    {
        private DTE2 dte;
        private Events events;
        private DTEEvents dteEvents;

        public event EventHandler TestRunCompleted;

        public TestEventTracker(DTE2 dte)
        {
            this.dte = dte;

            if (dte != null)
            {
                try
                {
                    events = dte.Events;
                    dteEvents = events.DTEEvents;

                    // Listen for Command events which can indicate test runs
                    dteEvents.CommandExecuted += DTEEvents_CommandExecuted;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error setting up DTE events: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handle command executions
        /// </summary>
        private void DTEEvents_CommandExecuted(string guid, int id, object customIn, object customOut)
        {
            // Test run commands
            if (guid == "{1E198C22-5980-4E7E-92F3-F73168D1FB63}" && (id == 16 || id == 3)) // Run tests and Run All tests
            {
                // Wait a bit for tests to complete before notifying
                Task.Delay(1000).ContinueWith(_ =>
                {
                    TestRunCompleted?.Invoke(this, EventArgs.Empty);
                });
            }
        }
    }

    /// <summary>
    /// Runs code coverage analysis
    /// </summary>
    internal class CoverageAnalysisRunner
    {
        private DTE2 dte;
        private CoverageAdornmentManager adornmentManager;

        public CoverageAnalysisRunner(DTE2 dte, CoverageAdornmentManager adornmentManager)
        {
            this.dte = dte;
            this.adornmentManager = adornmentManager;
        }

        /// <summary>
        /// Runs code coverage analysis
        /// </summary>
        public async Task RunCoverageAnalysisAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (dte == null)
                return;

            try
            {
                // Get solution directory
                var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                if (string.IsNullOrEmpty(solutionDir))
                    return;

                // Find test projects
                var testProjects = FindTestProjects();
                if (testProjects.Count == 0)
                    return;

                // Get the output window
                OutputWindow outputWindow = dte.ToolWindows.OutputWindow;
                OutputWindowPane outputPane = EnsureOutputPane(outputWindow, "Code Coverage");
                outputPane.Activate();
                outputPane.Clear();
                outputPane.OutputString("Running code coverage analysis...\n");

                // Run coverage for each test project
                foreach (var project in testProjects)
                {
                    try
                    {
                        string projectPath = project.FullName;
                        string projectDir = Path.GetDirectoryName(projectPath);

                        outputPane.OutputString($"Analyzing coverage for {project.Name}...\n");

                        // Run dotnet test with coverage collection
                        await RunDotNetTestWithCoverageAsync(projectDir, outputPane);
                    }
                    catch (Exception ex)
                    {
                        outputPane.OutputString($"Error analyzing {project.Name}: {ex.Message}\n");
                    }
                }

                // Refresh the adornment manager
                adornmentManager.ActivateCoverage();

                outputPane.OutputString("Code coverage analysis completed.\n");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error running coverage analysis: {ex.Message}");
            }
        }

        /// <summary>
        /// Finds test projects in the solution
        /// </summary>
        private List<Project> FindTestProjects()
        {
            var testProjects = new List<Project>();

            try
            {
                foreach (Project project in dte.Solution.Projects)
                {
                    if (IsTestProject(project))
                    {
                        testProjects.Add(project);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding test projects: {ex.Message}");
            }

            return testProjects;
        }

        /// <summary>
        /// Checks if a project is a test project
        /// </summary>
        private bool IsTestProject(Project project)
        {
            try
            {
                string projectName = project.Name.ToLowerInvariant();
                if (projectName.Contains("test") || projectName.Contains("tests") || projectName.EndsWith(".tests"))
                    return true;

                // Check references in the project file
                string projectPath = project.FullName;
                if (File.Exists(projectPath))
                {
                    string content = File.ReadAllText(projectPath);
                    if (content.Contains("MSTest.TestAdapter") ||
                        content.Contains("MSTest.TestFramework") ||
                        content.Contains("Microsoft.NET.Test.Sdk"))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // If we can't determine, return false
                return false;
            }

            return false;
        }

        /// <summary>
        /// Runs dotnet test with coverage collection
        /// </summary>
        private async Task RunDotNetTestWithCoverageAsync(string projectDir, OutputWindowPane outputPane)
        {
            try
            {
                // Create process to run dotnet test
                var startInfo = new ProcessStartInfo
                {
                    FileName = "dotnet",
                    Arguments = "test --collect:\"XPlat Code Coverage\"",
                    WorkingDirectory = projectDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                var process = new Process { StartInfo = startInfo };

                // Create task completion source to wait for process to exit
                var processExited = new TaskCompletionSource<bool>();

                // Output data handlers
                process.OutputDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputPane.OutputString($"{e.Data}\n");
                    }
                };

                process.ErrorDataReceived += (sender, e) =>
                {
                    if (e.Data != null)
                    {
                        outputPane.OutputString($"ERROR: {e.Data}\n");
                    }
                };

                // Process exit handler
                process.Exited += (sender, e) => processExited.SetResult(true);
                process.EnableRaisingEvents = true;

                // Start process
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Wait for process to exit, timeout after 2 minutes
                var exited = await Task.WhenAny(processExited.Task, Task.Delay(TimeSpan.FromMinutes(2)));

                // If it didn't exit, kill it
                if (exited != processExited.Task)
                {
                    try
                    {
                        process.Kill();
                        outputPane.OutputString("Test run timed out after 2 minutes\n");
                    }
                    catch
                    {
                        // Process may have exited by now
                    }
                }

                // Dispose process
                process.Dispose();
            }
            catch (Exception ex)
            {
                outputPane.OutputString($"Error running dotnet test: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Ensures an output pane exists
        /// </summary>
        private OutputWindowPane EnsureOutputPane(OutputWindow outputWindow, string name)
        {
            try
            {
                // Try to find existing pane
                foreach (OutputWindowPane pane in outputWindow.OutputWindowPanes)
                {
                    if (pane.Name == name)
                    {
                        return pane;
                    }
                }

                // Create a new pane
                return outputWindow.OutputWindowPanes.Add(name);
            }
            catch
            {
                // If we can't create a pane, use the general pane
                return outputWindow.OutputWindowPanes.Item("General");
            }
        }
    }
}