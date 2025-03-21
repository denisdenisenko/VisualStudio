using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace SimpleCoverage.CoverageAdornment
{
    /// <summary>
    /// Factory that creates adornments for displaying code coverage information
    /// </summary>
    [Export(typeof(IWpfTextViewMarginProvider))]
    [Name("CoverageAdornment")]
    [Order(After = PredefinedMarginNames.LineNumber)]
    [MarginContainer(PredefinedMarginNames.Left)]
    [ContentType("text")]
    [TextViewRole(PredefinedTextViewRoles.Document)]
    internal sealed class CoverageAdornmentFactory : IWpfTextViewMarginProvider
    {
        [Import]
        private CoverageAdornmentManager AdornmentManager { get; set; }

        /// <summary>
        /// Creates a margin for the specified text view
        /// </summary>
        public IWpfTextViewMargin CreateMargin(IWpfTextViewHost textViewHost, IWpfTextViewMargin containerMargin)
        {
            return new CoverageMarginElement(textViewHost.TextView, AdornmentManager);
        }
    }
}