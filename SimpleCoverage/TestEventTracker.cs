using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Timers;

namespace SimpleCoverage
{
    /// <summary>
    /// Tracks test execution events in Visual Studio
    /// </summary>
    public class TestEventTracker : IDisposable
    {
        private readonly DTE2 _dte;
        private readonly Timer _testRunTimer;
        private bool _testRunInProgress = false;
        private readonly HashSet<Process> _testRunnerProcesses = new HashSet<Process>();

        /// <summary>
        /// Event that fires when a test run completes
        /// </summary>
        public event EventHandler TestRunCompleted;

        /// <summary>
        /// Initializes a new test event tracker
        /// </summary>
        /// <param name="dte">The DTE service</param>
        public TestEventTracker(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            _dte = dte;

            // Set up a timer to check for test execution
            _testRunTimer = new Timer(1000);
            _testRunTimer.Elapsed += CheckTestRunStatus;
            _testRunTimer.Start();

            // Hook into solution events
            _dte.Events.SolutionEvents.ProjectAdded += SolutionEvents_ProjectChanged;
            _dte.Events.SolutionEvents.ProjectRemoved += SolutionEvents_ProjectChanged;
            _dte.Events.SolutionEvents.Opened += SolutionEvents_Changed;
            _dte.Events.SolutionEvents.AfterClosing += SolutionEvents_Changed;
        }

        private void SolutionEvents_ProjectChanged(Project project)
        {
            // Reset state when project structure changes
            _testRunInProgress = false;
            _testRunnerProcesses.Clear();
        }

        private void SolutionEvents_Changed()
        {
            // Reset state when solution changes
            _testRunInProgress = false;
            _testRunnerProcesses.Clear();
        }

        private void CheckTestRunStatus(object sender, ElapsedEventArgs e)
        {
            try
            {
                ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    // Check if the Test Explorer window is open
                    bool testWindowOpen = false;
                    foreach (Window window in _dte.Windows)
                    {
                        if (window.Caption.Contains("Test Explorer"))
                        {
                            testWindowOpen = true;
                            break;
                        }
                    }

                    if (!testWindowOpen)
                    {
                        return;
                    }

                    // Look for test runner processes
                    Process[] processes = Process.GetProcesses();
                    bool foundTestRunner = false;

                    foreach (Process process in processes)
                    {
                        try
                        {
                            if (IsTestRunnerProcess(process))
                            {
                                _testRunnerProcesses.Add(process);
                                foundTestRunner = true;
                                _testRunInProgress = true;
                            }
                        }
                        catch
                        {
                            // Process might have exited or access denied, ignore
                        }
                    }

                    // If we had a test run in progress but can't find any test runners now,
                    // the test run has completed
                    if (_testRunInProgress && !foundTestRunner)
                    {
                        // Clean up any stale process references
                        _testRunnerProcesses.RemoveWhere(p => p.HasExited);

                        if (_testRunnerProcesses.Count == 0)
                        {
                            _testRunInProgress = false;
                            TestRunCompleted?.Invoke(this, EventArgs.Empty);
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking test run status: {ex.Message}");
            }
        }

        private bool IsTestRunnerProcess(Process process)
        {
            try
            {
                string name = process.ProcessName.ToLowerInvariant();
                return name.Contains("testhost") ||
                       name.Contains("vstest") ||
                       name.Contains("mstest") ||
                       name.Contains("xunit") ||
                       name.Contains("nunit");
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Disposes resources
        /// </summary>
        public void Dispose()
        {
            _testRunTimer.Stop();
            _testRunTimer.Dispose();
            _testRunnerProcesses.Clear();
        }
    }
}