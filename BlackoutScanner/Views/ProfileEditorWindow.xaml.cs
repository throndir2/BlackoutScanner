using BlackoutScanner.Models;
using BlackoutScanner.Services;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BlackoutScanner.Views
{
    public partial class ProfileEditorWindow : Window, INotifyPropertyChanged
    {
        public GameProfile Profile { get; private set; }
        public bool IsActiveProfile { get; set; } // Add this property
        private readonly ObservableCollection<CaptureCategory> categories;
        private readonly ScreenCapture screenCapture = new ScreenCapture();

        public ProfileEditorWindow(GameProfile? profile = null)
        {
            InitializeComponent();

            if (profile == null)
            {
                // Creating a new profile
                Profile = new GameProfile
                {
                    ProfileName = "New Game Profile",
                    GameWindowTitle = "",
                    Categories = new List<CaptureCategory>()
                };
                Title = "New Game Profile";
            }
            else
            {
                // Editing an existing profile
                // Create a deep copy to avoid modifying the original until save
                var json = JsonConvert.SerializeObject(profile);
                Profile = JsonConvert.DeserializeObject<GameProfile>(json)!;
                Title = $"Edit Profile - {Profile.ProfileName}";
                Log.Information("Editing profile for {GameTitle}", Profile.ProfileName);

                // Convert RelativeBounds to Bounds for UI display
                ConvertRelativeBoundsToAbsolute();
            }

            profileNameTextBox.Text = Profile.ProfileName;
            gameTitleTextBox.Text = Profile.GameWindowTitle;
            categories = new ObservableCollection<CaptureCategory>(Profile.Categories);
            categoriesTabControl.ItemsSource = categories;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private void ConvertRelativeBoundsToAbsolute()
        {
            var gameWindowRect = screenCapture.GetWindowRectangle(Profile.GameWindowTitle);
            if (gameWindowRect.IsEmpty)
            {
                Log.Warning("Could not find game window '{GameTitle}' to calculate absolute bounds. Bounds will be empty.", Profile.GameWindowTitle);
                return;
            }

            var containerRect = new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height);
            Log.Information("Game window '{GameTitle}' found with container rect: {Rect}", Profile.GameWindowTitle, containerRect);

            foreach (var category in Profile.Categories)
            {
                category.Bounds = category.RelativeBounds.ToAbsolute(containerRect);
                Log.Debug("Converted category '{CategoryName}' relative bounds {RelativeBounds} to absolute {AbsoluteBounds}", category.Name, category.RelativeBounds, category.Bounds);
                foreach (var field in category.Fields)
                {
                    field.Bounds = field.RelativeBounds.ToAbsolute(containerRect);
                    Log.Debug("Converted field '{FieldName}' relative bounds {RelativeBounds} to absolute {AbsoluteBounds}", field.Name, field.RelativeBounds, field.Bounds);
                }
            }
        }

        private void AddCategory_Click(object sender, RoutedEventArgs e)
        {
            var newCategory = new CaptureCategory
            {
                Name = "New Category",
                Fields = new ObservableCollection<CaptureField>()
            };
            categories.Add(newCategory);
            categoriesTabControl.SelectedItem = newCategory;
        }

        private void RemoveCategory_Click(object sender, RoutedEventArgs e)
        {
            if (categoriesTabControl.SelectedItem is CaptureCategory selectedCategory)
            {
                categories.Remove(selectedCategory);
            }
            else
            {
                MessageBox.Show("Please select a category to remove.");
            }
        }

        private void DefineCategoryArea_Click(object sender, RoutedEventArgs e)
        {
            var gameTitle = gameTitleTextBox.Text;
            if (string.IsNullOrWhiteSpace(gameTitle))
            {
                MessageBox.Show("Please enter a game window title first.", "Game Title Required");
                return;
            }

            var gameWindowRect = screenCapture.GetClientRectangle(gameTitle);
            if (gameWindowRect.IsEmpty)
            {
                MessageBox.Show($"Could not find a window with the title '{gameTitle}'.\nPlease ensure the game is running and the title is correct.", "Window Not Found");
                return;
            }

            if (categoriesTabControl.SelectedItem is CaptureCategory selectedCategory)
            {
                // Minimize the profile editor window temporarily
                this.WindowState = WindowState.Minimized;

                // Give the game window focus
                screenCapture.BringGameWindowToFront(gameTitle);

                // Small delay to ensure window state changes are applied
                System.Threading.Thread.Sleep(200);

                // Create overlay without screenshot - just pass the game window bounds
                var areaSelector = new AreaSelectorWindow(gameWindowRect);
                if (areaSelector.ShowDialog() == true)
                {
                    var selectionInWindowCoordinates = areaSelector.SelectedRectangle;

                    // Log the selection for debugging
                    Log.Debug($"Selection rectangle: {selectionInWindowCoordinates}");

                    // Store the relative bounds
                    selectedCategory.RelativeBounds = RelativeBounds.FromAbsolute(selectionInWindowCoordinates, new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height));
                    selectedCategory.Bounds = selectionInWindowCoordinates; // For UI display

                    // Now capture just the selected area for preview
                    using (var gameWindowScreenshot = screenCapture.CaptureScreenArea(gameWindowRect))
                    {
                        if (selectionInWindowCoordinates.Width > 0 && selectionInWindowCoordinates.Height > 0)
                        {
                            using (var categoryScreenshot = gameWindowScreenshot.Clone(selectionInWindowCoordinates, gameWindowScreenshot.PixelFormat))
                            {
                                selectedCategory.PreviewImage = ConvertBitmapToBitmapImage(categoryScreenshot);
                            }
                        }
                    }
                }

                // Restore the profile editor window
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
            else
            {
                MessageBox.Show("Please select a category first.", "No Category Selected");
            }
        }

        private void AddField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is CaptureCategory selectedCategory)
            {
                var newField = new CaptureField
                {
                    Name = "New Field",
                    IsKeyField = true  // Set to true by default
                };
                selectedCategory.Fields.Add(newField);
            }
            else
            {
                MessageBox.Show("Could not find the category to add the field to.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void RemoveField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is CaptureField fieldToRemove)
            {
                // Get the current selected category
                var selectedCategory = categoriesTabControl.SelectedItem as CaptureCategory;
                if (selectedCategory != null)
                {
                    selectedCategory.Fields.Remove(fieldToRemove);
                }
            }
        }

        private void DefineFieldAreaButton_Click(object sender, RoutedEventArgs e)
        {
            var gameTitle = gameTitleTextBox.Text;
            if (string.IsNullOrWhiteSpace(gameTitle))
            {
                MessageBox.Show("Please enter a game window title first.", "Game Title Required");
                return;
            }

            var gameWindowRect = screenCapture.GetClientRectangle(gameTitle);
            if (gameWindowRect.IsEmpty)
            {
                MessageBox.Show($"Could not find a window with the title '{gameTitle}'.\nPlease ensure the game is running and the title is correct.", "Window Not Found");
                return;
            }

            if (sender is Button button && button.Tag is CaptureField selectedField)
            {
                // We already have gameTitle and gameWindowRect from above
                if (string.IsNullOrWhiteSpace(gameTitle))
                {
                    MessageBox.Show("Please enter a game window title first.", "Game Title Required");
                    return;
                }

                // Minimize the profile editor window temporarily
                this.WindowState = WindowState.Minimized;

                // Give the game window focus
                screenCapture.BringGameWindowToFront(gameTitle);

                // Small delay to ensure window state changes are applied
                System.Threading.Thread.Sleep(200);

                // Create overlay without screenshot
                var areaSelector = new AreaSelectorWindow(gameWindowRect);
                if (areaSelector.ShowDialog() == true)
                {
                    var selectionInWindowCoordinates = areaSelector.SelectedRectangle;

                    selectedField.RelativeBounds = RelativeBounds.FromAbsolute(selectionInWindowCoordinates, new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height));
                    selectedField.Bounds = selectionInWindowCoordinates; // For UI display

                    // Now capture just the selected area for preview
                    using (var gameWindowScreenshot = screenCapture.CaptureScreenArea(gameWindowRect))
                    {
                        if (selectionInWindowCoordinates.Width > 0 && selectionInWindowCoordinates.Height > 0)
                        {
                            using (var fieldScreenshot = gameWindowScreenshot.Clone(selectionInWindowCoordinates, gameWindowScreenshot.PixelFormat))
                            {
                                selectedField.PreviewImage = ConvertBitmapToBitmapImage(fieldScreenshot);
                            }
                        }
                    }
                }

                // Restore the profile editor window
                this.WindowState = WindowState.Normal;
                this.Activate();
            }
        }

        private void SearchWindow_Click(object sender, RoutedEventArgs e)
        {
            var searchDialog = new WindowSearchDialog();
            searchDialog.Owner = this;

            if (searchDialog.ShowDialog() == true && !string.IsNullOrEmpty(searchDialog.SelectedWindowTitle))
            {
                gameTitleTextBox.Text = searchDialog.SelectedWindowTitle;
                Log.Information($"Selected window title: {searchDialog.SelectedWindowTitle}");
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Profile.ProfileName = profileNameTextBox.Text;
            Profile.GameWindowTitle = gameTitleTextBox.Text;
            Profile.Categories = new List<CaptureCategory>(categories);

            var gameWindowRect = screenCapture.GetWindowRectangle(Profile.GameWindowTitle);
            if (gameWindowRect.IsEmpty)
            {
                Log.Warning("Game window '{GameTitle}' not found. Relative bounds will be calculated against an empty rectangle.", Profile.GameWindowTitle);
            }

            var containerRect = new Rectangle(0, 0, gameWindowRect.Width, gameWindowRect.Height);

            foreach (var category in Profile.Categories)
            {
                category.Bounds = category.RelativeBounds.ToAbsolute(containerRect);
                foreach (var field in category.Fields)
                {
                    field.Bounds = field.RelativeBounds.ToAbsolute(containerRect);
                }
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
        {
            using (var memory = new MemoryStream())
            {
                bitmap.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                memory.Position = 0;
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.StreamSource = memory;
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
        }

        private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                Log.Verbose("FindVisualChild: Traversing child {ChildType} of parent {ParentType}", child.GetType().Name, parent.GetType().Name);
                if (child is T t)
                {
                    Log.Information("FindVisualChild: Found matching child of type {ChildType}", typeof(T).Name);
                    return t;
                }

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            Log.Warning("FindVisualChild: Could not find child of type {ChildType} in parent {ParentType}", typeof(T).Name, parent.GetType().Name);
            return null;
        }

        private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            DependencyObject parentObject = VisualTreeHelper.GetParent(child);

            if (parentObject == null) return null;

            T? parent = parentObject as T;
            if (parent != null)
            {
                return parent;
            }
            else
            {
                return FindParent<T>(parentObject);
            }
        }
    }
}
