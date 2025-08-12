using BlackoutScanner.Models;
using BlackoutScanner.Utilities;
using Serilog;
using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using Rectangle = System.Drawing.Rectangle;

namespace BlackoutScanner.Views
{
    public partial class MultiEntitySelectorWindow : Window
    {
        // Properties
        public CaptureCategory CategoryToPreview { get; set; } = null!;
        public int UpdatedHeightOffset { get; set; }

        // Private fields
        private System.Drawing.Rectangle targetWindowRect;
        private double dpiScale;
        private int originalOffset;
        private double dragStartY = 0;
        private bool isDragging = false;
        private string gameTitle;

        public MultiEntitySelectorWindow(System.Drawing.Rectangle gameWindowRect, CaptureCategory category, string gameWindowTitle)
        {
            InitializeComponent();

            targetWindowRect = gameWindowRect;
            CategoryToPreview = category;
            gameTitle = gameWindowTitle;

            if (CategoryToPreview == null)
            {
                MessageBox.Show("No category provided for preview.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
                return;
            }

            originalOffset = CategoryToPreview.EntityHeightOffset;
            UpdatedHeightOffset = originalOffset;

            // Get DPI scale after window is loaded
            this.Loaded += (s, e) =>
            {
                dpiScale = DpiHelper.GetDpiScaleFactor(this);
                SetupOverlay();
                DrawFieldPreviews();
            };
        }

        private void SetupOverlay()
        {
            // Set window to cover entire screen
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            this.Left = 0;
            this.Top = 0;
            this.Width = screenWidth;
            this.Height = screenHeight;

            // Add mouse event handlers for dragging
            previewCanvas.MouseDown += Canvas_MouseDown;
            previewCanvas.MouseMove += Canvas_MouseMove;
            previewCanvas.MouseUp += Canvas_MouseUp;

            // Add keyboard shortcuts
            this.PreviewKeyDown += Window_PreviewKeyDown;

            // Focus the window
            this.Focus();

            // Update initial status
            UpdateStatusText();

            Log.Debug($"Multi-Entity Preview initialized - DPI: {dpiScale}, Game Window: {targetWindowRect}");
        }

        private void DrawFieldPreviews()
        {
            if (CategoryToPreview?.Fields == null || CategoryToPreview.Fields.Count == 0)
            {
                instructionText.Text = "No fields defined for this category. Press ESC to go back.";
                return;
            }

            // Clear existing elements
            previewCanvas.Children.Clear();

            // Draw game window border
            var gameAreaBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LimeGreen),
                BorderThickness = new Thickness(3),
                Width = targetWindowRect.Width / dpiScale,
                Height = targetWindowRect.Height / dpiScale,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(gameAreaBorder, targetWindowRect.Left / dpiScale);
            Canvas.SetTop(gameAreaBorder, targetWindowRect.Top / dpiScale);
            previewCanvas.Children.Add(gameAreaBorder);

            // Define colors for different rows
            var rowColors = new[] { Colors.Cyan, Colors.Yellow, Colors.Lime, Colors.Orange, Colors.Magenta };
            var totalFieldsDrawn = 0;

            // Debug logging
            Log.Debug($"Drawing field previews for category '{CategoryToPreview.Name}'");
            Log.Debug($"Target window rect: {targetWindowRect}");
            Log.Debug($"DPI Scale: {dpiScale}");

            // Draw all fields for each row
            for (int rowIndex = 0; rowIndex < CategoryToPreview.MaxEntityCount; rowIndex++)
            {
                var rowOffsetY = rowIndex * UpdatedHeightOffset;
                var rowColor = rowColors[rowIndex % rowColors.Length];
                bool rowExceedsBounds = false;

                foreach (var field in CategoryToPreview.Fields)
                {
                    if (field.Bounds == System.Drawing.Rectangle.Empty || field.Bounds.Width == 0 || field.Bounds.Height == 0)
                        continue;

                    // Debug first field of first row
                    if (rowIndex == 0 && totalFieldsDrawn == 0)
                    {
                        Log.Debug($"First field '{field.Name}' Bounds: {field.Bounds}");
                        Log.Debug($"First field position will be at: X={(targetWindowRect.Left + field.Bounds.X) / dpiScale}, Y={(targetWindowRect.Top + field.Bounds.Y) / dpiScale}");
                    }

                    // Calculate field position - field.Bounds is already relative to game window origin (0,0)
                    var fieldY = field.Bounds.Y + rowOffsetY;

                    // Check if field exceeds game window bounds
                    if (fieldY + field.Bounds.Height > targetWindowRect.Height)
                    {
                        rowExceedsBounds = true;
                        break;
                    }

                    // Create field rectangle
                    var fieldRect = new System.Windows.Shapes.Rectangle
                    {
                        Width = field.Bounds.Width / dpiScale,
                        Height = field.Bounds.Height / dpiScale,
                        Stroke = new SolidColorBrush(rowColor),
                        StrokeThickness = rowIndex == 0 ? 2 : 1,
                        Fill = new SolidColorBrush(rowColor) { Opacity = rowIndex == 0 ? 0.25 : 0.1 },
                        IsHitTestVisible = false
                    };

                    // Dashed line for non-first rows
                    if (rowIndex > 0)
                    {
                        fieldRect.StrokeDashArray = new DoubleCollection(new double[] { 4, 2 });
                    }

                    // Position field rectangle
                    // field.Bounds.X and Y are relative to game window origin (0,0)
                    // We need to add targetWindowRect.Left/Top to position them correctly on screen
                    Canvas.SetLeft(fieldRect, (targetWindowRect.Left + field.Bounds.X) / dpiScale);
                    Canvas.SetTop(fieldRect, (targetWindowRect.Top + fieldY) / dpiScale);
                    previewCanvas.Children.Add(fieldRect);

                    // Add field label for first row only
                    if (rowIndex == 0)
                    {
                        var label = new TextBlock
                        {
                            Text = field.Name,
                            Foreground = new SolidColorBrush(rowColor),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Background = new SolidColorBrush(Colors.Black) { Opacity = 0.7 },
                            Padding = new Thickness(3),
                            IsHitTestVisible = false
                        };
                        Canvas.SetLeft(label, (targetWindowRect.Left + field.Bounds.X) / dpiScale + 2);
                        Canvas.SetTop(label, (targetWindowRect.Top + fieldY) / dpiScale + 2);
                        previewCanvas.Children.Add(label);
                    }

                    totalFieldsDrawn++;
                }

                // Show warning if rows exceed bounds
                if (rowExceedsBounds)
                {
                    var warningText = new TextBlock
                    {
                        Text = $"⚠ Rows {rowIndex + 1}-{CategoryToPreview.MaxEntityCount} exceed screen bounds",
                        Foreground = new SolidColorBrush(Colors.Red),
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Background = new SolidColorBrush(Colors.Black) { Opacity = 0.8 },
                        Padding = new Thickness(5),
                        IsHitTestVisible = false
                    };
                    Canvas.SetLeft(warningText, targetWindowRect.Left / dpiScale + 10);
                    Canvas.SetTop(warningText, (targetWindowRect.Top + targetWindowRect.Height) / dpiScale - 40);
                    previewCanvas.Children.Add(warningText);
                    break;
                }

                // Add row indicator on the left
                var rowIndicator = new Border
                {
                    Background = new SolidColorBrush(rowColor) { Opacity = 0.8 },
                    CornerRadius = new CornerRadius(3),
                    Padding = new Thickness(5, 2, 5, 2),
                    IsHitTestVisible = false,
                    Child = new TextBlock
                    {
                        Text = $"Row {rowIndex + 1}",
                        Foreground = new SolidColorBrush(Colors.Black),
                        FontWeight = FontWeights.Bold,
                        FontSize = 10
                    }
                };

                // Position indicator using the first field's position if available
                if (CategoryToPreview.Fields.Count > 0)
                {
                    var firstField = CategoryToPreview.Fields[0];
                    if (firstField.Bounds != System.Drawing.Rectangle.Empty)
                    {
                        Canvas.SetLeft(rowIndicator, (targetWindowRect.Left + firstField.Bounds.X) / dpiScale - 65);
                        Canvas.SetTop(rowIndicator, (targetWindowRect.Top + firstField.Bounds.Y + rowOffsetY) / dpiScale);
                        previewCanvas.Children.Add(rowIndicator);
                    }
                }
            }

            // Update summary
            var totalPossibleFields = CategoryToPreview.Fields.Count * CategoryToPreview.MaxEntityCount;
            summaryText.Text = $"Total fields to capture: {totalFieldsDrawn} / {totalPossibleFields} " +
                              $"({CategoryToPreview.Fields.Count} fields × {CategoryToPreview.MaxEntityCount} rows)";
        }

