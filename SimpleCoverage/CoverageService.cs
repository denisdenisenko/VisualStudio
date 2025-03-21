using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Text.Editor;

namespace SimpleCoverage
{
    /// <summary>
    /// Coverage status of a line of code
    /// </summary>
    public enum LineCoverageStatus
    {
        /// <summary>
        /// Line is not covered by any tests
        /// </summary>
        NotCovered,

        /// <summary>
        /// Line is partially covered by tests
        /// </summary>
        PartiallyCovered,

        /// <summary>
        /// Line is fully covered by tests
        /// </summary>
        Covered
    }

    /// <summary>
    /// Information about code coverage for a file
    /// </summary>
    public class FileCoverageInfo
    {
        /// <summary>
        /// Gets or sets the file path
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// Gets or sets the number of covered lines
        /// </summary>
        public int CoveredLines { get; set; }

        /// <summary>
        /// Gets or sets the number of coverable lines
        /// </summary>
        public int CoverableLines { get; set; }

        /// <summary>
        /// Gets the coverage percentage
        /// </summary>
        public double CoveragePercentage
        {
            get
            {
                if (CoverableLines == 0)
                    return 0;

                return (double)CoveredLines / CoverableLines * 100;
            }
        }

        /// <summary>
        /// Gets or sets line coverage information (line number to coverage status)
        /// </summary>
        public Dictionary<int, LineCoverageStatus> LineCoverage { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FileCoverageInfo"/> class
        /// </summary>
        public FileCoverageInfo()
        {
            LineCoverage = new Dictionary<int, LineCoverageStatus>();
        }
    }

    /// <summary>
    /// Service for analyzing code coverage data
    /// </summary>
    public class CoverageService
    {
        private Dictionary<string, FileCoverageInfo> fileCoverageData = new Dictionary<string, FileCoverageInfo>();
        private string currentCoverageFilePath;
        private const string CoberturaFormat = "Cobertura";
        private const string OpenCoverFormat = "OpenCover";
        private const string VisualStudioFormat = "VisualStudio";

        /// <summary>
        /// Gets the coverage file path
        /// </summary>
        public string CoverageFilePath => currentCoverageFilePath;

        /// <summary>
        /// Gets the coverage data
        /// </summary>
        public Dictionary<string, FileCoverageInfo> FileCoverageData => fileCoverageData;

        /// <summary>
        /// Event that fires when coverage data is updated
        /// </summary>
        public event EventHandler CoverageDataUpdated;

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

        /// <summary>
        /// Loads coverage data from files in the solution directory
        /// </summary>
        /// <returns>Dictionary of file paths to coverage information</returns>
        public async Task<Dictionary<string, FileCoverageInfo>> LoadCoverageDataAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var result = new Dictionary<string, FileCoverageInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                // Get the solution directory
                var dte = Package.GetGlobalService(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte == null || dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                    return result;

                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName);

                // Find all coverage files
                var coverageFiles = FindCoverageFiles(solutionDir);

                if (coverageFiles.Length == 0)
                    return result;

                // Get the most recent file (typically what we want)
                string mostRecentFile = coverageFiles.OrderByDescending(f => File.GetLastWriteTime(f)).First();
                string format = DetermineCoverageFormat(mostRecentFile);

                // Parse coverage file
                Dictionary<string, FileCoverageInfo> coverageData = null;

                if (format == CoberturaFormat)
                    coverageData = ParseCoberturaFile(mostRecentFile);
                else if (format == OpenCoverFormat)
                    coverageData = ParseOpenCoverFile(mostRecentFile);
                else if (format == VisualStudioFormat)
                    coverageData = ParseVisualStudioCoverageFile(mostRecentFile);

                if (coverageData != null)
                {
                    foreach (var item in coverageData)
                    {
                        // Normalize paths
                        string normalizedPath = item.Key.Replace("/", "\\").ToLowerInvariant();

                        // Only include source files of the current solution
                        if (normalizedPath.StartsWith(solutionDir.ToLowerInvariant()) &&
                            File.Exists(normalizedPath))
                        {
                            result[normalizedPath] = item.Value;
                        }
                    }
                }

                // Update internal state
                fileCoverageData = result;
                currentCoverageFilePath = mostRecentFile;

                // Notify listeners
                CoverageDataUpdated?.Invoke(this, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error loading coverage data: {ex.Message}");
            }

            return result;
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

        /// <summary>
        /// Determines the format of a coverage file
        /// </summary>
        private string DetermineCoverageFormat(string filePath)
        {
            try
            {
                if (filePath.EndsWith(".coverage", StringComparison.OrdinalIgnoreCase))
                    return VisualStudioFormat;

                using (var reader = new StreamReader(filePath))
                {
                    // Read the first few lines to determine format
                    string header = reader.ReadLine();

                    if (header != null)
                    {
                        if (header.Contains("<coverage") && header.Contains("clover.dtd"))
                            return CoberturaFormat;

                        if (header.Contains("<CoverageSession"))
                            return OpenCoverFormat;
                    }
                }

                // Try to parse XML to determine format
                XDocument doc = XDocument.Load(filePath);
                XElement root = doc.Root;

                if (root != null)
                {
                    if (root.Name == "coverage")
                        return CoberturaFormat;

                    if (root.Name == "CoverageSession")
                        return OpenCoverFormat;
                }
            }
            catch
            {
                // Ignore parsing errors
            }

            // Default to Cobertura as it's most common
            return CoberturaFormat;
        }

