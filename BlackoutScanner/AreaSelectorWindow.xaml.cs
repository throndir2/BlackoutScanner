using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Serilog;
using System;

namespace BlackoutScanner
{
    public partial class AreaSelectorWindow : Window
    {
        public System.Drawing.Rectangle SelectedRectangle { get; private set; }
        private System.Windows.Point startPoint;
        private bool isDragging = false;
        private System.Drawing.Rectangle targetWindowRect;
        private double dpiScale;

        public AreaSelectorWindow(System.Drawing.Rectangle gameWindowRect)
        {
            InitializeComponent();

            targetWindowRect = gameWindowRect;

            // Get DPI scale - but we need to get it after the window is loaded
            this.Loaded += (s, e) =>
            {
                dpiScale = DpiHelper.GetDpiScaleFactor(this);
                SetupOverlay();
            };
        }

        private void SetupOverlay()
        {
            // Get actual screen dimensions using WPF methods
            var screenWidth = SystemParameters.PrimaryScreenWidth;
            var screenHeight = SystemParameters.PrimaryScreenHeight;

            // Set window size to cover entire screen in logical pixels
            this.Left = 0;
            this.Top = 0;
            this.Width = screenWidth;
            this.Height = screenHeight;

            // Draw a border around the game window area
            var gameAreaBorder = new Border
            {
                BorderBrush = new SolidColorBrush(Colors.LimeGreen),
                BorderThickness = new Thickness(3), // Make it a bit thicker for visibility
                Width = targetWindowRect.Width / dpiScale,
                Height = targetWindowRect.Height / dpiScale,
                IsHitTestVisible = false
            };

            // Position the border correctly
            Canvas.SetLeft(gameAreaBorder, targetWindowRect.Left / dpiScale);
            Canvas.SetTop(gameAreaBorder, targetWindowRect.Top / dpiScale);
            selectionCanvas.Children.Insert(0, gameAreaBorder);

            // Add keyboard shortcuts
            this.PreviewKeyDown += Window_PreviewKeyDown;

            // Focus the window
            this.Focus();

            // Show instructions
            instructionText.Text = "Click and drag to select an area within the green border. Press ENTER to confirm or ESC to cancel.";

            // Log for debugging
            Log.Debug($"DPI Scale: {dpiScale}");
            Log.Debug($"Game window rect (physical): {targetWindowRect}");
            Log.Debug($"Game window rect (logical): X={targetWindowRect.Left / dpiScale}, Y={targetWindowRect.Top / dpiScale}, W={targetWindowRect.Width / dpiScale}, H={targetWindowRect.Height / dpiScale}");
            Log.Debug($"Screen size (physical): {screenWidth}x{screenHeight}");
            Log.Debug($"Screen size (logical): {this.Width}x{this.Height}");
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ConfirmSelection();
            }
            else if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }

        private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var mousePos = e.GetPosition(this.selectionCanvas);

            // Check if click is within the game window area
            var gameAreaLeft = targetWindowRect.Left / dpiScale;
            var gameAreaTop = targetWindowRect.Top / dpiScale;
            var gameAreaRight = gameAreaLeft + (targetWindowRect.Width / dpiScale);
            var gameAreaBottom = gameAreaTop + (targetWindowRect.Height / dpiScale);

            if (mousePos.X >= gameAreaLeft && mousePos.X <= gameAreaRight &&
                mousePos.Y >= gameAreaTop && mousePos.Y <= gameAreaBottom)
            {
                startPoint = mousePos;
                isDragging = true;
                this.selectionCanvas.CaptureMouse();

                // Reset selection rectangle
                Canvas.SetLeft(this.selectionRectangle, startPoint.X);
                Canvas.SetTop(this.selectionRectangle, startPoint.Y);
                this.selectionRectangle.Width = 0;
                this.selectionRectangle.Height = 0;
                this.selectionRectangle.Visibility = Visibility.Visible;
            }
        }

        private void Canvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragging)
            {
                System.Windows.Point currentPoint = e.GetPosition(selectionCanvas);

                // Constrain to game window area
                var gameAreaLeft = targetWindowRect.Left / dpiScale;
                var gameAreaTop = targetWindowRect.Top / dpiScale;
                var gameAreaRight = gameAreaLeft + (targetWindowRect.Width / dpiScale);
                var gameAreaBottom = gameAreaTop + (targetWindowRect.Height / dpiScale);

                currentPoint.X = Math.Max(gameAreaLeft, Math.Min(gameAreaRight, currentPoint.X));
                currentPoint.Y = Math.Max(gameAreaTop, Math.Min(gameAreaBottom, currentPoint.Y));

                double x = Math.Min(currentPoint.X, startPoint.X);
                double y = Math.Min(currentPoint.Y, startPoint.Y);
                double width = Math.Abs(currentPoint.X - startPoint.X);
                double height = Math.Abs(currentPoint.Y - startPoint.Y);

                Canvas.SetLeft(this.selectionRectangle, x);
                Canvas.SetTop(this.selectionRectangle, y);
                this.selectionRectangle.Width = width;
                this.selectionRectangle.Height = height;

                // Update coordinate display
                UpdateCoordinateDisplay(x, y, width, height);
            }
        }

        private void UpdateCoordinateDisplay(double x, double y, double width, double height)
        {
            // Convert to relative coordinates within the game window
            var relX = (x - targetWindowRect.Left / dpiScale) * dpiScale;
            var relY = (y - targetWindowRect.Top / dpiScale) * dpiScale;
            var relWidth = width * dpiScale;
            var relHeight = height * dpiScale;

            coordinateText.Text = $"X: {(int)relX}, Y: {(int)relY}, W: {(int)relWidth}, H: {(int)relHeight}";
        }

        private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging)
            {
                isDragging = false;
                this.selectionCanvas.ReleaseMouseCapture();
            }
        }

        private void ConfirmSelection()
        {
            if (this.selectionRectangle.Width > 0 && this.selectionRectangle.Height > 0)
            {
                // Get the selection in logical pixels
                double logicalX = Canvas.GetLeft(this.selectionRectangle);
                double logicalY = Canvas.GetTop(this.selectionRectangle);
                double logicalWidth = this.selectionRectangle.Width;
                double logicalHeight = this.selectionRectangle.Height;

                // Convert to coordinates relative to the game window
                var relX = (logicalX - targetWindowRect.Left / dpiScale) * dpiScale;
                var relY = (logicalY - targetWindowRect.Top / dpiScale) * dpiScale;
                var relWidth = logicalWidth * dpiScale;
                var relHeight = logicalHeight * dpiScale;

                Log.Debug($"Selection relative to game window: X={relX}, Y={relY}, W={relWidth}, H={relHeight}");

                // Store as rectangle relative to game window origin
                SelectedRectangle = new System.Drawing.Rectangle(
                    (int)Math.Round(relX),
                    (int)Math.Round(relY),
                    (int)Math.Round(relWidth),
                    (int)Math.Round(relHeight)
                );

                DialogResult = true;
                Close();
            }
            else
            {
                MessageBox.Show("Please select an area.", "No Area Selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            ConfirmSelection();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