        private void UpdateStatusText()
        {
            statusText.Text = $"Row Height Offset: {UpdatedHeightOffset} pixels | " +
                             $"Fields: {CategoryToPreview?.Fields?.Count ?? 0} | " +
                             $"Max Rows: {CategoryToPreview?.MaxEntityCount ?? 0}";

            // Update instruction based on whether we're dragging
            if (isDragging)
            {
                instructionText.Text = $"Dragging... Current offset: {UpdatedHeightOffset}px (Original: {originalOffset}px)";
            }
            else
            {
                instructionText.Text = "Drag UP/DOWN to adjust row spacing. Use ↑↓ keys for fine control. Press ENTER to save or ESC to cancel.";
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            isDragging = true;
            dragStartY = e.GetPosition(previewCanvas).Y;
            previewCanvas.CaptureMouse();
            UpdateStatusText();
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                var currentY = e.GetPosition(previewCanvas).Y;
                var deltaY = dragStartY - currentY; // Inverted: drag up = increase offset

                // Calculate new offset with sensitivity adjustment
                var newOffset = originalOffset + (int)(deltaY * 0.5);
                UpdatedHeightOffset = Math.Max(10, Math.Min(500, newOffset));

                // Redraw previews with new offset
                DrawFieldPreviews();
                UpdateStatusText();
            }
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                previewCanvas.ReleaseMouseCapture();

                // Update original offset for next drag operation
                originalOffset = UpdatedHeightOffset;
                UpdateStatusText();
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Confirm_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Escape)
            {
                Cancel_Click(this, new RoutedEventArgs());
            }
            else if (e.Key == Key.Up || e.Key == Key.Down)
            {
                // Keyboard adjustment
                int adjustment = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;

                if (e.Key == Key.Up)
                {
                    UpdatedHeightOffset = Math.Min(500, UpdatedHeightOffset + adjustment);
                }
                else
                {
                    UpdatedHeightOffset = Math.Max(10, UpdatedHeightOffset - adjustment);
                }

                DrawFieldPreviews();
                UpdateStatusText();
                e.Handled = true;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Reset to original value
            UpdatedHeightOffset = CategoryToPreview.EntityHeightOffset;
            DialogResult = false;
            Close();
        }
    }
}