        /// <summary>
        /// Parses a Cobertura XML file
        /// </summary>
        private Dictionary<string, FileCoverageInfo> ParseCoberturaFile(string filePath)
        {
            var result = new Dictionary<string, FileCoverageInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                XDocument doc = XDocument.Load(filePath);

                // Get all classes (files)
                var classElements = doc.Descendants("class");

                foreach (var classElement in classElements)
                {
                    string filename = classElement.Attribute("filename")?.Value;

                    if (string.IsNullOrEmpty(filename))
                        continue;

                    // Create file coverage info
                    var fileCoverage = new FileCoverageInfo
                    {
                        FilePath = filename
                    };

                    // Get all lines
                    var lineElements = classElement.Descendants("line");

                    foreach (var lineElement in lineElements)
                    {
                        // Get line info
                        int lineNumber;
                        if (!int.TryParse(lineElement.Attribute("number")?.Value, out lineNumber))
                            continue;

                        int hits;
                        if (!int.TryParse(lineElement.Attribute("hits")?.Value, out hits))
                            continue;

                        // Set coverage status
                        LineCoverageStatus status = hits > 0
                            ? LineCoverageStatus.Covered
                            : LineCoverageStatus.NotCovered;

                        // Check for branch coverage
                        bool hasBranch = false;
                        int coveredBranches = 0;

                        if (lineElement.Attribute("branch")?.Value == "true")
                        {
                            hasBranch = true;

                            // Parse branch coverage
                            int.TryParse(lineElement.Attribute("condition-coverage")?.Value?.Split('(')[1]?.Split('/')[0], out coveredBranches);
                            int.TryParse(lineElement.Attribute("condition-coverage")?.Value?.Split('/')[1]?.Split(')')[0], out int totalBranches);

                            if (totalBranches > 0 && coveredBranches < totalBranches && coveredBranches > 0)
                                status = LineCoverageStatus.PartiallyCovered;
                        }

                        // Add to line coverage
                        fileCoverage.LineCoverage[lineNumber] = status;

                        // Update counters
                        fileCoverage.CoverableLines++;

                        if (status == LineCoverageStatus.Covered || status == LineCoverageStatus.PartiallyCovered)
                            fileCoverage.CoveredLines++;
                    }

                    // Add to result
                    if (fileCoverage.CoverableLines > 0)
                        result[filename] = fileCoverage;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing Cobertura file: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Parses an OpenCover XML file
        /// </summary>
        private Dictionary<string, FileCoverageInfo> ParseOpenCoverFile(string filePath)
        {
            var result = new Dictionary<string, FileCoverageInfo>(StringComparer.OrdinalIgnoreCase);

            try
            {
                XDocument doc = XDocument.Load(filePath);

                // Get all modules
                var moduleElements = doc.Descendants("Module");

                foreach (var moduleElement in moduleElements)
                {
                    // Get all files
                    var fileElements = moduleElement.Descendants("File");
                    var files = new Dictionary<int, string>();

                    foreach (var fileElement in fileElements)
                    {
                        int fileId;
                        if (!int.TryParse(fileElement.Attribute("uid")?.Value, out fileId))
                            continue;

                        string fullPath = fileElement.Attribute("fullPath")?.Value;
                        if (string.IsNullOrEmpty(fullPath))
                            continue;

                        files[fileId] = fullPath;
                    }

                    // Get all methods
                    var methodElements = moduleElement.Descendants("Method");

                    foreach (var methodElement in methodElements)
                    {
                        // Get sequences
                        var sequencePoints = methodElement.Descendants("SequencePoint");

                        foreach (var sequencePoint in sequencePoints)
                        {
                            int fileId;
                            if (!int.TryParse(sequencePoint.Attribute("fileid")?.Value, out fileId))
                                continue;

                            string fullPath;
                            if (!files.TryGetValue(fileId, out fullPath))
                                continue;

                            // Create file coverage info if needed
                            if (!result.TryGetValue(fullPath, out var fileCoverage))
                            {
                                fileCoverage = new FileCoverageInfo
                                {
                                    FilePath = fullPath
                                };
                                result[fullPath] = fileCoverage;
                            }

                            // Get line info
                            int lineNumber;
                            if (!int.TryParse(sequencePoint.Attribute("sl")?.Value, out lineNumber))
                                continue;

                            int visitCount;
                            if (!int.TryParse(sequencePoint.Attribute("vc")?.Value, out visitCount))
                                continue;

                            // Set coverage status
                            LineCoverageStatus status = visitCount > 0
                                ? LineCoverageStatus.Covered
                                : LineCoverageStatus.NotCovered;

                            // Check for branch coverage
                            if (int.TryParse(sequencePoint.Attribute("offsetchain")?.Value, out int offsetChain) && offsetChain > 0)
                            {
                                status = LineCoverageStatus.PartiallyCovered;
                            }

                            // Add to line coverage
                            fileCoverage.LineCoverage[lineNumber] = status;

                            // Update counters
                            fileCoverage.CoverableLines++;

                            if (status == LineCoverageStatus.Covered || status == LineCoverageStatus.PartiallyCovered)
                                fileCoverage.CoveredLines++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error parsing OpenCover file: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Parses a Visual Studio coverage file (.coverage)
        /// </summary>
        private Dictionary<string, FileCoverageInfo> ParseVisualStudioCoverageFile(string filePath)
        {
            // Visual Studio coverage files require specialized libraries to read
            // For now, we'll return an empty result
            return new Dictionary<string, FileCoverageInfo>();
        }
    }
}