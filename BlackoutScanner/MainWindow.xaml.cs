using BlackoutScanner.Interfaces;
using BlackoutScanner.Infrastructure;
using BlackoutScanner.Models;
using BlackoutScanner.Infrastructure;
using Newtonsoft.Json;
using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinImage = System.Windows.Controls.Image;
using Microsoft.Win32; // For SaveFileDialog/OpenFileDialog

namespace BlackoutScanner
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isScanning = false;
        private IScanner? scanner;
        private IDataManager? dataManager;
        private IOCRProcessor? ocrProcessor;
        private IGameProfileManager? _gameProfileManager;

        public IGameProfileManager? GameProfileManager
        {
            get => _gameProfileManager;
            private set
            {
                _gameProfileManager = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // Debug settings
        private bool saveDebugImages = false;
        private bool verboseLogging = false;
        private string debugImagesFolder = "DebugImages";

        private const string tessdataDirectory = "tessdata";
        private readonly string[] SupportedLanguages = { "eng", "kor", "jpn", "chi_sim", "chi_tra" };
        private readonly string cacheFilePath = "ocrCache.json";

        private Dictionary<string, TextBox> fieldTextBoxes = new Dictionary<string, TextBox>();
        private Dictionary<string, WinImage> fieldImages = new Dictionary<string, WinImage>();
        private string? currentRecordHash = null;

        // Dictionary to store ObservableCollections for each category
        private Dictionary<string, ObservableCollection<DataRecord>> categoryCollections = new Dictionary<string, ObservableCollection<DataRecord>>();

        // Add these new fields to store the original key values
        private Dictionary<DataRecord, Dictionary<string, object>> originalKeyValues = new Dictionary<DataRecord, Dictionary<string, object>>();

        // Throttling for UI updates to prevent excessive operations
        private System.Windows.Threading.DispatcherTimer autoSizeTimer;
        private HashSet<DataGrid> dataGridsToResize = new HashSet<DataGrid>();
        private const int AutoSizeThrottleMilliseconds = 500; // Wait this long before resizing columns

        // Temporary values for editing
        private string originalName = string.Empty;
        private string originalKingdom = string.Empty;
        private string originalAlliance = string.Empty;


        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            // Set up logging for UI
            SetupLoggingToUI();

            scanButton.IsEnabled = false;

            // Initialize the auto-size timer for throttled column resizing
            autoSizeTimer = new System.Windows.Threading.DispatcherTimer();
            autoSizeTimer.Interval = TimeSpan.FromMilliseconds(AutoSizeThrottleMilliseconds);
            autoSizeTimer.Tick += AutoSizeTimer_Tick;

            this.Show();

            // Then, start the initialization in the background
            Task.Run(() => InitializeAppComponents());
        }

        private void AutoSizeTimer_Tick(object? sender, EventArgs e)
        {
            autoSizeTimer.Stop();

            // Process all DataGrids that need resizing
            foreach (var grid in dataGridsToResize)
            {
                if (grid != null)
                {
                    AutoSizeDataGridColumns(grid);
                }
            }

            // Clear the set after processing
            dataGridsToResize.Clear();
        }

        public void AppendLogMessage(string message)
        {
            // Ensure the operation happens on the UI thread
            Dispatcher.Invoke(() =>
            {
                logArea.AppendText(message + "\n");
                logArea.ScrollToEnd();
            });
        }

        private void SetupLoggingToUI()
        {
            // Subscribe to the static event from UISink
            BlackoutScanner.Infrastructure.UISink.LogMessage += OnLogMessage;
        }

        private void OnLogMessage(string message)
        {
            AppendLogMessage(message);
        }

        private void InitializeAppComponents()
        {
            try
            {
                Dispatcher.Invoke(() => AppendLogMessage("Initializing OCR components..."));

                // Get services from the ServiceLocator
                dataManager = ServiceLocator.GetService<IDataManager>();
                ocrProcessor = ServiceLocator.GetService<IOCRProcessor>();
                GameProfileManager = ServiceLocator.GetService<IGameProfileManager>();
                scanner = ServiceLocator.GetService<IScanner>();

                // Load debug settings
                LoadDebugSettings();

                // Load OCR cache
                var cacheData = LoadOcrResultsCache();
                ocrProcessor.LoadCache(cacheData);

                // Update the Scanner with our debug settings
                scanner.UpdateDebugSettings(saveDebugImages, verboseLogging, debugImagesFolder);
                SubscribeToScannerEvents();

                Dispatcher.Invoke(() =>
                {
                    Log.Information("OCR components initialized successfully.");
                    scanButton.IsEnabled = true;

                    // Check if we have any profiles
                    if (GameProfileManager.Profiles.Any())
                    {
                        // Check if we have a previously active profile
                        if (GameProfileManager.ActiveProfile != null)
                        {
                            SetupDynamicUI(GameProfileManager.ActiveProfile);
                            // IMPORTANT: Load data records BEFORE setting up the data UI
                            dataManager.LoadDataRecordsWithProfile(GameProfileManager.ActiveProfile);
                            SetupDynamicDataUI(GameProfileManager.ActiveProfile);
                            Log.Information($"Active profile restored: {GameProfileManager.ActiveProfile.ProfileName}");
                        }
                        else
                        {
                            // No active profile saved, use the first one
                            GameProfileManager.SetActiveProfile(GameProfileManager.Profiles.First());
                            if (GameProfileManager.ActiveProfile != null)
                            {
                                SetupDynamicUI(GameProfileManager.ActiveProfile);
                                // IMPORTANT: Load data records BEFORE setting up the data UI
                                dataManager.LoadDataRecordsWithProfile(GameProfileManager.ActiveProfile);
                                SetupDynamicDataUI(GameProfileManager.ActiveProfile);
                                Log.Information($"Active profile set to: {GameProfileManager.ActiveProfile.ProfileName}");
                            }
                        }
                    }
                    else
                    {
                        Log.Information("No game profiles found. Please create one in the Configuration tab.");
                    }

                    LoadProfilesIntoView();
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLogMessage($"Initialization failed: {ex.Message}"));
                Log.Error(ex, "Failed to initialize OCR components.");
            }
        }

        private void LoadProfilesIntoView()
        {
            if (GameProfileManager != null)
            {
                profilesListBox.ItemsSource = null;
                profilesListBox.ItemsSource = GameProfileManager.Profiles;
            }
        }

        private void SetupDynamicDataUI(GameProfile profile)
        {
            // Clear existing tabs and collections
            dataTabControl.Items.Clear();
            categoryCollections.Clear();

            // Update profile indicator
            dataProfileIndicator.Text = $"Profile: {profile.ProfileName}";

            // Create a tab for each category
            foreach (var category in profile.Categories)
            {
                var tabItem = new TabItem
                {
                    Header = category.Name,
                    Tag = category // Store category reference
                };

                // Create a DataGrid for this category with virtualization
                var dataGrid = new DataGrid
                {
                    AutoGenerateColumns = false,
                    CanUserAddRows = false,
                    CanUserDeleteRows = true,
                    IsReadOnly = false, // Make the grid editable
                    HeadersVisibility = DataGridHeadersVisibility.Column,
                    GridLinesVisibility = DataGridGridLinesVisibility.Horizontal,
                    Margin = new Thickness(5),
                    Tag = category.Name, // Store category name for reference
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    ColumnWidth = DataGridLength.Auto, // Default column width to auto
                    CanUserResizeColumns = true,
                    EnableRowVirtualization = true, // Enable virtualization for performance
                    EnableColumnVirtualization = true, // Enable column virtualization
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
                };

                // Add event handlers for cell editing
                dataGrid.CellEditEnding += DataGrid_CellEditEnding;
                dataGrid.PreparingCellForEdit += DataGrid_PreparingCellForEdit;

                // Set virtualization properties via attached properties
                VirtualizingPanel.SetIsVirtualizing(dataGrid, true);
                VirtualizingPanel.SetVirtualizationMode(dataGrid, VirtualizationMode.Recycling);

                // Create columns based on the fields defined in the category
                foreach (var field in category.Fields)
                {
                    var column = new DataGridTextColumn
                    {
                        Header = field.Name,
                        Binding = new System.Windows.Data.Binding($"Fields[{field.Name}]")
                        {
                            Mode = System.Windows.Data.BindingMode.TwoWay,
                            UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged
                        },
                        Width = DataGridLength.Auto,
                        MinWidth = 80 // Set minimum width for each column
                    };

                    // If it's a key field, mark it with a special style or indicator
                    if (field.IsKeyField)
                    {
                        column.Header = $"{field.Name} 🔑"; // Add key emoji to indicate key field
                        column.CellStyle = new Style(typeof(DataGridCell))
                        {
                            Setters =
                            {
                                new Setter(DataGridCell.ToolTipProperty, "This is a key field. Editing will create a new record entry.")
                            }
                        };
                    }
                    else
                    {
                        // Allow editing for non-key fields
                        column.IsReadOnly = false;
                    }

                    dataGrid.Columns.Add(column);
                }

                // Add ScanDate column
                var scanDateColumn = new DataGridTextColumn
                {
                    Header = "Scan Date",
                    Binding = new System.Windows.Data.Binding("ScanDate")
                    {
                        StringFormat = "{0:g}"
                    },
                    IsReadOnly = true,
                    Width = new DataGridLength(150)
                };
                dataGrid.Columns.Add(scanDateColumn);

                // Load data for this category
                LoadDataForCategory(dataGrid, category.Name);

                // Add event handler for when data is loaded
                dataGrid.Loaded += (s, e) => AutoSizeDataGridColumns(dataGrid);

                // Add context menu for row operations
                var contextMenu = new ContextMenu();
                var deleteMenuItem = new MenuItem { Header = "Delete Record" };
                deleteMenuItem.Click += (s, e) => DeleteDataRecord_Click(dataGrid);
                contextMenu.Items.Add(deleteMenuItem);
                dataGrid.ContextMenu = contextMenu;

                tabItem.Content = dataGrid;
                dataTabControl.Items.Add(tabItem);
            }
        }

        // Add this method to handle when a cell starts being edited
        private void DataGrid_PreparingCellForEdit(object? sender, DataGridPreparingCellForEditEventArgs e)
        {
            if (e.Row.Item is DataRecord record && e.Column is DataGridTextColumn textColumn)
            {
                var binding = textColumn.Binding as Binding;
                if (binding != null)
                {
                    var fieldName = binding.Path.Path.Replace("Fields[", "").Replace("]", "");

                    // Check if this is a key field
                    if (GameProfileManager?.ActiveProfile != null)
                    {
                        var category = GameProfileManager.ActiveProfile.Categories
                            .FirstOrDefault(c => c.Name == record.Category);

                        if (category != null)
                        {
                            var field = category.Fields.FirstOrDefault(f => f.Name == fieldName);
                            if (field != null && field.IsKeyField)
                            {
                                // Store all original key field values for this record
                                if (!originalKeyValues.ContainsKey(record))
                                {
                                    originalKeyValues[record] = new Dictionary<string, object>();
                                }

                                // Store all key fields' values
                                foreach (var keyField in category.Fields.Where(f => f.IsKeyField))
                                {
                                    if (record.Fields.TryGetValue(keyField.Name, out var value))
                                    {
                                        originalKeyValues[record][keyField.Name] = value;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }

        // Add this method to handle when cell editing is complete
        private void DataGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction == DataGridEditAction.Cancel)
                return;

            if (e.Row.Item is DataRecord record && e.Column is DataGridTextColumn textColumn)
            {
                var binding = textColumn.Binding as Binding;
                if (binding != null)
                {
                    var fieldName = binding.Path.Path.Replace("Fields[", "").Replace("]", "");

                    // Check if this is a key field
                    if (GameProfileManager?.ActiveProfile != null)
                    {
                        var category = GameProfileManager.ActiveProfile.Categories
                            .FirstOrDefault(c => c.Name == record.Category);

                        if (category != null)
                        {
                            var field = category.Fields.FirstOrDefault(f => f.Name == fieldName);
                            if (field != null && field.IsKeyField)
                            {
                                // Get the new value from the TextBox
                                var textBox = e.EditingElement as TextBox;
                                if (textBox != null)
                                {
                                    var newValue = textBox.Text;

                                    // Delay the update to allow the binding to complete
                                    Dispatcher.BeginInvoke(new Action(() =>
                                    {
                                        HandleKeyFieldChange(record, fieldName, newValue);
                                    }), System.Windows.Threading.DispatcherPriority.DataBind);
                                }
                            }
                            else
                            {
                                // For non-key fields, just mark as unsaved
                                if (dataManager != null)
                                {
                                    dataManager.MarkAsUnsaved();
                                    UpdateUnsavedIndicator();
                                }
                            }
                        }
                    }
                }
            }
        }

        // Add this method to handle key field changes
        private void HandleKeyFieldChange(DataRecord record, string changedFieldName, object newValue)
        {
            if (GameProfileManager?.ActiveProfile == null || dataManager == null)
                return;

            // Get original key values
            if (!originalKeyValues.TryGetValue(record, out var originalKeys))
                return;

            // Create a copy of the record with original key values to generate old hash
            var oldRecord = new DataRecord
            {
                Fields = new Dictionary<string, object>(record.Fields),
                ScanDate = record.ScanDate,
                Category = record.Category,
                GameProfile = record.GameProfile
            };

            // Restore original key values in the copy
            foreach (var kvp in originalKeys)
            {
                oldRecord.Fields[kvp.Key] = kvp.Value;
            }

            // Restore the changed field to its original value in the copy
            oldRecord.Fields[changedFieldName] = originalKeys[changedFieldName];

            // Generate the old hash
            var oldHash = dataManager.GenerateDataHash(oldRecord, GameProfileManager.ActiveProfile);

            // Update the record with the new value
            record.Fields[changedFieldName] = newValue;                // Generate the new hash
            var newHash = dataManager.GenerateDataHash(record, GameProfileManager.ActiveProfile);

            // If the hash changed, update the dictionary
            if (oldHash != newHash && !string.IsNullOrEmpty(oldHash) && !string.IsNullOrEmpty(newHash))
            {
                // Use the UpdateRecordKey method
                dataManager.UpdateRecordKey(oldHash, record, GameProfileManager.ActiveProfile);

                // Update the observable collection
                if (record.Category != null && categoryCollections.TryGetValue(record.Category, out var collection))
                {
                    // The record object itself is already updated, just need to refresh the view
                    var index = collection.IndexOf(record);
                    if (index >= 0)
                    {
                        // Force a refresh of the item in the collection
                        collection[index] = record;
                    }
                }

                Log.Information($"Updated key field '{changedFieldName}' for record. Old hash: {oldHash}, New hash: {newHash}");
            }

            // Clean up the stored original values
            originalKeyValues.Remove(record);

            // Update unsaved indicator
            UpdateUnsavedIndicator();
        }

        // Add this helper method to update the unsaved indicator
        private void UpdateUnsavedIndicator()
        {
            Dispatcher.Invoke(() =>
            {
                if (dataManager != null && dataManager.HasUnsavedChanges())
                {
                    unsavedIndicator.Visibility = Visibility.Visible;
                }
                else
                {
                    unsavedIndicator.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void LoadDataForCategory(DataGrid dataGrid, string categoryName)
        {
            // Create or get existing ObservableCollection for this category
            if (!categoryCollections.TryGetValue(categoryName, out var collection))
            {
                collection = new ObservableCollection<DataRecord>();
                categoryCollections[categoryName] = collection;
            }
            else
            {
                // Clear existing data but keep the same collection instance
                collection.Clear();
            }

            if (dataManager != null)
            {
                // Filter and add data records to the collection
                var categoryRecords = dataManager.DataRecordDictionary.Values
                    .Where(r => r.Category == categoryName)
                    .OrderByDescending(r => r.ScanDate);

                foreach (var record in categoryRecords)
                {
                    collection.Add(record);
                }
            }

            // Set the ItemsSource to our ObservableCollection
            dataGrid.ItemsSource = collection;

            // Schedule column auto-sizing with throttling
            if (!dataGridsToResize.Contains(dataGrid))
            {
                dataGridsToResize.Add(dataGrid);

                if (!autoSizeTimer.IsEnabled)
                {
                    autoSizeTimer.Start();
                }
            }
        }

        private void DeleteDataRecord_Click(DataGrid dataGrid)
        {
            if (dataGrid.SelectedItem is DataRecord record && GameProfileManager?.ActiveProfile != null && dataManager != null)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete this record?\n\n{record.ToDisplayString()}",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    // Generate hash to find the record in the dictionary
                    var hash = dataManager.GenerateDataHash(record, GameProfileManager.ActiveProfile);
                    if (!string.IsNullOrEmpty(hash) && dataManager.DataRecordDictionary.ContainsKey(hash))
                    {
                        dataManager.DataRecordDictionary.Remove(hash);

                        // Refresh the grid
                        if (dataGrid.Tag is string categoryName)
                        {
                            LoadDataForCategory(dataGrid, categoryName);
                        }

                        AppendLogMessage($"Record deleted: {hash}");
                    }
                }
            }
        }

        private void AutoSizeDataGridColumns(DataGrid dataGrid)
        {
            // Only process visible rows to improve performance
            const int maxRowsToProcess = 50; // Limit the number of rows to process
            var itemsSource = dataGrid.ItemsSource;
            var visibleItemCount = itemsSource is ICollection<DataRecord> collection ?
                Math.Min(collection.Count, maxRowsToProcess) : maxRowsToProcess;

            Log.Debug($"Auto-sizing columns for grid with {visibleItemCount} visible items");

            // Auto-size all columns based on content
            foreach (var column in dataGrid.Columns)
            {
                if (column is DataGridTextColumn textColumn)
                {
                    // First, size to header
                    textColumn.Width = DataGridLength.SizeToHeader;

                    // For the ScanDate column, set a fixed width
                    if (textColumn.Header.ToString() == "Scan Date")
                    {
                        textColumn.Width = new DataGridLength(150);
                        continue; // Skip further processing for this column
                    }

                    // Only size to cells if the column is not too wide already
                    if (textColumn.ActualWidth < 300) // Skip very wide columns
                    {
                        // Then size to cells if content is wider
                        textColumn.Width = DataGridLength.SizeToCells;
                    }

                    // Finally, set to auto with a minimum width
                    textColumn.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
                    textColumn.MinWidth = 50; // Set minimum width
                }
            }
        }

        private void NewProfileButton_Click(object sender, RoutedEventArgs e)
        {
            var editorWindow = new ProfileEditorWindow();
            if (editorWindow.ShowDialog() == true)
            {
                var newProfile = editorWindow.Profile;
                if (GameProfileManager != null)
                {
                    GameProfileManager.SaveProfile(newProfile);
                    LoadProfilesIntoView();

                    // If this is the first profile, set it as active
                    if (GameProfileManager.Profiles.Count == 1)
                    {
                        GameProfileManager.SetActiveProfile(newProfile);
                        SetupDynamicUI(newProfile);
                        AppendLogMessage($"New profile '{newProfile.ProfileName}' created and set as active.");
                    }
                    else
                    {
                        AppendLogMessage($"New profile '{newProfile.ProfileName}' created.");
                    }
                }
            }
        }

        private void EditProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profilesListBox.SelectedItem is GameProfile selectedProfile)
            {
                // Create a deep copy of the profile for editing
                var json = JsonConvert.SerializeObject(selectedProfile);
                var profileToEdit = JsonConvert.DeserializeObject<GameProfile>(json);

                if (profileToEdit == null) { return; }

                var editorWindow = new ProfileEditorWindow(profileToEdit);

                // Check if this is the active profile
                bool isActiveProfile = GameProfileManager?.ActiveProfile?.ProfileName == selectedProfile.ProfileName;

                if (editorWindow.ShowDialog() == true)
                {
                    if (GameProfileManager != null)
                    {
                        GameProfileManager.SaveProfile(editorWindow.Profile);
                        LoadProfilesIntoView();

                        // If we edited the active profile, update the UI
                        if (isActiveProfile)
                        {
                            // Update the active profile reference
                            GameProfileManager.SetActiveProfile(editorWindow.Profile);
                            SetupDynamicUI(editorWindow.Profile);

                            // IMPORTANT: If scanner is currently scanning, we need to restart it with the updated profile
                            if (isScanning && scanner != null && GameProfileManager.ActiveProfile != null)
                            {
                                scanner.StopScanning();
                                System.Threading.Thread.Sleep(100); // Give it time to stop
                                scanner.StartScanning(GameProfileManager.ActiveProfile);
                            }

                            AppendLogMessage($"Active profile '{editorWindow.Profile.ProfileName}' updated and UI refreshed.");
                        }
                        else
                        {
                            AppendLogMessage($"Profile '{editorWindow.Profile.ProfileName}' updated.");
                        }
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a profile to edit.", "No Profile Selected");
            }
        }

        private void DeleteProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profilesListBox.SelectedItem is GameProfile selectedProfile)
            {
                var result = MessageBox.Show($"Are you sure you want to delete the profile '{selectedProfile.ProfileName}'?", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    if (GameProfileManager != null)
                    {
                        GameProfileManager.DeleteProfile(selectedProfile);
                        LoadProfilesIntoView(); // Refresh the list box
                        AppendLogMessage($"Profile '{selectedProfile.ProfileName}' has been deleted.");
                    }
                }
            }
            else
            {
                MessageBox.Show("Please select a profile to delete.", "No Profile Selected");
            }
        }

        private void SetActiveProfileButton_Click(object sender, RoutedEventArgs e)
        {
            if (profilesListBox.SelectedItem is GameProfile selectedProfile)
            {
                if (GameProfileManager != null && dataManager != null)
                {
                    GameProfileManager.SetActiveProfile(selectedProfile);
                    dataManager.LoadDataRecordsWithProfile(selectedProfile);
                    SetupDynamicUI(selectedProfile);
                    SetupDynamicDataUI(selectedProfile);
                    Log.Information($"Activated profile: {selectedProfile.ProfileName}");

                    // Refresh the profiles list to update visual state
                    profilesListBox.Items.Refresh();
                }
            }
            else
            {
                MessageBox.Show("Please select a profile to activate.");
            }
        }

        // Helper to sanitize field names for WPF Name property
        private string SanitizeNameForWpf(string name)
        {
            // Replace spaces and invalid characters with underscores
            var sanitized = System.Text.RegularExpressions.Regex.Replace(name, @"[^a-zA-Z0-9_]", "_");
            // Ensure it starts with a letter or underscore
            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }
            return sanitized;
        }

        private void SetupDynamicUI(GameProfile profile)
        {
            // Clear the TabControl instead of Grid
            scanTabControl.Items.Clear();

            // Reset UI element tracking
            fieldTextBoxes.Clear();
            fieldImages.Clear();

            if (profile == null) return;

            Title = $"Blackout Scanner - {profile.ProfileName}";

            // Create a tab for each category
            foreach (var category in profile.Categories)
            {
                // Create a new TabItem for this category
                var tabItem = new TabItem
                {
                    Header = category.Name
                };

                // Create a grid for the fields in this category
                var categoryGrid = new Grid
                {
                    Margin = new Thickness(10)
                };

                // Define columns for the grid
                categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) }); // Label
                categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Image
                categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // TextBox
                categoryGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Save Button

                int rowIndex = 0;
                foreach (var field in category.Fields)
                {
                    // Add a row for this field
                    categoryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                    // Create the label
                    var label = new TextBlock
                    {
                        Text = field.Name + ":",
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(0, 5, 5, 5)
                    };

                    // Create the image control
                    var sanitizedName = SanitizeNameForWpf(field.Name);
                    var image = new WinImage
                    {
                        Name = sanitizedName + "Image",
                        Margin = new Thickness(5),
                        Height = 25,
                        Stretch = System.Windows.Media.Stretch.Uniform
                    };

                    // Create the text box
                    var textBox = new TextBox
                    {
                        Name = sanitizedName + "TextBox",
                        IsReadOnly = false,
                        Margin = new Thickness(5),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // Create the save button
                    var button = new Button
                    {
                        Content = "Save",
                        Margin = new Thickness(5),
                        Tag = field.Name // Store field name in Tag (original, not sanitized)
                    };
                    button.Click += SaveField_Click;

                    // Add controls to the grid
                    Grid.SetRow(label, rowIndex);
                    Grid.SetColumn(label, 0);
                    categoryGrid.Children.Add(label);

                    Grid.SetRow(image, rowIndex);
                    Grid.SetColumn(image, 1);
                    categoryGrid.Children.Add(image);

                    Grid.SetRow(textBox, rowIndex);
                    Grid.SetColumn(textBox, 2);
                    categoryGrid.Children.Add(textBox);

                    Grid.SetRow(button, rowIndex);
                    Grid.SetColumn(button, 3);
                    categoryGrid.Children.Add(button);

                    // Store references for easy access
                    fieldImages[field.Name] = image;
                    fieldTextBoxes[field.Name] = textBox;

                    rowIndex++;
                }

                // Add the grid to the tab
                tabItem.Content = categoryGrid;

                // Add the tab to the TabControl
                scanTabControl.Items.Add(tabItem);
            }

            // Select the first tab if available
            if (scanTabControl.Items.Count > 0)
            {
                scanTabControl.SelectedIndex = 0;
            }
        }

        private void SubscribeToScannerEvents()
        {
            scanner.DataUpdated += HandleDataUpdated;
            scanner.ImageUpdated += HandleImageUpdated;
            scanner.ScanDateUpdated += UpdateScanDate;
            scanner.CategoryScanning += HandleCategoryScanning;
        }

        private void HandleCategoryScanning(string categoryName)
        {
            Dispatcher.Invoke(() =>
            {
                // Check if the current selected tab already matches the category
                if (scanTabControl.SelectedItem is TabItem currentTab &&
                    currentTab.Header?.ToString() == categoryName)
                {
                    // Tab is already selected, no need to switch
                    return;
                }

                // Find the tab with matching category name
                foreach (TabItem tab in scanTabControl.Items)
                {
                    if (tab.Header?.ToString() == categoryName)
                    {
                        scanTabControl.SelectedItem = tab;
                        Log.Information($"Switched to category tab: {categoryName}");
                        break;
                    }
                }
            });
        }

        // The HandleDataPersisted method has been replaced by the updated HandleDataUpdated method
        // and is no longer needed as the Scanner directly calls AddOrUpdateRecord and fires DataUpdated events.

        private void HandleDataUpdated(Dictionary<string, object> data)
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Show update indicator
                    if (FindName("dataUpdateIndicator") is TextBlock dataUpdateIndicator)
                    {
                        dataUpdateIndicator.Visibility = Visibility.Visible;
                        dataUpdateIndicator.Text = "● Updating...";
                        dataUpdateIndicator.Foreground = new SolidColorBrush(Colors.Orange);
                    }

                    if (GameProfileManager?.ActiveProfile != null && dataManager != null)
                    {
                        // Create temporary DataRecord to get category info
                        var tempRecord = new DataRecord
                        {
                            Fields = new Dictionary<string, object>(data),
                            ScanDate = DateTime.UtcNow,
                            GameProfile = GameProfileManager.ActiveProfile.ProfileName
                        };

                        currentRecordHash = dataManager.GenerateDataHash(tempRecord, GameProfileManager.ActiveProfile);

                        // Find which category this data belongs to by checking the visible fields
                        string? detectedCategory = null;
                        foreach (var category in GameProfileManager.ActiveProfile.Categories)
                        {
                            // Check if all fields from this category are present in the data
                            bool allFieldsMatch = category.Fields.All(field =>
                                data.ContainsKey(field.Name));

                            if (allFieldsMatch && category.Fields.Count > 0)
                            {
                                detectedCategory = category.Name;
                                break;
                            }
                        }

                        if (!string.IsNullOrEmpty(detectedCategory))
                        {
                            tempRecord.Category = detectedCategory;

                            // Add or update the record
                            dataManager.AddOrUpdateRecord(tempRecord, GameProfileManager.ActiveProfile);

                            // Update the ObservableCollection for real-time UI updates
                            if (categoryCollections.TryGetValue(detectedCategory, out var collection))
                            {
                                var hash = dataManager.GenerateDataHash(tempRecord, GameProfileManager.ActiveProfile);

                                // Find existing record in the collection
                                var existingRecord = collection.FirstOrDefault(r =>
                                    dataManager.GenerateDataHash(r, GameProfileManager.ActiveProfile) == hash);

                                if (existingRecord != null)
                                {
                                    // Update existing record - copy all fields
                                    foreach (var field in tempRecord.Fields)
                                    {
                                        existingRecord.Fields[field.Key] = field.Value;
                                    }
                                    existingRecord.ScanDate = tempRecord.ScanDate;

                                    // Force UI refresh by removing and re-adding
                                    var index = collection.IndexOf(existingRecord);
                                    collection.RemoveAt(index);
                                    collection.Insert(index, existingRecord);
                                }
                                else
                                {
                                    // Add new record to the collection
                                    collection.Insert(0, tempRecord);
                                }

                                // Mark as having unsaved changes
                                dataManager.MarkAsUnsaved();
                                UpdateUnsavedIndicator();

                                // Find the DataGrid for this category to schedule auto-sizing
                                if (FindName("dataTabControl") is TabControl dataTabControl)
                                {
                                    // Check if we're already on the correct tab
                                    bool alreadyOnCorrectTab = false;
                                    if (dataTabControl.SelectedItem is TabItem currentTab &&
                                        currentTab.Header?.ToString() == detectedCategory)
                                    {
                                        alreadyOnCorrectTab = true;
                                        // We're already on the correct tab, still need to get the DataGrid for resizing
                                        if (currentTab.Content is DataGrid dataGrid)
                                        {
                                            // Schedule a throttled resize
                                            if (!dataGridsToResize.Contains(dataGrid))
                                            {
                                                dataGridsToResize.Add(dataGrid);
                                                if (!autoSizeTimer.IsEnabled)
                                                {
                                                    autoSizeTimer.Start();
                                                }
                                            }
                                        }
                                    }

                                    // If not on the correct tab, find and switch to it
                                    if (!alreadyOnCorrectTab)
                                    {
                                        foreach (TabItem tab in dataTabControl.Items)
                                        {
                                            if (tab.Header?.ToString() == detectedCategory)
                                            {
                                                dataTabControl.SelectedItem = tab;
                                                Log.Information($"Switched to data tab: {detectedCategory}");

                                                if (tab.Content is DataGrid dataGrid)
                                                {
                                                    // Schedule a throttled resize
                                                    if (!dataGridsToResize.Contains(dataGrid))
                                                    {
                                                        dataGridsToResize.Add(dataGrid);
                                                        if (!autoSizeTimer.IsEnabled)
                                                        {
                                                            autoSizeTimer.Start();
                                                        }
                                                    }
                                                }
                                                break;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }

                    // Update field text boxes with the new data
                    foreach (var kvp in data)
                    {
                        if (fieldTextBoxes.TryGetValue(kvp.Key, out var textBox))
                        {
                            textBox.Text = kvp.Value?.ToString() ?? string.Empty;
                        }
                    }

                    // Update the visual indicator after a delay
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(1);
                    timer.Tick += (s, e) =>
                    {
                        if (FindName("dataUpdateIndicator") is TextBlock dataUpdateIndicator)
                        {
                            dataUpdateIndicator.Text = "● Live";
                            dataUpdateIndicator.Foreground = new SolidColorBrush(Colors.Green);
                        }
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating data in UI");
                }
            });
        }

        private void HandleImageUpdated(string fieldName, BitmapImage image)
        {
            Dispatcher.Invoke(() =>
            {
                if (fieldImages.TryGetValue(fieldName, out var imageControl))
                {
                    imageControl.Source = image;
                }
            });
        }


        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            if (isScanning)
            {
                // Stop scanning
                scanner?.StopScanning();

                if (FindName("scanButton") is Button scanButton)
                {
                    scanButton.Content = "Start Scanning";
                }

                isScanning = false;
                Log.Information("Scanning stopped.");
            }
            else
            {
                if (GameProfileManager?.ActiveProfile != null && scanner != null)
                {
                    // Log profile details before starting the scan
                    var profile = GameProfileManager.ActiveProfile;
                    Log.Information($"Starting scan with profile: {profile.ProfileName}");

                    // Log key field information
                    var keyFields = new List<string>();
                    foreach (var category in profile.Categories)
                    {
                        foreach (var field in category.Fields)
                        {
                            if (field.IsKeyField)
                            {
                                keyFields.Add($"{category.Name}.{field.Name}");
                            }
                        }
                    }

                    if (keyFields.Count > 0)
                    {
                        Log.Information($"Profile has {keyFields.Count} key fields: {string.Join(", ", keyFields)}");
                    }
                    else
                    {
                        Log.Warning("Profile has no key fields defined. Records cannot be uniquely identified.");
                    }

                    // Start scanning - always use the latest active profile from gameProfileManager
                    scanner.StartScanning(profile);

                    if (FindName("scanButton") is Button scanButton)
                    {
                        scanButton.Content = "Stop Scanning";
                    }

                    isScanning = true;
                    Log.Information("Scanning started...");
                }
                else
                {
                    MessageBox.Show("Please set an active game profile before scanning.", "No Active Profile");
                }
            }
        }

        private void SaveField_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string fieldName)
            {
                if (fieldImages.TryGetValue(fieldName, out var imageControl) && imageControl.Source is BitmapImage bitmapImage)
                {
                    var correctedText = fieldTextBoxes[fieldName].Text;
                    if (ocrProcessor != null)
                    {
                        var imageHash = ocrProcessor.GenerateImageHash(bitmapImage);

                        ocrProcessor.UpdateCacheResult(imageHash, correctedText);
                        Log.Information($"Saved corrected value for '{fieldName}': '{correctedText}' with image hash '{imageHash}' to OCR cache.");
                    }

                    // Now, update the data in memory
                    if (!string.IsNullOrEmpty(currentRecordHash) && dataManager?.DataRecordDictionary.TryGetValue(currentRecordHash, out var recordToUpdate) == true)
                    {
                        recordToUpdate.Fields[fieldName] = correctedText;
                        Log.Information($"Updated '{fieldName}' for record with hash '{currentRecordHash}' in memory.");
                    }
                }
            }
        }

        private void UpdateScanDate(DateTime scanDate)
        {
            Dispatcher.Invoke(() =>
            {
                if (FindName("scanDateTextBox") is TextBox scanDateTextBox)
                {
                    scanDateTextBox.Text = scanDate.ToString("g");
                }
            });
        }

        private Dictionary<string, OCRResult> LoadOcrResultsCache()
        {
            if (File.Exists(cacheFilePath))
            {
                var json = File.ReadAllText(cacheFilePath);
                return JsonConvert.DeserializeObject<Dictionary<string, OCRResult>>(json) ?? new Dictionary<string, OCRResult>();
            }
            return new Dictionary<string, OCRResult>();
        }

        private void SaveOcrResultsCache()
        {
            if (ocrProcessor != null)
            {
                var cacheData = ocrProcessor.ExportCache();
                var json = JsonConvert.SerializeObject(cacheData, Formatting.Indented);
                File.WriteAllText(cacheFilePath, json);
            }
        }

        private void LoadDebugSettings()
        {
            // Nothing to do here for now - we'll use the default values already set in the fields
            Log.Debug($"Debug settings initialized: saveImages={saveDebugImages}, verbose={verboseLogging}, folder={debugImagesFolder}");
        }

        private void SaveDebugSettings()
        {
            try
            {
                // Ensure the debug images folder exists
                if (saveDebugImages && !string.IsNullOrEmpty(debugImagesFolder))
                {
                    Directory.CreateDirectory(debugImagesFolder);
                }

                // Update the Scanner's debug settings
                if (scanner != null)
                {
                    scanner.UpdateDebugSettings(saveDebugImages, verboseLogging, debugImagesFolder);
                }

                Log.Information($"Debug settings updated: saveImages={saveDebugImages}, verbose={verboseLogging}, folder={debugImagesFolder}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save debug settings");
            }
        }

        private void DebugSettings_Changed(object sender, RoutedEventArgs e)
        {
            // Since we can't access the UI controls directly yet, we'll manually toggle the values
            // This method will be called from the UI button click
            if (sender is CheckBox checkBox)
            {
                if (checkBox.Content?.ToString() == "Save Debug Images")
                {
                    saveDebugImages = checkBox.IsChecked ?? false;
                }
                else if (checkBox.Content?.ToString() == "Verbose Logging")
                {
                    verboseLogging = checkBox.IsChecked ?? false;
                }

                SaveDebugSettings();
            }
        }

        private void BrowseDebugFolder_Click(object sender, RoutedEventArgs e)
        {
            // Creating a workaround using OpenFileDialog since WPF doesn't have a native FolderBrowserDialog
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select folder for debug images",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select this folder",
                Filter = "Folders|*.none", // This is a dummy filter
                ValidateNames = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Get the selected path without the filename
                string? selectedPath = Path.GetDirectoryName(openFileDialog.FileName);

                if (!string.IsNullOrEmpty(selectedPath))
                {
                    debugImagesFolder = selectedPath;  // Update the field directly
                    SaveDebugSettings();
                    Log.Information($"Debug images folder set to: {selectedPath}");
                }
            }
        }

        private void OpenDebugFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Ensure the folder exists
                if (!Directory.Exists(debugImagesFolder))
                {
                    Directory.CreateDirectory(debugImagesFolder);
                }

                // Open the folder in Windows Explorer
                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Path.GetFullPath(debugImagesFolder),
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to open debug folder: {debugImagesFolder}");
                MessageBox.Show($"Failed to open debug folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Ensure to unsubscribe from the event when the window is closed to prevent memory leaks
        protected override void OnClosing(CancelEventArgs e)
        {
            if (dataManager != null && dataManager.HasUnsavedChanges()) // Need to add this tracking
            {
                var result = MessageBox.Show(
                    "You have unsaved data. Do you want to save before closing?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    if (GameProfileManager?.ActiveProfile != null)
                    {
                        dataManager.SaveDataRecords(GameProfileManager.ActiveProfile);
                    }
                }
                else if (result == MessageBoxResult.Cancel)
                {
                    e.Cancel = true;
                    return;
                }
            }

            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // Stop any pending timers
            if (autoSizeTimer != null && autoSizeTimer.IsEnabled)
            {
                autoSizeTimer.Stop();
            }

            // Unsubscribe from all events
            if (scanner != null)
            {
                scanner.DataUpdated -= HandleDataUpdated;
                scanner.ImageUpdated -= HandleImageUpdated;
                scanner.ScanDateUpdated -= UpdateScanDate;
                scanner.CategoryScanning -= HandleCategoryScanning;
            }

            // Unsubscribe from UI logging
            BlackoutScanner.Infrastructure.UISink.LogMessage -= OnLogMessage;

            SaveOcrResultsCache();

            base.OnClosed(e);
        }

        private void ExportTsv_Click(object sender, RoutedEventArgs e)
        {
            if (GameProfileManager?.ActiveProfile != null && dataManager != null)
            {
                dataManager.SaveDataRecordsAsTsv(GameProfileManager.ActiveProfile);

                // Show a message with information about the exported files
                var safeProfileName = GetSafeFileName(GameProfileManager.ActiveProfile.ProfileName);
                var categories = dataManager.DataRecordDictionary.Values
                    .Select(r => r.Category)
                    .Distinct()
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                var message = $"TSV data exported successfully!\n\n";
                message += $"Profile: {GameProfileManager.ActiveProfile.ProfileName}\n";
                message += $"Categories exported: {string.Join(", ", categories)}\n\n";
                message += $"Files created:\n";

                foreach (var category in categories)
                {
                    var safeCategoryName = GetSafeFileName(category);
                    message += $"• data_records_{safeProfileName}_{safeCategoryName}.tsv\n";
                }

                MessageBox.Show(message, "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                Log.Information("TSV export completed for all categories.");
            }
            else
            {
                MessageBox.Show("No active profile set. Please set an active profile first.", "No Active Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExportJson_Click(object sender, RoutedEventArgs e)
        {
            if (GameProfileManager?.ActiveProfile != null && dataManager != null)
            {
                dataManager.SaveDataRecordsAsJson(GameProfileManager.ActiveProfile);

                var safeProfileName = GetSafeFileName(GameProfileManager.ActiveProfile.ProfileName);
                var fileName = $"data_records_{safeProfileName}.json";

                MessageBox.Show($"JSON data exported successfully!\n\nFile: {fileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                Log.Information($"JSON export completed to {fileName}");
            }
            else
            {
                MessageBox.Show("No active profile set. Please set an active profile first.", "No Active Profile", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string GetSafeFileName(string fileName)
        {
            // Remove invalid characters from filename
            var invalidChars = Path.GetInvalidFileNameChars();
            var safeFileName = string.Join("_", fileName.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));
            return safeFileName.Replace(" ", "_");
        }
    }
}