using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;

namespace SimpleCoverage
{
    /// <summary>
    /// Service for analyzing code coverage data
    /// </summary>
    public class CoverageService
    {
        /// <summary>
        /// Gets code coverage data asynchronously
        /// </summary>
        public async Task<Dictionary<string, double>> GetCoverageDataAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var coverageData = new Dictionary<string, double>();

            try
            {
                // Get the DTE service
                var dte = Package.GetGlobalService(typeof(SDTE)) as DTE2;
                if (dte == null)
                {
                    return coverageData; // Return empty results if DTE is not available
                }

                // Get solution directory
                var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                if (string.IsNullOrEmpty(solutionDir))
                {
                    return coverageData;
                }

                // Find MSTest projects
                var msTestProjects = FindMSTestProjects(solutionDir);

                // Look for coverage files
                var coverageFiles = new List<string>();
                coverageFiles.AddRange(Directory.GetFiles(solutionDir, "coverage.cobertura.xml", SearchOption.AllDirectories));
                coverageFiles.AddRange(Directory.GetFiles(solutionDir, "coverage.opencover.xml", SearchOption.AllDirectories));

                // Process coverage files
                foreach (var file in coverageFiles)
                {
                    try
                    {
                        if (IsMSTestCoverageFile(file, msTestProjects))
                        {
                            var (projectName, coverage) = ParseCoverageFile(file);
                            if (!string.IsNullOrEmpty(projectName) && !coverageData.ContainsKey(projectName))
                            {
                                coverageData[projectName] = coverage;
                            }
                        }
                    }
                    catch (Exception)
                    {
                        // Skip problematic files
                    }
                }

                // If we haven't found any coverage files, look in TestResults directories
                if (coverageData.Count == 0)
                {
                    var testResults = Directory.GetDirectories(solutionDir, "TestResults", SearchOption.AllDirectories);
                    foreach (var resultDir in testResults)
                    {
                        try
                        {
                            var xmlFiles = Directory.GetFiles(resultDir, "*.xml", SearchOption.AllDirectories);
                            foreach (var file in xmlFiles)
                            {
                                try
                                {
                                    if (File.ReadAllText(file).Contains("<coverage") &&
                                        IsMSTestCoverageFile(file, msTestProjects))
                                    {
                                        var (projectName, coverage) = ParseCoverageFile(file);
                                        if (!string.IsNullOrEmpty(projectName) && !coverageData.ContainsKey(projectName))
                                        {
                                            coverageData[projectName] = coverage;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Skip problematic files
                                }
                            }
                        }
                        catch
                        {
                            // Skip problematic directories
                        }
                    }
                }
            }
            catch
            {
                // Return empty results on error
            }

            return coverageData;
        }

        /// <summary>
        /// Parses a coverage file to extract project name and coverage percentage
        /// </summary>
        private (string projectName, double coverage) ParseCoverageFile(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;

                // Default values
                string projectName = "Unknown";
                double coverage = 0.0;

                // Handle Cobertura format
                if (root.Name.LocalName == "coverage")
                {
                    // Get line rate
                    var lineRate = root.Attribute("line-rate");
                    if (lineRate != null && double.TryParse(lineRate.Value, out double rate))
                    {
                        coverage = rate * 100.0;
                    }

                    // Get project name
                    var firstPackage = root.Elements("packages").Elements("package").FirstOrDefault();
                    if (firstPackage != null)
                    {
                        var packageName = firstPackage.Attribute("name")?.Value;
                        if (!string.IsNullOrEmpty(packageName))
                        {
                            projectName = packageName;
                        }
                    }
                }
                // Handle OpenCover format
                else if (root.Name.LocalName == "CoverageSession")
                {
                    var summary = root.Element("Summary");
                    if (summary != null)
                    {
                        var sequencePoints = summary.Attribute("sequencePoints");
                        var visitedSequencePoints = summary.Attribute("visitedSequencePoints");

                        if (sequencePoints != null && visitedSequencePoints != null)
                        {
                            if (int.TryParse(sequencePoints.Value, out int total) &&
                                int.TryParse(visitedSequencePoints.Value, out int visited) &&
                                total > 0)
                            {
                                coverage = (visited / (double)total) * 100.0;
                            }
                        }
                    }

                    // Get module name
                    var module = root.Elements("Modules").Elements("Module").FirstOrDefault();
                    if (module != null)
                    {
                        var moduleName = module.Attribute("moduleId")?.Value ??
                                        module.Attribute("name")?.Value;
                        if (!string.IsNullOrEmpty(moduleName))
                        {
                            projectName = Path.GetFileNameWithoutExtension(moduleName);
                        }
                    }
                }

                return (projectName, coverage);
            }
            catch
            {
                return (null, 0.0);
            }
        }

        /// <summary>
        /// Finds MSTest projects in the solution directory
        /// </summary>
        private List<string> FindMSTestProjects(string solutionDir)
        {
            var msTestProjects = new List<string>();

            try
            {
                var csprojFiles = Directory.GetFiles(solutionDir, "*.csproj", SearchOption.AllDirectories);
                foreach (var csprojFile in csprojFiles)
                {
                    try
                    {
                        var content = File.ReadAllText(csprojFile);
                        if (content.Contains("MSTest.TestAdapter") &&
                            content.Contains("MSTest.TestFramework"))
                        {
                            msTestProjects.Add(csprojFile);
                            msTestProjects.Add(Path.GetDirectoryName(csprojFile));
                        }
                    }
                    catch
                    {
                        // Skip problematic files
                    }
                }
            }
            catch
            {
                // Return empty list on error
            }

            return msTestProjects;
        }

        /// <summary>
        /// Checks if a coverage file belongs to an MSTest project
        /// </summary>
        private bool IsMSTestCoverageFile(string coverageFile, List<string> msTestProjects)
        {
            try
            {
                var coverageDir = Path.GetDirectoryName(coverageFile);

                // Check if the file or its path contains MSTest indicators
                if (coverageFile.Contains("MSTest") || coverageDir.Contains("MSTest"))
                {
                    return true;
                }

                // Check if the file is under an MSTest project directory
                return msTestProjects.Any(proj =>
                    coverageDir.StartsWith(proj, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
    }
}