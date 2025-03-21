using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.IO;

namespace SimpleCoverage.Resources
{
    /// <summary>
    /// Provides the code coverage icon
    /// </summary>
    public static class CodeCoverageIcon
    {
        /// <summary>
        /// Gets the code coverage icon as a bitmap
        /// </summary>
        public static BitmapSource GetIcon()
        {
            // Create a canvas to draw on
            var canvas = new Canvas
            {
                Width = 16,
                Height = 16,
                Background = Brushes.Transparent
            };

            // Draw a document icon with colored blocks for coverage
            var documentOutline = new Rectangle
            {
                Width = 12,
                Height = 14,
                Fill = Brushes.White,
                Stroke = Brushes.Black,
                StrokeThickness = 1
            };
            Canvas.SetLeft(documentOutline, 2);
            Canvas.SetTop(documentOutline, 1);
            canvas.Children.Add(documentOutline);

            // Add coverage lines
            var coveredLine = new Rectangle
            {
                Width = 8,
                Height = 2,
                Fill = Brushes.LightGreen
            };
            Canvas.SetLeft(coveredLine, 4);
            Canvas.SetTop(coveredLine, 3);
            canvas.Children.Add(coveredLine);

            var partialLine = new Rectangle
            {
                Width = 8,
                Height = 2,
                Fill = Brushes.Yellow
            };
            Canvas.SetLeft(partialLine, 4);
            Canvas.SetTop(partialLine, 6);
            canvas.Children.Add(partialLine);

            var uncoveredLine = new Rectangle
            {
                Width = 8,
                Height = 2,
                Fill = Brushes.Red
            };
            Canvas.SetLeft(uncoveredLine, 4);
            Canvas.SetTop(uncoveredLine, 9);
            canvas.Children.Add(uncoveredLine);

            // Render the canvas to a bitmap
            var renderTarget = new RenderTargetBitmap(
                16, 16, 96, 96, PixelFormats.Pbgra32);
            renderTarget.Render(canvas);
            return renderTarget;
        }

        /// <summary>
        /// Saves the code coverage icon to a file
        /// </summary>
        public static void SaveIconToFile(string filePath)
        {
            var icon = GetIcon();

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(icon));
                encoder.Save(fileStream);
            }
        }
    }
}