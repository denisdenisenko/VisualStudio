using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using EnvDTE;
using System.Linq;
using System.Xml.Linq;
using System.Diagnostics;

namespace DD_Coverage
{
    public class CoverageService
    {
        private readonly IServiceProvider serviceProvider;

        public CoverageService(IServiceProvider serviceProvider)
        {
            this.serviceProvider = serviceProvider;
        }

        public async Task<CoverageResult> AnalyzeCoverageAsync()
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Get DTE service to access solution information
                var dte = serviceProvider.GetService(typeof(DTE)) as DTE;
                if (dte == null)
                {
                    return new CoverageResult { Success = false, Message = "Visual Studio DTE service not available" };
                }

                // Check if solution is available
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    return new CoverageResult { Success = false, Message = "No solution is currently open" };
                }

                // Get solution directory
                var solutionDir = Path.GetDirectoryName(dte.Solution.FullName);

                // Find and parse coverage reports
                var coverageData = await FindAndParseCoverageReportsAsync(solutionDir);

                if (coverageData.Count == 0)
                {
                    return new CoverageResult
                    {
                        Success = true,
                        Message = "No MSTest coverage data found. Run your MSTest tests with coverage collection enabled.",
                        CoverageData = new Dictionary<string, double>()
                    };
                }

                return new CoverageResult
                {
                    Success = true,
                    Message = $"Found MSTest coverage data for {coverageData.Count} projects",
                    CoverageData = coverageData
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in coverage analysis: {ex.Message}");
                return new CoverageResult
                {
                    Success = false,
                    Message = $"Error analyzing coverage: {ex.Message}",
                    CoverageData = new Dictionary<string, double>()
                };
            }
        }

        private async Task<Dictionary<string, double>> FindAndParseCoverageReportsAsync(string solutionDir)
        {
            var coverageData = new Dictionary<string, double>();

            try
            {
                // Look for coverage files in the solution directory and subdirectories
                var coberturaFiles = Directory.GetFiles(solutionDir, "coverage.cobertura.xml", SearchOption.AllDirectories);
                var openCoverFiles = Directory.GetFiles(solutionDir, "coverage.opencover.xml", SearchOption.AllDirectories);

                // Filter for MSTest projects only
                var msTestProjects = FindMSTestProjects(solutionDir);
                Debug.WriteLine($"Found {msTestProjects.Count} MSTest projects");

                // Process Cobertura format files (Coverlet default)
                foreach (var file in coberturaFiles)
                {
                    try
                    {
                        var (projectName, coverage) = ParseCoberturaReport(file);
                        if (!string.IsNullOrEmpty(projectName) && IsMSTestProject(file, msTestProjects))
                        {
                            coverageData[projectName] = coverage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing {file}: {ex.Message}");
                    }
                }

                // Process OpenCover format files
                foreach (var file in openCoverFiles)
                {
                    try
                    {
                        var (projectName, coverage) = ParseOpenCoverReport(file);
                        if (!string.IsNullOrEmpty(projectName) && IsMSTestProject(file, msTestProjects))
                        {
                            coverageData[projectName] = coverage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error parsing {file}: {ex.Message}");
                    }
                }

                // If no coverage files found, check TestResults directories for any XML files that might be coverage reports
                if (coverageData.Count == 0)
                {
                    var testResultsDirs = Directory.GetDirectories(solutionDir, "TestResults", SearchOption.AllDirectories);
                    foreach (var dir in testResultsDirs)
                    {
                        var xmlFiles = Directory.GetFiles(dir, "*.xml", SearchOption.AllDirectories);
                        foreach (var file in xmlFiles)
                        {
                            try
                            {
                                // Try to detect file format and parse
                                if (File.ReadAllText(file).Contains("<coverage") && IsMSTestProject(file, msTestProjects))
                                {
                                    var (projectName, coverage) = ParseCoberturaReport(file);
                                    if (!string.IsNullOrEmpty(projectName))
                                    {
                                        coverageData[projectName] = coverage;
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }

                // If we still don't have coverage data, try to look for Visual Studio's coverage files (.coverage)
                if (coverageData.Count == 0)
                {
                    var vsCoverageFiles = Directory.GetFiles(solutionDir, "*.coverage", SearchOption.AllDirectories);
                    if (vsCoverageFiles.Length > 0)
                    {
                        return new Dictionary<string, double>
                        {
                            { "Visual Studio Coverage", 0.0 },
                            { "Note: Visual Studio coverage format (.coverage) requires special processing", 0.0 },
                            { "Run coverlet with --collect:\"XPlat Code Coverage\" for better support", 0.0 }
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding coverage files: {ex.Message}");
            }

            if (coverageData.Count == 0)
            {
                // Return instructions if no coverage data found
                return new Dictionary<string, double>
                {
                    { "No MSTest coverage data found", 0.0 },
                    { "Run MSTest tests with coverage: dotnet test --filter \"FullyQualifiedName~MSTest\" --collect:\"XPlat Code Coverage\"", 0.0 }
                };
            }

            return coverageData;
        }

        private (string projectName, double coverage) ParseCoberturaReport(string filePath)
        {
            try
            {
                XDocument doc = XDocument.Load(filePath);
                XElement root = doc.Root;

                if (root.Name.LocalName != "coverage")
                {
                    return (null, 0.0);
                }

                // Extract line coverage
                var lineRate = root.Attribute("line-rate");
                double lineCoverage = 0.0;
                if (lineRate != null && double.TryParse(lineRate.Value, out double rate))
                {
                    lineCoverage = rate * 100.0; // Convert to percentage
                }

                // Try to get the project name from the file path
                string projectName = "Unknown";

                // Try to determine project name from the assembly
                var firstPackage = root.Elements("packages").Elements("package").FirstOrDefault();
                if (firstPackage != null)
                {
                    var packageName = firstPackage.Attribute("name")?.Value;
                    if (!string.IsNullOrEmpty(packageName))
                    {
                        projectName = packageName;
                    }
                }

                // If we can't get it from the XML, try from the path
                if (projectName == "Unknown")
                {
                    try
                    {
                        DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(filePath));
                        // Go up a few directories until we find one that might be a project directory
                        while (dir != null && !File.Exists(Path.Combine(dir.FullName, dir.Name + ".csproj")))
                        {
                            dir = dir.Parent;
                        }

                        if (dir != null)
                        {
                            projectName = dir.Name;
                        }
                    }
                    catch { }
                }

                return (projectName, lineCoverage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing Cobertura report: {ex.Message}");
                return (null, 0.0);
            }
        }

        private (string projectName, double coverage) ParseOpenCoverReport(string filePath)
        {
            try
            {
                XDocument doc = XDocument.Load(filePath);
                XElement root = doc.Root;

                if (root.Name.LocalName != "CoverageSession")
                {
                    return (null, 0.0);
                }

                // Extract coverage metrics
                var summary = root.Element("Summary");
                if (summary == null)
                {
                    return (null, 0.0);
                }

                double sequenceCoverage = 0.0;
                var sequencePoints = summary.Attribute("sequencePoints");
                var visitedSequencePoints = summary.Attribute("visitedSequencePoints");

                if (sequencePoints != null && visitedSequencePoints != null)
                {
                    if (int.TryParse(sequencePoints.Value, out int total) &&
                        int.TryParse(visitedSequencePoints.Value, out int visited) &&
                        total > 0)
                    {
                        sequenceCoverage = (visited / (double)total) * 100.0;
                    }
                }

                // Try to get module/project name
                string projectName = "Unknown";
                var module = root.Elements("Modules").Elements("Module").FirstOrDefault();
                if (module != null)
                {
                    var moduleName = module.Attribute("moduleId")?.Value ??
                                    module.Attribute("name")?.Value ??
                                    module.Attribute("module")?.Value;
                    if (!string.IsNullOrEmpty(moduleName))
                    {
                        projectName = Path.GetFileNameWithoutExtension(moduleName);
                    }
                }

                return (projectName, sequenceCoverage);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error parsing OpenCover report: {ex.Message}");
                return (null, 0.0);
            }
        }

        // Helper method to find MSTest projects
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
                        if (content.Contains("MSTest.TestAdapter") && content.Contains("MSTest.TestFramework"))
                        {
                            msTestProjects.Add(csprojFile);
                            var projectDir = Path.GetDirectoryName(csprojFile);
                            msTestProjects.Add(projectDir);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error checking project file {csprojFile}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding MSTest projects: {ex.Message}");
            }
            return msTestProjects;
        }

        // Helper method to check if a coverage file is from an MSTest project
        private bool IsMSTestProject(string coverageFile, List<string> msTestProjects)
        {
            try
            {
                var coverageDir = Path.GetDirectoryName(coverageFile);

                // Check if the coverage file is in or under an MSTest project directory
                return msTestProjects.Any(msTestProject =>
                    coverageDir.StartsWith(msTestProject, StringComparison.OrdinalIgnoreCase) ||
                    coverageFile.Contains("MSTest") ||
                    coverageDir.Contains("MSTest"));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error checking if {coverageFile} is MSTest project: {ex.Message}");
                return false;
            }
        }
    }

    public class CoverageResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public Dictionary<string, double> CoverageData { get; set; } = new Dictionary<string, double>();
    }
}