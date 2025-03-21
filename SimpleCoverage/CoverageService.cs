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
    /// Information about a source file's coverage
    /// </summary>
    public class FileCoverageInfo
    {
        public string FilePath { get; set; }
        public Dictionary<int, bool> LinesCovered { get; set; } = new Dictionary<int, bool>();
        public double CoveragePercentage { get; set; }
        public int TotalLines { get; set; }
        public int CoveredLines { get; set; }
    }

    /// <summary>
    /// Service for analyzing code coverage data
    /// </summary>
    public class CoverageService
    {
        private Dictionary<string, FileCoverageInfo> fileCoverageData = new Dictionary<string, FileCoverageInfo>();
        private string currentCoverageFilePath;

        /// <summary>
        /// Gets code coverage data asynchronously
        /// </summary>
        public async Task<Dictionary<string, double>> GetCoverageDataAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var coverageData = new Dictionary<string, double>();
            fileCoverageData.Clear();

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
                                currentCoverageFilePath = file;
                                // Parse detailed file coverage information
                                ParseFileCoverageDetails(file);
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
                                            currentCoverageFilePath = file;
                                            // Parse detailed file coverage information
                                            ParseFileCoverageDetails(file);
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
        /// Gets coverage information for a specific file
        /// </summary>
        public FileCoverageInfo GetFileCoverage(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return null;

            string normalizedPath = NormalizeFilePath(filePath);

            if (fileCoverageData.TryGetValue(normalizedPath, out var coverageInfo))
                return coverageInfo;

            // Try to match by file name if full path doesn't match
            string fileName = Path.GetFileName(filePath);
            foreach (var item in fileCoverageData)
            {
                if (Path.GetFileName(item.Key) == fileName)
                    return item.Value;
            }

            return null;
        }

        /// <summary>
        /// Normalizes file path for consistent comparison
        /// </summary>
        private string NormalizeFilePath(string path)
        {
            return Path.GetFullPath(path).ToLowerInvariant();
        }

        /// <summary>
        /// Parses detailed file coverage information from the coverage file
        /// </summary>
        private void ParseFileCoverageDetails(string filePath)
        {
            try
            {
                var doc = XDocument.Load(filePath);
                var root = doc.Root;

                // Handle Cobertura format
                if (root.Name.LocalName == "coverage")
                {
                    ParseCoberturaCoverageDetails(root);
                }
                // Handle OpenCover format
                else if (root.Name.LocalName == "CoverageSession")
                {
                    ParseOpenCoverCoverageDetails(root);
                }
            }
            catch
            {
                // Skip on error
            }
        }

        /// <summary>
        /// Parses detailed coverage information from Cobertura format
        /// </summary>
        private void ParseCoberturaCoverageDetails(XElement root)
        {
            var packages = root.Element("packages");
            if (packages == null) return;

            foreach (var package in packages.Elements("package"))
            {
                var classes = package.Element("classes");
                if (classes == null) continue;

                foreach (var classElement in classes.Elements("class"))
                {
                    var fileName = classElement.Attribute("filename")?.Value;
                    if (string.IsNullOrEmpty(fileName)) continue;

                    string normalizedPath = NormalizeFilePath(fileName);
                    if (!fileCoverageData.TryGetValue(normalizedPath, out var coverageInfo))
                    {
                        coverageInfo = new FileCoverageInfo
                        {
                            FilePath = fileName
                        };
                        fileCoverageData[normalizedPath] = coverageInfo;
                    }

                    var lines = classElement.Element("lines");
                    if (lines == null) continue;

                    foreach (var line in lines.Elements("line"))
                    {
                        if (int.TryParse(line.Attribute("number")?.Value, out int lineNumber) &&
                            int.TryParse(line.Attribute("hits")?.Value, out int hits))
                        {
                            coverageInfo.LinesCovered[lineNumber] = hits > 0;
                        }
                    }

                    // Calculate coverage percentage
                    UpdateCoverageStats(coverageInfo);
                }
            }
        }

        /// <summary>
        /// Parses detailed coverage information from OpenCover format
        /// </summary>
        private void ParseOpenCoverCoverageDetails(XElement root)
        {
            var modules = root.Element("Modules");
            if (modules == null) return;

            foreach (var module in modules.Elements("Module"))
            {
                var files = module.Element("Files");
                if (files == null) continue;

                var classes = module.Element("Classes");
                if (classes == null) continue;

                // Create a mapping from file IDs to file paths
                var fileMap = new Dictionary<string, string>();
                foreach (var file in files.Elements("File"))
                {
                    var fileId = file.Attribute("uid")?.Value;
                    var filePath = file.Attribute("fullPath")?.Value;
                    if (!string.IsNullOrEmpty(fileId) && !string.IsNullOrEmpty(filePath))
                    {
                        fileMap[fileId] = filePath;
                    }
                }

                // Process classes and their methods
                foreach (var classElement in classes.Elements("Class"))
                {
                    var methods = classElement.Element("Methods");
                    if (methods == null) continue;

                    foreach (var method in methods.Elements("Method"))
                    {
                        var sequencePoints = method.Element("SequencePoints");
                        if (sequencePoints == null) continue;

                        // Process each sequence point (line of code)
                        foreach (var sp in sequencePoints.Elements("SequencePoint"))
                        {
                            var fileId = sp.Attribute("fileid")?.Value;
                            if (string.IsNullOrEmpty(fileId) || !fileMap.TryGetValue(fileId, out string filePath))
                                continue;

                            string normalizedPath = NormalizeFilePath(filePath);
                            if (!fileCoverageData.TryGetValue(normalizedPath, out var coverageInfo))
                            {
                                coverageInfo = new FileCoverageInfo
                                {
                                    FilePath = filePath
                                };
                                fileCoverageData[normalizedPath] = coverageInfo;
                            }

                            if (int.TryParse(sp.Attribute("sl")?.Value, out int lineNumber) &&
                                int.TryParse(sp.Attribute("vc")?.Value, out int visitCount))
                            {
                                coverageInfo.LinesCovered[lineNumber] = visitCount > 0;
                            }
                        }
                    }
                }

                // Calculate coverage percentages for all files
                foreach (var coverageInfo in fileCoverageData.Values)
                {
                    UpdateCoverageStats(coverageInfo);
                }
            }
        }

        /// <summary>
        /// Updates coverage statistics for a file
        /// </summary>
        private void UpdateCoverageStats(FileCoverageInfo coverageInfo)
        {
            int total = coverageInfo.LinesCovered.Count;
            int covered = coverageInfo.LinesCovered.Count(l => l.Value);

            coverageInfo.TotalLines = total;
            coverageInfo.CoveredLines = covered;
            coverageInfo.CoveragePercentage = total > 0 ? (covered / (double)total) * 100.0 : 0.0;
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