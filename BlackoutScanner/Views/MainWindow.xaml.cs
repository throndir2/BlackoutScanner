using BlackoutScanner.Interfaces;
using BlackoutScanner.Infrastructure;
using BlackoutScanner.Models;
using BlackoutScanner.Services;
using BlackoutScanner.Utilities;
using BlackoutScanner.ViewModels;
using Newtonsoft.Json;
using Serilog;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinImage = System.Windows.Controls.Image;
using Microsoft.Win32; // For SaveFileDialog/OpenFileDialog
using System.Diagnostics; // For Process.Start

namespace BlackoutScanner.Views
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private bool isScanning = false;
        private bool wasScanningBeforeEdit = false; // Track if we were scanning before editing
        private HashSet<string> fieldsBeingEdited = new HashSet<string>(); // Track which fields are being edited
        private Dictionary<string, string> lastFieldValues = new Dictionary<string, string>(); // Track last values to prevent unnecessary updates
        private IScanner? scanner;
        private IDataManager? dataManager;
        private IOCRProcessor? ocrProcessor;
        private IGameProfileManager? _gameProfileManager;
        private ISettingsManager? _settingsManager;
        private IHotKeyManager? _hotKeyManager;
        private IAIQueueProcessor? _aiQueueProcessor;
        private AIQueueMonitorViewModel? _aiQueueMonitorViewModel;
        private bool _isCapturingHotKey = false;
        private bool _isShuttingDown = false; public IGameProfileManager? GameProfileManager
        {
            get => _gameProfileManager;
            private set
            {
                _gameProfileManager = value;
                OnPropertyChanged();
            }
        }

        public ISettingsManager? SettingsManager
        {
            get => _settingsManager;
            private set
            {
                _settingsManager = value;
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
        private string logLevel = "Information";
        private string debugImagesFolder = "DebugImages";

        private const string tessdataDirectory = "tessdata";
        private readonly string[] SupportedLanguages = { "eng", "kor", "jpn", "chi_sim", "chi_tra" };
        private readonly string cacheFilePath = "ocrCache.json";
        private Dictionary<string, List<LanguageInfo>>? _languagesByScript;
        private bool _isInitializingLanguages = false;

        private Dictionary<string, TextBox> fieldTextBoxes = new Dictionary<string, TextBox>();
        private Dictionary<string, WinImage> fieldImages = new Dictionary<string, WinImage>();
        private string? currentRecordHash = null;

        // Dictionary to store ObservableCollections for each category
        private Dictionary<string, ObservableCollection<DataRecord>> categoryCollections = new Dictionary<string, ObservableCollection<DataRecord>>();

        // Add these new fields to store the original key values
        private Dictionary<DataRecord, Dictionary<string, object>> originalKeyValues = new Dictionary<DataRecord, Dictionary<string, object>>();

        // Throttling for UI updates to prevent excessive operations
        private System.Windows.Threading.DispatcherTimer autoSizeTimer;

        // Method to handle log level selection changes
        private void LogLevelComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem)
            {
                var selectedLevel = selectedItem.Tag?.ToString() ?? "Information";
                Log.Information($"Log level changed to: {selectedLevel}");

                if (SettingsManager != null)
                {
                    SettingsManager.Settings.LogLevel = selectedLevel;

                    // Apply log level immediately
                    if (Enum.TryParse<LogEventLevel>(selectedLevel, out var logEventLevel))
                    {
                        UISink.MinimumLevel = logEventLevel;
                        Log.Information($"UI log level set to: {logEventLevel}");
                    }

                    // Save settings to persist the change
                    SettingsManager.SaveSettings();
                }
            }
        }
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

            // Set the window title with version information
            var version = Utilities.VersionInfo.GetProductVersion();
            this.Title = $"Blackout Scanner v{version}";

            // Set up logging for UI
            SetupLoggingToUI();

            // Disable scan button until OCR is initialized
            scanButton.IsEnabled = false;

            // Add a status indicator to show OCR is loading
            AppendLogMessage("Loading profiles and initializing application...");

            // Initialize the auto-size timer for throttled column resizing
            autoSizeTimer = new System.Windows.Threading.DispatcherTimer();
            autoSizeTimer.Interval = TimeSpan.FromMilliseconds(AutoSizeThrottleMilliseconds);
            autoSizeTimer.Tick += AutoSizeTimer_Tick;

            // MainWindow_Loaded event handler will initialize the hotkey manager
            this.Loaded += MainWindow_Loaded;

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

        private void UpdateLastLogMessage(string message, int count)
        {
            System.Diagnostics.Debug.WriteLine($"[MainWindow] UpdateLastLogMessage called with count={count}");

            // Update the last line in the log area with a counter
            Dispatcher.Invoke(() =>
            {
                try
                {
                    // Find the logArea TextBox by name
                    var logTextBox = FindName("logArea") as System.Windows.Controls.TextBox;
                    if (logTextBox == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] logArea TextBox is NULL!");
                        return;
                    }

                    if (string.IsNullOrEmpty(logTextBox.Text))
                    {
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] logArea.Text is empty!");
                        return;
                    }

                    // Save current scroll position and selection
                    var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(logTextBox);
                    var wasAtBottom = IsScrolledToBottom(logTextBox);
                    var savedVerticalOffset = scrollViewer?.VerticalOffset ?? 0;
                    var savedSelectionStart = logTextBox.SelectionStart;
                    var savedSelectionLength = logTextBox.SelectionLength;
                    var hasSelection = savedSelectionLength > 0;

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] logArea found, text length={logTextBox.Text.Length}, wasAtBottom={wasAtBottom}, hasSelection={hasSelection}");

                    // Get the current text
                    var currentText = logTextBox.Text;
                    var lastNewLineIndex = currentText.LastIndexOf('\n', currentText.Length - 2); // -2 to skip trailing newline

                    if (lastNewLineIndex < 0)
                    {
                        // Only one line in the log
                        lastNewLineIndex = 0;
                    }
                    else
                    {
                        lastNewLineIndex++; // Move past the newline character
                    }

                    // Get everything before the last line
                    var beforeLastLine = currentText.Substring(0, lastNewLineIndex);

                    // Get the last line (trim the trailing newline if present)
                    var lastLine = currentText.Substring(lastNewLineIndex).TrimEnd('\r', '\n');

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Last line before update: '{lastLine.Substring(0, Math.Min(60, lastLine.Length))}...'");

                    // Remove any existing counter suffix (e.g., " [5]" or " (x5)")
                    var baseMessage = System.Text.RegularExpressions.Regex.Replace(
                        lastLine,
                        @"\s*(?:\[\d+\]|\(x\d+\))$",
                        "");

                    // Create new line with counter in bracket format [3]
                    var newLastLine = count > 1 ? $"{baseMessage} [{count}]" : baseMessage;

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] New last line: '{newLastLine.Substring(0, Math.Min(60, newLastLine.Length))}...'");

                    // Calculate the length difference (text might be shorter or longer)
                    var oldLength = currentText.Length;
                    var newText = beforeLastLine + newLastLine + "\n";
                    var lengthDiff = newText.Length - oldLength;

                    // Update the text area
                    logTextBox.Text = newText;

                    // Only restore selection if:
                    // 1. User had an active selection
                    // 2. The selection is still within valid bounds
                    // 3. The selection wasn't in the last line (which we just modified)
                    if (hasSelection &&
                        savedSelectionStart >= 0 &&
                        savedSelectionStart < lastNewLineIndex) // Selection is BEFORE the last line
                    {
                        // Selection is in the unchanged part of the text, safe to restore
                        logTextBox.SelectionStart = savedSelectionStart;
                        logTextBox.SelectionLength = Math.Min(savedSelectionLength, newText.Length - savedSelectionStart);
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] Restored selection at {savedSelectionStart}");
                    }
                    else
                    {
                        // Clear selection to prevent auto-scroll to old position
                        logTextBox.SelectionStart = 0;
                        logTextBox.SelectionLength = 0;
                        System.Diagnostics.Debug.WriteLine($"[MainWindow] Cleared selection (hasSelection={hasSelection}, inLastLine={savedSelectionStart >= lastNewLineIndex})");
                    }

                    // Restore scroll position
                    if (wasAtBottom)
                    {
                        // User was at bottom, keep them there
                        logTextBox.ScrollToEnd();
                    }
                    else
                    {
                        // User had scrolled up, restore their exact position
                        scrollViewer?.ScrollToVerticalOffset(savedVerticalOffset);
                    }

                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Successfully updated log area");
                }
                catch (Exception ex)
                {
                    // Log error but don't crash
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Error updating last log message: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Checks if the TextBox is scrolled to the bottom
        /// </summary>
        private bool IsScrolledToBottom(System.Windows.Controls.TextBox textBox)
        {
            try
            {
                // Get the vertical scroll position
                var scrollViewer = FindVisualChild<System.Windows.Controls.ScrollViewer>(textBox);
                if (scrollViewer != null)
                {
                    // Consider "at bottom" if within 10 pixels of the bottom
                    // This accounts for rounding errors and makes it more user-friendly
                    var threshold = 10.0;
                    var atBottom = scrollViewer.VerticalOffset >= (scrollViewer.ScrollableHeight - threshold);
                    return atBottom;
                }

                // Fallback: assume at bottom if we can't determine
                return true;
            }
            catch
            {
                // If we can't determine, assume at bottom (safer default)
                return true;
            }
        }

        /// <summary>
        /// Helper method to find a child element of a specific type in the visual tree
        /// </summary>
        private T? FindVisualChild<T>(System.Windows.DependencyObject parent) where T : System.Windows.DependencyObject
        {
            for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var childOfChild = FindVisualChild<T>(child);
                if (childOfChild != null)
                {
                    return childOfChild;
                }
            }
            return null;
        }

        private void SetupLoggingToUI()
        {
            // Subscribe to the static events from UISink
            BlackoutScanner.Infrastructure.UISink.LogMessage += OnLogMessage;
            BlackoutScanner.Infrastructure.UISink.UpdateLastMessage += UpdateLastLogMessage;
        }

        private void OnLogMessage(string message)
        {
            AppendLogMessage(message);
        }

        private void InitializeAppComponents()
        {
            try
            {
                Dispatcher.Invoke(() => AppendLogMessage("Initializing application components..."));

                // Get services from the ServiceLocator
                dataManager = ServiceLocator.GetService<IDataManager>();
                GameProfileManager = ServiceLocator.GetService<IGameProfileManager>();
                SettingsManager = ServiceLocator.GetService<ISettingsManager>();
                _hotKeyManager = ServiceLocator.GetService<IHotKeyManager>();
                _aiQueueProcessor = ServiceLocator.GetService<IAIQueueProcessor>();

                Log.Information($"[InitializeAppComponents] Got services, about to load settings");

                // Initialize AI Queue Monitor ViewModel
                _aiQueueMonitorViewModel = new AIQueueMonitorViewModel();
                Dispatcher.Invoke(() =>
                {
                    if (aiQueueMonitorView != null)
                    {
                        aiQueueMonitorView.DataContext = _aiQueueMonitorViewModel;
                    }
                });

                // Subscribe to AI Queue events
                if (_aiQueueProcessor != null)
                {
                    _aiQueueProcessor.ItemProcessed += OnAIQueueItemProcessed;
                    _aiQueueProcessor.Start();
                    Log.Information("AI Queue Processor started and subscribed to events");
                }

                // Load debug settings from SettingsManager instead
                LoadDebugSettingsFromManager();

                // Initialize language selection
                InitializeLanguageSelection();

                // Initialize AI settings UI
                InitializeAISettings();

                // Schedule a verification for when UI is fully loaded
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    VerifySettingsAndUISync();
                }));

                // Load profiles FIRST - this is fast
                Dispatcher.Invoke(() =>
                {
                    LoadProfilesIntoView();

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
                });

                // Initialize OCR components asynchronously AFTER profiles are loaded
                Task.Run(() =>
                {
                    try
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AppendLogMessage("Initializing OCR engines (downloading language files if needed)...");
                            if (FindName("scanButton") is Button scanBtn)
                            {
                                scanBtn.Content = "Initializing OCR...";
                            }
                        });

                        // Get OCR processor and eagerly initialize it
                        ocrProcessor = ServiceLocator.GetService<IOCRProcessor>();
                        ocrProcessor.Initialize(); // Eagerly initialize Tesseract engines

                        scanner = ServiceLocator.GetService<IScanner>();

                        // Load OCR cache
                        var cacheData = LoadOcrResultsCache();
                        ocrProcessor.LoadCache(cacheData);

                        // Update the Scanner with our debug settings - CRITICAL POINT
                        Log.Information($"[InitializeAppComponents] About to update scanner debug settings: saveDebugImages={saveDebugImages}, logLevel={logLevel}");
                        // Double-check current settings values from SettingsManager
                        if (SettingsManager != null)
                        {
                            Log.Information($"[InitializeAppComponents] Current SettingsManager.Settings.SaveDebugImages={SettingsManager.Settings.SaveDebugImages}");
                            // Re-sync our local values with SettingsManager to ensure consistency
                            saveDebugImages = SettingsManager.Settings.SaveDebugImages;
                            logLevel = SettingsManager.Settings.LogLevel;
                            Log.Information($"[InitializeAppComponents] After re-sync: saveDebugImages={saveDebugImages}, logLevel={logLevel}");
                        }

                        // Convert log level to verbose boolean for scanner
                        bool isVerbose = logLevel == "Verbose" || logLevel == "Debug";
                        scanner.UpdateDebugSettings(saveDebugImages, isVerbose, debugImagesFolder);
                        Log.Information($"[InitializeAppComponents] Scanner debug settings updated");
                        SubscribeToScannerEvents();

                        Dispatcher.Invoke(() =>
                        {
                            Log.Information("OCR engines initialized successfully.");
                            AppendLogMessage("OCR engines ready. You can now start scanning.");
                            if (FindName("scanButton") is Button scanBtn)
                            {
                                scanBtn.IsEnabled = true;
                                scanBtn.Content = "Start Scanning";
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            AppendLogMessage($"OCR initialization failed: {ex.Message}");
                            Log.Error(ex, "Failed to initialize OCR components.");
                        });
                    }
                });
            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => AppendLogMessage($"Initialization failed: {ex.Message}"));
                Log.Error(ex, "Failed to initialize application components.");
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
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                    VerticalContentAlignment = VerticalAlignment.Center // Add vertical alignment
                };

                // Add resources for TextBox and CheckBox styling
                var editingStyle = new Style(typeof(TextBox));
                editingStyle.BasedOn = Application.Current.FindResource("DataGridEditingTextBoxStyle") as Style;
                dataGrid.Resources.Add(typeof(TextBox), editingStyle);

                var checkboxStyle = new Style(typeof(CheckBox));
                checkboxStyle.BasedOn = Application.Current.FindResource("DataGridCheckBoxStyle") as Style;
                dataGrid.Resources.Add(typeof(CheckBox), checkboxStyle);

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

                    // Add confidence column for this field
                    var confidenceColumn = new DataGridTextColumn
                    {
                        Header = $"{field.Name} (%)",
                        Binding = new System.Windows.Data.Binding($"FieldConfidences[{field.Name}]")
                        {
                            StringFormat = "{0:F1}",
                            TargetNullValue = "N/A"
                        },
                        Width = DataGridLength.Auto,
                        MinWidth = 60,
                        IsReadOnly = true
                    };

                    // Style the confidence column to show low confidence in red
                    var style = new Style(typeof(DataGridCell));
                    var trigger = new DataTrigger
                    {
                        Binding = new System.Windows.Data.Binding($"FieldConfidences[{field.Name}]"),
                        Value = null
                    };
                    style.Triggers.Add(trigger);

                    // Add a style trigger for low confidence (< 70%)
                    var lowConfidenceTrigger = new DataTrigger
                    {
                        Binding = new System.Windows.Data.Binding($"FieldConfidences[{field.Name}]")
                    };
                    // We'll use a converter or direct value comparison
                    var setter = new Setter(DataGridCell.ForegroundProperty, Brushes.Red);

                    confidenceColumn.CellStyle = new Style(typeof(DataGridCell))
                    {
                        Setters =
                        {
                            new Setter(DataGridCell.ToolTipProperty, "OCR Confidence Score (0-100%)")
                        }
                    };

                    dataGrid.Columns.Add(confidenceColumn);
                }

                // Add ScanDate column
                var scanDateColumn = new DataGridTextColumn
                {
                    Header = "Scan Date (UTC)",
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

        private void LoadDataForCategory(DataGrid dataGrid, string categoryName, bool reloadAll = true)
        {
            // Create or get existing ObservableCollection for this category
            if (!categoryCollections.TryGetValue(categoryName, out var collection))
            {
                collection = new ObservableCollection<DataRecord>();
                categoryCollections[categoryName] = collection;
                reloadAll = true; // Force reload if collection didn't exist
            }

            if (reloadAll && dataManager != null)
            {
                // Use a temporary list for better performance when loading many items
                var tempList = dataManager.DataRecordDictionary.Values
                    .Where(r => r.Category == categoryName)
                    .OrderByDescending(r => r.ScanDate)
                    .ToList();

                // Batch update: Temporarily disconnect from UI to prevent multiple refreshes
                dataGrid.ItemsSource = null;
                
                collection.Clear();
                foreach (var record in tempList)
                {
                    // Cache the hash for fast lookups later
                    if (string.IsNullOrEmpty(record.CachedHash) && GameProfileManager?.ActiveProfile != null)
                    {
                        record.CachedHash = dataManager.GenerateDataHash(record, GameProfileManager.ActiveProfile);
                    }
                    collection.Add(record);
                }
                
                // Reconnect to UI once
                dataGrid.ItemsSource = collection;

                Log.Debug($"Reloaded {tempList.Count} records for category {categoryName}");
            }
            else
            {
                // Set the ItemsSource to our ObservableCollection - only needed on first load
                if (dataGrid.ItemsSource != collection)
                {
                    dataGrid.ItemsSource = collection;
                }
            }

            // Schedule column auto-sizing with throttling - but only if needed
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
                            LoadDataForCategory(dataGrid, categoryName, true);
                        }

                        AppendLogMessage($"Record deleted: {hash}");
                    }
                }
            }
        }

        // Track when columns were last auto-sized to avoid excessive resizing
        private Dictionary<DataGrid, DateTime> lastAutoSizeTime = new Dictionary<DataGrid, DateTime>();
        private const int AutoSizeMinIntervalMs = 2000; // Minimum 2 seconds between auto-sizes per grid

        private void AutoSizeDataGridColumns(DataGrid dataGrid)
        {
            // Check if we've recently auto-sized this grid
            if (lastAutoSizeTime.TryGetValue(dataGrid, out var lastTime))
            {
                if ((DateTime.UtcNow - lastTime).TotalMilliseconds < AutoSizeMinIntervalMs)
                {
                    Log.Debug($"Skipping auto-size for grid - too soon since last resize");
                    return;
                }
            }
            
            lastAutoSizeTime[dataGrid] = DateTime.UtcNow;
            
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
                    if (textColumn.Header.ToString() == "Scan Date (UTC)")
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

                        // IMPORTANT: Load data records and set up the Data tab UI
                        if (dataManager != null)
                        {
                            dataManager.LoadDataRecordsWithProfile(newProfile);
                            SetupDynamicDataUI(newProfile);
                        }

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

                        // Force reload profiles from disk to ensure all changes are properly reflected
                        GameProfileManager.LoadProfiles();
                        LoadProfilesIntoView();

                        // If we edited the active profile, update the UI
                        if (isActiveProfile)
                        {
                            // Update the active profile reference - use the refreshed instance from profiles list
                            var refreshedProfile = GameProfileManager.Profiles.FirstOrDefault(p => p.ProfileName == editorWindow.Profile.ProfileName);
                            if (refreshedProfile != null)
                            {
                                GameProfileManager.SetActiveProfile(refreshedProfile);
                                SetupDynamicUI(refreshedProfile);

                                // Reload data UI to reflect any changes in key fields
                                dataManager?.LoadDataRecordsWithProfile(refreshedProfile);
                                SetupDynamicDataUI(refreshedProfile);
                            }

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

            // Get the version and update title with both version and profile
            var version = Utilities.VersionInfo.GetProductVersion();
            Title = $"Blackout Scanner v{version} - {profile.ProfileName}";

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

                    // Add event handlers for edit tracking
                    textBox.GotFocus += TextBox_GotFocus;
                    textBox.LostFocus += TextBox_LostFocus;
                    textBox.TextChanged += TextBox_TextChanged;

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
            if (scanner != null)
            {
                scanner.DataUpdated += HandleDataUpdated;
                scanner.ImageUpdated += HandleImageUpdated;
                scanner.ScanDateUpdated += UpdateScanDate;
                scanner.CategoryScanning += HandleCategoryScanning;
            }
            else
            {
                Log.Error("Cannot subscribe to scanner events: Scanner is null");
            }
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
            // Process category detection outside UI thread
            string? detectedCategory = null;
            if (data.TryGetValue("__Category__", out var categoryObj) && categoryObj is string categoryName)
            {
                detectedCategory = categoryName;
                // Remove the special key so it doesn't get stored as a field
                data.Remove("__Category__");
            }

            // Process data record creation outside UI thread
            DataRecord? tempRecord = null;
            string? newHash = null;

            if (GameProfileManager?.ActiveProfile != null && dataManager != null && !string.IsNullOrEmpty(detectedCategory))
            {
                // Create temporary DataRecord
                tempRecord = new DataRecord
                {
                    Fields = new Dictionary<string, object>(data),
                    ScanDate = DateTime.UtcNow,
                    GameProfile = GameProfileManager.ActiveProfile.ProfileName,
                    Category = detectedCategory
                };

                // Generate hash for the new data
                newHash = dataManager.GenerateDataHash(tempRecord, GameProfileManager.ActiveProfile);

                // Add or update the record in the data manager (not UI related)
                dataManager.AddOrUpdateRecord(tempRecord, GameProfileManager.ActiveProfile);
            }

            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
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

                    // Update hash for UI interaction
                    if (newHash != null)
                    {
                        var previousHash = currentRecordHash;
                        currentRecordHash = newHash;
                    }

                    // Update UI for the specific record if needed
                    if (tempRecord != null && !string.IsNullOrEmpty(detectedCategory) && newHash != null)
                    {
                        UpdateSingleRecordInCollection(tempRecord, detectedCategory, newHash);
                    }

                    // Update field text boxes with the new data - but skip fields being edited
                    foreach (var kvp in data)
                    {
                        // Skip updating fields that are currently being edited
                        if (fieldsBeingEdited.Contains(kvp.Key))
                        {
                            continue;
                        }

                        if (fieldTextBoxes.TryGetValue(kvp.Key, out var textBox))
                        {
                            var newValue = kvp.Value?.ToString() ?? string.Empty;

                            // Only update if the value has actually changed
                            if (!lastFieldValues.TryGetValue(kvp.Key, out var lastValue) || lastValue != newValue)
                            {
                                textBox.Text = newValue;
                                lastFieldValues[kvp.Key] = newValue;
                            }
                        }
                    }

                    // Update the visual indicator after a delay
                    var timer = new System.Windows.Threading.DispatcherTimer();
                    timer.Interval = TimeSpan.FromSeconds(1);
                    timer.Tick += (s, e) =>
                    {
                        if (FindName("dataUpdateIndicator") is TextBlock indicator)
                        {
                            indicator.Text = "● Live";
                            indicator.Foreground = new SolidColorBrush(Colors.Green);
                        }
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error updating data in UI");

                    // Update indicator to show error
                    if (FindName("dataUpdateIndicator") is TextBlock errorIndicator)
                    {
                        errorIndicator.Text = "● Error";
                        errorIndicator.Foreground = new SolidColorBrush(Colors.Red);
                    }
                }
            }));
        }

        private void UpdateSingleRecordInCollection(DataRecord record, string categoryName, string hash)
        {
            if (GameProfileManager?.ActiveProfile == null || dataManager == null)
                return;

            if (categoryCollections.TryGetValue(categoryName, out var collection))
            {
                // Find existing record in the collection using cached hash for O(n) comparison instead of O(n²) hash regeneration
                int existingIndex = -1;
                DataRecord? existingRecord = null;
                
                for (int i = 0; i < collection.Count; i++)
                {
                    // Use cached hash if available, otherwise generate and cache it
                    var recordHash = collection[i].CachedHash;
                    if (string.IsNullOrEmpty(recordHash))
                    {
                        recordHash = dataManager.GenerateDataHash(collection[i], GameProfileManager.ActiveProfile);
                        collection[i].CachedHash = recordHash;
                    }
                    
                    if (recordHash == hash)
                    {
                        existingIndex = i;
                        existingRecord = collection[i];
                        break;
                    }
                }

                if (existingRecord != null && existingIndex >= 0)
                {
                    // Track if any data actually changed
                    bool dataChanged = false;
                    
                    // Update existing record - copy all fields
                    foreach (var field in record.Fields)
                    {
                        if (!existingRecord.Fields.TryGetValue(field.Key, out var existingValue) ||
                            !Equals(existingValue, field.Value))
                        {
                            existingRecord.Fields[field.Key] = field.Value;
                            dataChanged = true;
                        }
                    }

                    // Copy field confidences
                    if (record.FieldConfidences != null)
                    {
                        if (existingRecord.FieldConfidences == null)
                        {
                            existingRecord.FieldConfidences = new Dictionary<string, float>();
                            dataChanged = true;
                        }
                        foreach (var confidence in record.FieldConfidences)
                        {
                            if (!existingRecord.FieldConfidences.TryGetValue(confidence.Key, out var existingConf) ||
                                Math.Abs(existingConf - confidence.Value) > 0.001f)
                            {
                                existingRecord.FieldConfidences[confidence.Key] = confidence.Value;
                                dataChanged = true;
                            }
                        }
                    }

                    if (existingRecord.ScanDate != record.ScanDate)
                    {
                        existingRecord.ScanDate = record.ScanDate;
                        dataChanged = true;
                    }

                    // Only trigger UI refresh if data actually changed
                    if (dataChanged)
                    {
                        // Use simple in-place replacement to trigger UI update
                        collection[existingIndex] = existingRecord;
                    }
                }
                else
                {
                    // Add new record to the collection
                    record.CachedHash = hash; // Cache the hash for future lookups
                    collection.Insert(0, record);
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
                        currentTab.Header?.ToString() == categoryName)
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
                            if (tab.Header?.ToString() == categoryName)
                            {
                                dataTabControl.SelectedItem = tab;

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


        private void TextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Find the field name from our dictionary
                var fieldName = fieldTextBoxes.FirstOrDefault(x => x.Value == textBox).Key;
                if (!string.IsNullOrEmpty(fieldName))
                {
                    Log.Debug($"[TextBox_GotFocus] Field '{fieldName}' got focus. Current recordHash: {currentRecordHash ?? "null"}");
                    fieldsBeingEdited.Add(fieldName);
                    Log.Debug($"[TextBox_GotFocus] Added '{fieldName}' to fieldsBeingEdited. Count: {fieldsBeingEdited.Count}");

                    // If we're currently scanning, pause it
                    if (isScanning && scanner != null)
                    {
                        wasScanningBeforeEdit = true;
                        isScanning = false;  // Important: Set this to false AFTER setting wasScanningBeforeEdit
                        scanner.StopScanning();
                        Log.Information($"[TextBox_GotFocus] Paused scanning for editing field: {fieldName}. wasScanningBeforeEdit={wasScanningBeforeEdit}");

                        // Update UI to show paused state
                        if (FindName("scanButton") is Button scanButton)
                        {
                            scanButton.Content = "Paused (Editing)";
                        }
                    }
                    else
                    {
                        wasScanningBeforeEdit = false;
                        Log.Debug($"[TextBox_GotFocus] Not pausing scan (not currently scanning). isScanning={isScanning}, wasScanningBeforeEdit={wasScanningBeforeEdit}");
                    }
                }
                else
                {
                    Log.Warning("[TextBox_GotFocus] TextBox got focus but couldn't find associated field name");
                }
            }
        }

        private void TextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Find the field name from our dictionary
                var fieldName = fieldTextBoxes.FirstOrDefault(x => x.Value == textBox).Key;
                if (!string.IsNullOrEmpty(fieldName))
                {
                    Log.Debug($"[TextBox_LostFocus] Field '{fieldName}' lost focus. Current recordHash: {currentRecordHash ?? "null"}");

                    bool wasInEditSet = fieldsBeingEdited.Contains(fieldName);
                    fieldsBeingEdited.Remove(fieldName);

                    Log.Debug($"[TextBox_LostFocus] Removed '{fieldName}' from fieldsBeingEdited (was in set: {wasInEditSet}). Count now: {fieldsBeingEdited.Count}");

                    // If no fields are being edited and we should resume scanning
                    if (fieldsBeingEdited.Count == 0 && wasScanningBeforeEdit && !isScanning)
                    {
                        Log.Debug($"[TextBox_LostFocus] Could auto-resume scanning, but waiting for Save button click. wasScanningBeforeEdit={wasScanningBeforeEdit}, isScanning={isScanning}");
                        // Don't auto-resume here - wait for Save button click
                        // This gives user time to review their changes
                    }
                }
                else
                {
                    Log.Warning("[TextBox_LostFocus] TextBox lost focus but couldn't find associated field name");
                }
            }
        }

        private void TextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                // Mark that user has made manual changes
                var fieldName = fieldTextBoxes.FirstOrDefault(x => x.Value == textBox).Key;
                if (!string.IsNullOrEmpty(fieldName))
                {
                    // Store the manually edited value
                    lastFieldValues[fieldName] = textBox.Text;
                }
            }
        }

        private void ScanButton_Click(object sender, RoutedEventArgs e)
        {
            // Reset the edit tracking when manually starting/stopping
            wasScanningBeforeEdit = false;

            // If OCR is not initialized yet, show a message
            if (scanner == null || ocrProcessor == null)
            {
                MessageBox.Show("OCR engine is still initializing. Please wait a moment and try again.",
                                "OCR Not Ready", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (isScanning)
            {
                // Stop scanning
                scanner.StopScanning();

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
            Log.Debug($"[SaveField_Click] START - currentRecordHash: {currentRecordHash ?? "null"}, fieldsBeingEdited.Count: {fieldsBeingEdited.Count}");

            if (sender is Button button && button.Tag is string fieldName)
            {
                Log.Debug($"[SaveField_Click] Processing save for field: '{fieldName}'");

                if (fieldTextBoxes.TryGetValue(fieldName, out var textBox) &&
                    fieldImages.TryGetValue(fieldName, out var imageControl) &&
                    imageControl.Source is BitmapImage bitmapImage)
                {
                    string correctedText = textBox.Text;
                    string imageHash = string.Empty;

                    // Update the OCR cache
                    if (ocrProcessor != null)
                    {
                        imageHash = ocrProcessor.GenerateImageHash(bitmapImage);
                        ocrProcessor.UpdateCacheResult(imageHash, correctedText);
                        Log.Information($"[SaveField_Click] Saved corrected value for '{fieldName}': '{correctedText}' with image hash '{imageHash}' to OCR cache.");
                    }

                    // Update the last known value
                    lastFieldValues[fieldName] = correctedText;
                    Log.Debug($"[SaveField_Click] Updated lastFieldValues['{fieldName}'] = '{correctedText}'");

                    // Check if we have the active profile and data manager
                    if (GameProfileManager?.ActiveProfile != null && dataManager != null)
                    {
                        Log.Debug($"[SaveField_Click] Active profile: {GameProfileManager.ActiveProfile.ProfileName}, DataManager available");

                        // Build current field values from UI
                        var currentFieldValues = new Dictionary<string, object>();
                        foreach (var kvp in fieldTextBoxes)
                        {
                            currentFieldValues[kvp.Key] = kvp.Value.Text;
                        }
                        Log.Debug($"[SaveField_Click] Built currentFieldValues dictionary with {currentFieldValues.Count} fields");

                        // Find the category for this field
                        var category = GameProfileManager.ActiveProfile.Categories
                            .FirstOrDefault(c => c.Fields.Any(f => f.Name == fieldName));

                        if (category != null)
                        {
                            Log.Debug($"[SaveField_Click] Field '{fieldName}' belongs to category '{category.Name}'");

                            // Check if this field is a key field
                            var field = category.Fields.FirstOrDefault(f => f.Name == fieldName);
                            bool isKeyField = field?.IsKeyField ?? false;
                            Log.Debug($"[SaveField_Click] Field '{fieldName}' isKeyField={isKeyField}");

                            if (!string.IsNullOrEmpty(currentRecordHash))
                            {
                                Log.Debug($"[SaveField_Click] Attempting to get existing record with hash: '{currentRecordHash}'");

                                // Try to get the existing record
                                if (dataManager.DataRecordDictionary.TryGetValue(currentRecordHash, out var existingRecord))
                                {
                                    Log.Debug($"[SaveField_Click] Found existing record with hash '{currentRecordHash}'");
                                    Log.Debug($"[SaveField_Click] Record details: Category='{existingRecord.Category}', Fields={existingRecord.Fields.Count}, GameProfile='{existingRecord.GameProfile}'");

                                    if (isKeyField)
                                    {
                                        Log.Information($"[SaveField_Click] Handling key field update for '{fieldName}'");

                                        // Key field changed - need to update the hash
                                        var updatedRecord = new DataRecord
                                        {
                                            Fields = new Dictionary<string, object>(currentFieldValues),
                                            ScanDate = DateTime.UtcNow,
                                            Category = existingRecord.Category,
                                            GameProfile = existingRecord.GameProfile,
                                            EntityIndex = existingRecord.EntityIndex,
                                            GroupId = existingRecord.GroupId
                                        };
                                        Log.Debug($"[SaveField_Click] Created updated record with {updatedRecord.Fields.Count} fields");

                                        // Remove old record and add with new hash
                                        Log.Debug($"[SaveField_Click] Calling UpdateRecordKey with oldHash='{currentRecordHash}'");
                                        string oldHash = currentRecordHash;
                                        dataManager.UpdateRecordKey(currentRecordHash, updatedRecord, GameProfileManager.ActiveProfile);

                                        // Update the ObservableCollection for the Data tab
                                        if (categoryCollections.TryGetValue(category.Name, out var collection))
                                        {
                                            Log.Debug($"[SaveField_Click] Found collection for category '{category.Name}' with {collection.Count} items");

                                            // Find and remove the old record from the collection using cached hash
                                            var oldRecord = collection.FirstOrDefault(r =>
                                                r.CachedHash == oldHash || 
                                                (string.IsNullOrEmpty(r.CachedHash) && dataManager.GenerateDataHash(r, GameProfileManager.ActiveProfile) == oldHash));

                                            if (oldRecord != null)
                                            {
                                                Log.Debug($"[SaveField_Click] Found old record in collection. Removing it.");
                                                collection.Remove(oldRecord);
                                            }
                                            else
                                            {
                                                Log.Warning($"[SaveField_Click] Could not find old record with hash '{oldHash}' in collection");
                                            }

                                            // Add the updated record
                                            Log.Debug($"[SaveField_Click] Adding updated record to collection");
                                            collection.Add(updatedRecord);
                                            Log.Debug($"[SaveField_Click] Collection now has {collection.Count} items");
                                        }
                                        else
                                        {
                                            Log.Warning($"[SaveField_Click] Could not find collection for category '{category.Name}'");
                                        }

                                        // Update currentRecordHash to the new hash
                                        string newHash = dataManager.GenerateDataHash(updatedRecord, GameProfileManager.ActiveProfile);
                                        updatedRecord.CachedHash = newHash; // Cache the hash for future lookups
                                        Log.Debug($"[SaveField_Click] New hash generated: '{newHash}'");

                                        currentRecordHash = newHash;
                                        Log.Information($"[SaveField_Click] Record hash updated from '{oldHash}' to '{currentRecordHash}'");
                                    }
                                    else
                                    {
                                        // Non-key field - just update the value in place
                                        Log.Debug($"[SaveField_Click] Updating non-key field '{fieldName}' from '{existingRecord.Fields.GetValueOrDefault(fieldName)}' to '{correctedText}'");
                                        existingRecord.Fields[fieldName] = correctedText;
                                        existingRecord.ScanDate = DateTime.UtcNow;
                                        dataManager.MarkAsUnsaved();
                                        Log.Information($"[SaveField_Click] Updated non-key field '{fieldName}' for record with hash '{currentRecordHash}'");

                                        // Update the ObservableCollection to trigger UI refresh
                                        if (categoryCollections.TryGetValue(category.Name, out var collection))
                                        {
                                            Log.Debug($"[SaveField_Click] Found collection for category '{category.Name}' with {collection.Count} items");

                                            // Find the record in the collection using cached hash
                                            var recordInCollection = collection.FirstOrDefault(r =>
                                                r.CachedHash == currentRecordHash ||
                                                (string.IsNullOrEmpty(r.CachedHash) && dataManager.GenerateDataHash(r, GameProfileManager.ActiveProfile) == currentRecordHash));

                                            if (recordInCollection != null)
                                            {
                                                Log.Debug($"[SaveField_Click] Found record in collection at index {collection.IndexOf(recordInCollection)}");

                                                // Since recordInCollection is the same reference as in DataRecordDictionary,
                                                // the field is already updated. We just need to trigger a UI refresh.
                                                // Force a collection change notification by replacing the item
                                                int index = collection.IndexOf(recordInCollection);
                                                if (index >= 0)
                                                {
                                                    Log.Debug($"[SaveField_Click] Triggering UI refresh for index {index}");
                                                    // Trigger UI update without creating duplicate
                                                    collection[index] = recordInCollection;
                                                    // Note: Setting the same reference should trigger INotifyCollectionChanged
                                                }
                                            }
                                            else
                                            {
                                                Log.Warning($"[SaveField_Click] Could not find record with hash '{currentRecordHash}' in collection");
                                            }
                                        }
                                        else
                                        {
                                            Log.Warning($"[SaveField_Click] Could not find collection for category '{category.Name}'");
                                        }
                                    }

                                    UpdateUnsavedIndicator();
                                    Log.Information($"[SaveField_Click] Successfully updated field '{fieldName}' with value: {correctedText}");
                                    Log.Debug($"[SaveField_Click] Current currentRecordHash after update: {currentRecordHash ?? "null"}");
                                }
                                else
                                {
                                    Log.Warning($"[SaveField_Click] CRITICAL ERROR: Could not find record with hash '{currentRecordHash}' in DataRecordDictionary");
                                    Log.Debug($"[SaveField_Click] DataRecordDictionary contains {dataManager.DataRecordDictionary.Count} records");

                                    // Check if the hash exists but is case-different
                                    var similarHash = dataManager.DataRecordDictionary.Keys.FirstOrDefault(k =>
                                        string.Equals(k, currentRecordHash, StringComparison.OrdinalIgnoreCase));

                                    if (similarHash != null)
                                    {
                                        Log.Warning($"[SaveField_Click] Found a similar hash with case difference: '{similarHash}'");
                                    }
                                }
                            }
                            else
                            {
                                // No current record exists - this shouldn't happen during a Save operation
                                // as Save is only available after scanning has populated currentRecordHash
                                Log.Warning($"[SaveField_Click] CRITICAL ERROR: No currentRecordHash available when saving field '{fieldName}'");
                                Log.Debug($"[SaveField_Click] fieldsBeingEdited.Count={fieldsBeingEdited.Count}, isScanning={isScanning}, wasScanningBeforeEdit={wasScanningBeforeEdit}");

                                // Don't create a new record here - this is a save operation, not a create operation
                                MessageBox.Show("No active record to update. Please scan first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                            }
                        }
                    }

                    // Clear the field from being edited set
                    bool wasInSet = fieldsBeingEdited.Contains(fieldName);
                    fieldsBeingEdited.Remove(fieldName);
                    Log.Debug($"[SaveField_Click] Removed '{fieldName}' from fieldsBeingEdited (was in set: {wasInSet}). Count now: {fieldsBeingEdited.Count}");

                    // Resume scanning if it was paused for editing
                    if (wasScanningBeforeEdit && !isScanning && GameProfileManager?.ActiveProfile != null && scanner != null)
                    {
                        Log.Information($"[SaveField_Click] Resuming scanning after save. wasScanningBeforeEdit={wasScanningBeforeEdit}, isScanning={isScanning}");
                        scanner.StartScanning(GameProfileManager.ActiveProfile);
                        isScanning = true;
                        wasScanningBeforeEdit = false;

                        if (FindName("scanButton") is Button scanButton)
                        {
                            scanButton.Content = "Stop Scanning";
                        }

                        Log.Information("[SaveField_Click] Resumed scanning after saving field correction.");
                    }
                    else
                    {
                        Log.Debug($"[SaveField_Click] Not resuming scanning. wasScanningBeforeEdit={wasScanningBeforeEdit}, isScanning={isScanning}");
                        MessageBox.Show($"Field '{fieldName}' has been updated and saved.", "Field Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                else
                {
                    Log.Warning($"[SaveField_Click] Missing TextBox, ImageControl, or BitmapImage for field '{fieldName}'");
                }
            }
            else
            {
                Log.Warning("[SaveField_Click] Invalid sender or missing Tag");
            }

            Log.Debug($"[SaveField_Click] END - currentRecordHash: {currentRecordHash ?? "null"}");
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

        private void LoadDebugSettingsFromManager()
        {
            if (SettingsManager != null)
            {
                Log.Information($"[LoadDebugSettingsFromManager] Before loading - current saveDebugImages={saveDebugImages}");
                Log.Information($"[LoadDebugSettingsFromManager] SettingsManager.Settings.SaveDebugImages={SettingsManager.Settings.SaveDebugImages}");

                // Store settings values in local fields
                saveDebugImages = SettingsManager.Settings.SaveDebugImages;
                logLevel = SettingsManager.Settings.LogLevel;
                debugImagesFolder = SettingsManager.Settings.DebugImagesFolder;

                Log.Information($"[LoadDebugSettingsFromManager] Debug settings loaded from SettingsManager: saveImages={saveDebugImages}, logLevel={logLevel}, folder={debugImagesFolder}");

                // Update UI to reflect loaded settings - do this on UI thread
                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // Find the UI elements by name
                        var saveImagesCheckbox = this.FindName("saveDebugImagesCheckbox") as CheckBox;
                        var logLevelCombo = this.FindName("logLevelComboBox") as ComboBox;
                        var debugFolderTextBox = this.FindName("debugImagesFolderTextBox") as TextBox;

                        // Update checkboxes if available
                        if (saveImagesCheckbox != null)
                        {
                            // Temporarily remove event handlers to avoid triggering save while loading
                            saveImagesCheckbox.Click -= DebugSettings_Changed;
                            saveImagesCheckbox.IsChecked = saveDebugImages;
                            saveImagesCheckbox.Click += DebugSettings_Changed;
                            Log.Information($"[LoadDebugSettingsFromManager] Updated saveDebugImagesCheckbox.IsChecked={saveImagesCheckbox.IsChecked}");
                        }
                        else
                        {
                            Log.Warning("[LoadDebugSettingsFromManager] saveDebugImagesCheckbox not found, cannot update UI");
                        }

                        // Update log level combo box
                        if (logLevelCombo != null)
                        {
                            logLevelCombo.SelectionChanged -= LogLevelComboBox_SelectionChanged;

                            // Find and select the matching item
                            foreach (ComboBoxItem item in logLevelCombo.Items)
                            {
                                if (item.Tag?.ToString() == logLevel)
                                {
                                    logLevelCombo.SelectedItem = item;
                                    break;
                                }
                            }

                            logLevelCombo.SelectionChanged += LogLevelComboBox_SelectionChanged;
                            Log.Information($"[LoadDebugSettingsFromManager] Updated logLevelComboBox selection to {logLevel}");
                        }
                        else
                        {
                            Log.Warning("[LoadDebugSettingsFromManager] logLevelComboBox not found, cannot update UI");
                        }

                        // Update text box if available
                        if (debugFolderTextBox != null)
                        {
                            debugFolderTextBox.Text = debugImagesFolder;
                            Log.Information($"[LoadDebugSettingsFromManager] Updated debugImagesFolderTextBox.Text={debugFolderTextBox.Text}");
                        }
                        else
                        {
                            Log.Warning("[LoadDebugSettingsFromManager] debugImagesFolderTextBox not found, cannot update UI");
                        }

                        // Update OCR settings
                        var multiEngineCheckBox = this.FindName("useMultiEngineOCRCheckBox") as CheckBox;
                        var confidenceSlider = this.FindName("ocrConfidenceSlider") as Slider;

                        if (multiEngineCheckBox != null)
                        {
                            multiEngineCheckBox.Click -= OCRSettings_Changed;
                            multiEngineCheckBox.IsChecked = SettingsManager.Settings.UseMultiEngineOCR;
                            multiEngineCheckBox.Click += OCRSettings_Changed;
                        }

                        if (confidenceSlider != null)
                        {
                            confidenceSlider.ValueChanged -= OCRConfidenceSlider_Changed;
                            confidenceSlider.Value = SettingsManager.Settings.OCRConfidenceThreshold;
                            confidenceSlider.ValueChanged += OCRConfidenceSlider_Changed;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[LoadDebugSettingsFromManager] Error updating UI elements");
                    }
                });

                // If scanner is available, update its settings immediately
                if (scanner != null)
                {
                    // We'll convert 'false' for verbose to match the new log level system
                    bool isVerbose = logLevel == "Verbose" || logLevel == "Debug";

                    Log.Information($"[LoadDebugSettingsFromManager] Updating scanner debug settings: saveDebugImages={saveDebugImages}, isVerbose={isVerbose}");
                    scanner.UpdateDebugSettings(saveDebugImages, isVerbose, debugImagesFolder);
                }
                else
                {
                    Log.Information($"[LoadDebugSettingsFromManager] Scanner not available yet, settings will be applied when it's initialized");
                }
            }
            else
            {
                Log.Warning("[LoadDebugSettingsFromManager] SettingsManager is null, using default values");
            }
        }

        private void SaveDebugSettings()
        {
            try
            {
                Log.Information($"[SaveDebugSettings] Starting to save debug settings...");
                Log.Information($"[SaveDebugSettings] Current values: saveDebugImages={saveDebugImages}, logLevel={logLevel}, debugImagesFolder={debugImagesFolder}");

                if (SettingsManager != null)
                {
                    // Log before updating
                    Log.Information($"[SaveDebugSettings] Before update - SettingsManager.Settings.SaveDebugImages={SettingsManager.Settings.SaveDebugImages}");

                    // Update settings in the manager
                    SettingsManager.Settings.SaveDebugImages = saveDebugImages;
                    SettingsManager.Settings.LogLevel = logLevel;
                    SettingsManager.Settings.DebugImagesFolder = debugImagesFolder;

                    // Log after updating
                    Log.Information($"[SaveDebugSettings] After update - SettingsManager.Settings.SaveDebugImages={SettingsManager.Settings.SaveDebugImages}");

                    // Save to disk
                    SettingsManager.SaveSettings();
                    Log.Information("[SaveDebugSettings] Settings saved to file");
                }
                else
                {
                    Log.Warning("[SaveDebugSettings] SettingsManager is null, cannot save settings");
                }

                // Ensure the debug images folder exists
                if (saveDebugImages && !string.IsNullOrEmpty(debugImagesFolder))
                {
                    Directory.CreateDirectory(debugImagesFolder);
                }

                // Update the Scanner's debug settings
                if (scanner != null)
                {
                    // Convert log level to verbose boolean for scanner
                    bool isVerbose = logLevel == "Verbose" || logLevel == "Debug";

                    Log.Information($"[SaveDebugSettings] Updating scanner debug settings: saveDebugImages={saveDebugImages}, isVerbose={isVerbose}");
                    scanner.UpdateDebugSettings(saveDebugImages, isVerbose, debugImagesFolder);
                }
                else
                {
                    Log.Warning("[SaveDebugSettings] Scanner is null, cannot update scanner settings");
                }

                Log.Information($"[SaveDebugSettings] Debug settings updated: saveImages={saveDebugImages}, logLevel={logLevel}, folder={debugImagesFolder}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save debug settings");
            }
        }

        private void VerifySettingsAndUISync()
        {
            try
            {
                Log.Information("[MainWindow.VerifySettingsAndUISync] Verifying settings and UI sync");

                if (SettingsManager?.Settings == null)
                {
                    Log.Warning("[MainWindow.VerifySettingsAndUISync] SettingsManager or Settings is null");
                    return;
                }

                // Find the UI elements by name
                var saveImagesCheckbox = this.FindName("saveDebugImagesCheckbox") as CheckBox;

                // Check if the UI matches the settings
                if (saveImagesCheckbox != null)
                {
                    if (saveImagesCheckbox.IsChecked != SettingsManager.Settings.SaveDebugImages)
                    {
                        Log.Warning($"[MainWindow.VerifySettingsAndUISync] UI checkbox state {saveImagesCheckbox.IsChecked} " +
                                    $"doesn't match setting {SettingsManager.Settings.SaveDebugImages}");

                        // Force reload of settings to UI
                        LoadDebugSettingsFromManager();

                        // Also make sure scanner has the correct settings
                        if (scanner != null)
                        {
                            // Convert log level to verbose boolean for scanner
                            bool isVerbose = SettingsManager.Settings.LogLevel == "Verbose" ||
                                           SettingsManager.Settings.LogLevel == "Debug";

                            scanner.UpdateDebugSettings(
                                SettingsManager.Settings.SaveDebugImages,
                                isVerbose,
                                SettingsManager.Settings.DebugImagesFolder);
                        }
                    }
                    else
                    {
                        Log.Information($"[MainWindow.VerifySettingsAndUISync] UI checkbox state {saveImagesCheckbox.IsChecked} " +
                                       $"matches setting {SettingsManager.Settings.SaveDebugImages}");
                    }
                }
                else
                {
                    Log.Warning("[MainWindow.VerifySettingsAndUISync] saveDebugImagesCheckbox not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[MainWindow.VerifySettingsAndUISync] Error verifying settings and UI sync");
            }
        }

        private void BrowseExportFolder_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "Select folder for exports",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select this folder",
                Filter = "Folders|*.none",
                ValidateNames = false
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string? selectedPath = Path.GetDirectoryName(openFileDialog.FileName);
                if (!string.IsNullOrEmpty(selectedPath) && SettingsManager != null)
                {
                    SettingsManager.Settings.ExportFolder = selectedPath;
                    SettingsManager.SaveSettings(); // Auto-save immediately
                    Log.Information($"Export folder set to: {selectedPath} and settings auto-saved");
                }
            }
        }

        private void OpenExportFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (SettingsManager != null)
                {
                    var exportPath = SettingsManager.GetFullExportPath();

                    // Ensure the folder exists
                    if (!Directory.Exists(exportPath))
                    {
                        Directory.CreateDirectory(exportPath);
                    }

                    // Open the folder in Windows Explorer
                    var startInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = exportPath,
                        UseShellExecute = true
                    };
                    System.Diagnostics.Process.Start(startInfo);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open export folder");
                MessageBox.Show($"Failed to open export folder: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveSettings_Click(object sender, RoutedEventArgs e)
        {
            if (SettingsManager != null)
            {
                SettingsManager.SaveSettings();
                MessageBox.Show("Settings saved successfully!", "Settings Saved", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ResetDebugSettings()
        {
            Log.Information("[MainWindow.ResetDebugSettings] Resetting debug settings to defaults");

            if (SettingsManager?.Settings != null)
            {
                // Set to default values
                SettingsManager.Settings.SaveDebugImages = false;
                SettingsManager.Settings.LogLevel = "Information";
                SettingsManager.Settings.DebugImagesFolder = "DebugImages";

                // Save the reset settings
                SettingsManager.SaveSettings();

                // Update local fields
                saveDebugImages = false;
                logLevel = "Information";
                debugImagesFolder = "DebugImages";

                // Update UI
                LoadDebugSettingsFromManager();

                // Update scanner
                if (scanner != null)
                {
                    scanner.UpdateDebugSettings(false, false, "DebugImages");
                }

                Log.Information("[MainWindow.ResetDebugSettings] Debug settings reset complete");
            }
        }

        private void DebugSettings_Changed(object sender, RoutedEventArgs e)
        {
            // Since we can't access the UI controls directly yet, we'll manually toggle the values
            // This method will be called from the UI button click
            if (sender is CheckBox checkBox)
            {
                string settingName = checkBox.Content?.ToString() ?? "Unknown";
                bool newValue = checkBox.IsChecked ?? false;

                Log.Information($"[DebugSettings_Changed] CheckBox '{settingName}' changed to: {newValue}");
                bool settingsChanged = false;

                if (settingName == "Save Debug Images")
                {
                    Log.Information($"[DebugSettings_Changed] Changing saveDebugImages from {saveDebugImages} to {newValue}");
                    if (saveDebugImages != newValue)
                    {
                        saveDebugImages = newValue;
                        settingsChanged = true;
                    }
                }

                if (settingsChanged)
                {
                    // Save settings to disk
                    SaveDebugSettings();

                    // Also directly update the scanner if available - don't wait for next scan
                    if (scanner != null)
                    {
                        bool isVerbose = logLevel == "Verbose" || logLevel == "Debug";
                        Log.Information($"[DebugSettings_Changed] Directly updating scanner with new settings: saveDebugImages={saveDebugImages}, isVerbose={isVerbose}");
                        scanner.UpdateDebugSettings(saveDebugImages, isVerbose, debugImagesFolder);
                    }
                    else
                    {
                        Log.Warning("[DebugSettings_Changed] Scanner not available, can't update settings directly");
                    }

                    // Save all settings immediately
                    if (SettingsManager != null)
                    {
                        SettingsManager.SaveSettings();
                        Log.Information("[DebugSettings_Changed] Settings auto-saved");
                    }
                }
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

                    // Update the TextBox
                    if (FindName("debugImagesFolderTextBox") is TextBox debugFolderTextBox)
                    {
                        debugFolderTextBox.Text = selectedPath;
                    }

                    SaveDebugSettings();

                    // Auto-save all settings
                    if (SettingsManager != null)
                    {
                        SettingsManager.SaveSettings();
                        Log.Information($"Debug images folder set to: {selectedPath} and settings auto-saved");
                    }
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
        protected override async void OnClosing(CancelEventArgs e)
        {
            // Prevent multiple shutdown attempts
            if (_isShuttingDown)
            {
                base.OnClosing(e);
                return;
            }

            // Check for unsaved changes first
            if (dataManager != null && dataManager.HasUnsavedChanges())
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

            // Cancel the close temporarily so we can do async cleanup
            e.Cancel = true;
            _isShuttingDown = true;

            Log.Information("Window closing, starting graceful shutdown...");

            // Show a message to user that we're shutting down
            if (this.IsLoaded)
            {
                this.Title = "BlackoutScanner - Shutting down...";
            }

            try
            {
                // Stop scanner first
                if (scanner != null)
                {
                    Log.Information("Stopping scanner...");
                    scanner.StopScanning();
                    scanner.DataUpdated -= HandleDataUpdated;
                    scanner.ImageUpdated -= HandleImageUpdated;
                    scanner.ScanDateUpdated -= UpdateScanDate;
                    scanner.CategoryScanning -= HandleCategoryScanning;
                }

                // Stop AI Queue Processor with proper async handling
                if (_aiQueueProcessor != null)
                {
                    Log.Information("Stopping AI Queue Processor...");
                    _aiQueueProcessor.ItemProcessed -= OnAIQueueItemProcessed;

                    try
                    {
                        // Properly await the async stop operation
                        await _aiQueueProcessor.StopAsync();
                        Log.Information("AI Queue Processor stopped successfully");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Error stopping AI Queue Processor");
                    }
                }

                // Stop any pending timers
                if (autoSizeTimer != null && autoSizeTimer.IsEnabled)
                {
                    autoSizeTimer.Stop();
                }

                // Unsubscribe from UI logging
                BlackoutScanner.Infrastructure.UISink.LogMessage -= OnLogMessage;
                BlackoutScanner.Infrastructure.UISink.UpdateLastMessage -= UpdateLastLogMessage;

                // Dispose of the hotkey manager
                _hotKeyManager?.Dispose();

                SaveOcrResultsCache();

                Log.Information("Graceful shutdown complete, closing window...");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error during shutdown");
            }

            // Now actually close the window
            base.OnClosing(e);
            this.Close();
        }

        protected override void OnClosed(EventArgs e)
        {
            // Most cleanup is done in OnClosing, just flush logs here
            Log.Information("Application shutdown complete");
            Log.CloseAndFlush();

            base.OnClosed(e);
        }

        private void ExportTsv_Click(object sender, RoutedEventArgs e)
        {
            if (GameProfileManager?.ActiveProfile != null && dataManager != null && SettingsManager != null)
            {
                // Pass the export path to the save method
                var exportPath = SettingsManager.GetFullExportPath();
                dataManager.SaveDataRecordsAsTsv(GameProfileManager.ActiveProfile, exportPath);

                // Show a message with information about the exported files
                var safeProfileName = GetSafeFileName(GameProfileManager.ActiveProfile.ProfileName);
                var categories = dataManager.DataRecordDictionary.Values
                    .Select(r => r.Category)
                    .Distinct()
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();

                var message = $"TSV data exported successfully!\n\n";
                message += $"Profile: {GameProfileManager.ActiveProfile.ProfileName}\n";
                message += $"Export folder: {exportPath}\n";
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
            if (GameProfileManager?.ActiveProfile != null && dataManager != null && SettingsManager != null)
            {
                // Pass the export path to the save method
                var exportPath = SettingsManager.GetFullExportPath();
                dataManager.SaveDataRecordsAsJson(GameProfileManager.ActiveProfile, exportPath);

                var safeProfileName = GetSafeFileName(GameProfileManager.ActiveProfile.ProfileName);
                var fileName = $"data_records_{safeProfileName}.json";

                MessageBox.Show($"JSON data exported successfully!\n\nFile: {Path.Combine(exportPath, fileName)}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Log.Debug("MainWindow_Loaded event triggered");

            // Delay the hotkey initialization to ensure everything is ready
            this.Dispatcher.BeginInvoke(new Action(() =>
            {
                InitializeHotKey();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        private void InitializeHotKey()
        {
            Log.Debug("InitializeHotKey called");

            // If hotkey manager isn't available yet, try to get it from the service locator
            if (_hotKeyManager == null)
            {
                if (ServiceLocator.IsInitialized)
                {
                    try
                    {
                        _hotKeyManager = ServiceLocator.GetService<IHotKeyManager>();
                        Log.Debug("Retrieved HotKeyManager from ServiceLocator in InitializeHotKey");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to get HotKeyManager from ServiceLocator");
                        return;
                    }
                }
                else
                {
                    Log.Debug("ServiceLocator not initialized yet, scheduling retry");
                    // Retry after a short delay
                    this.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        InitializeHotKey();
                    }), System.Windows.Threading.DispatcherPriority.Background);
                    return;
                }
            }

            if (_hotKeyManager != null)
            {
                Log.Debug("Initializing HotKeyManager with window handle");
                _hotKeyManager.Initialize(this);

                // Load and register the saved hotkey
                string hotKey = "Ctrl+Q"; // Default

                if (SettingsManager != null)
                {
                    hotKey = SettingsManager.Settings.HotKey;
                    Log.Debug($"Loading saved hotkey from settings: {hotKey}");
                }
                else
                {
                    Log.Debug("SettingsManager not available, using default hotkey");
                }

                // Update the UI
                if (this.FindName("HotKeyTextBox") is TextBox hotKeyTextBox)
                {
                    hotKeyTextBox.Text = hotKey;
                    Log.Debug($"Updated HotKeyTextBox with: {hotKey}");
                }

                // Register the hotkey
                RegisterHotKey(hotKey);
            }
            else
            {
                Log.Warning("HotKeyManager is not available. Hot keys will not work.");
            }
        }

        private void RegisterHotKey(string hotKeyString)
        {
            if (_hotKeyManager == null)
            {
                Log.Warning("Cannot register hotkey: HotKeyManager is not available");
                return;
            }

            Log.Debug($"Attempting to register hotkey: {hotKeyString}");

            if (string.IsNullOrWhiteSpace(hotKeyString))
            {
                Log.Warning("Hotkey string is empty or null, cannot register");
                return;
            }

            if (_hotKeyManager.RegisterHotKey(hotKeyString, () =>
            {
                Log.Debug($"Hotkey {hotKeyString} triggered!");

                Dispatcher.Invoke(() =>
                {
                    // Toggle scanning
                    if (isScanning)
                    {
                        Log.Debug("Hotkey stopping scan");
                        // Use ScanButton_Click to stop scanning
                        ScanButton_Click(this, new RoutedEventArgs());
                    }
                    else
                    {
                        Log.Debug("Hotkey starting scan");
                        // Use ScanButton_Click to start scanning
                        ScanButton_Click(this, new RoutedEventArgs());
                    }
                });
            }))
            {
                // Save the hotkey if registration successful
                if (SettingsManager != null)
                {
                    SettingsManager.Settings.HotKey = hotKeyString;
                    SettingsManager.SaveSettings();
                }
                Log.Information($"Successfully registered hotkey: {hotKeyString}");
            }
            else
            {
                MessageBox.Show($"Failed to register hotkey: {hotKeyString}\n\nThis might happen if another application is using this hotkey combination.",
                              "Hotkey Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                Log.Warning($"Failed to register hotkey: {hotKeyString}");
            }
        }

        private void HotKeyTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (!_isCapturingHotKey)
                return;

            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            // Ignore modifier keys alone
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                return;
            }

            // Build the hotkey string
            var hotKeyString = "";

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
                hotKeyString += "Ctrl+";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
                hotKeyString += "Alt+";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                hotKeyString += "Shift+";
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows))
                hotKeyString += "Win+";

            hotKeyString += key.ToString();

            if (sender is TextBox hotKeyTextBox)
            {
                hotKeyTextBox.Text = hotKeyString;
            }

            RegisterHotKey(hotKeyString);
        }

        private void HotKeyTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotKey = true;

            if (sender is TextBox hotKeyTextBox)
            {
                hotKeyTextBox.Text = "Press keys...";
                hotKeyTextBox.SelectAll();
            }
        }

        private void HotKeyTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            _isCapturingHotKey = false;

            if (sender is TextBox hotKeyTextBox)
            {
                // Restore the current hotkey if nothing was entered
                if (string.IsNullOrWhiteSpace(hotKeyTextBox.Text) || hotKeyTextBox.Text == "Press keys...")
                {
                    if (SettingsManager != null)
                    {
                        hotKeyTextBox.Text = SettingsManager.Settings.HotKey;
                    }
                    else
                    {
                        hotKeyTextBox.Text = "Ctrl+Q";
                    }
                }
            }
        }

        private void SetDefaultHotKey_Click(object sender, RoutedEventArgs e)
        {
            const string defaultHotKey = "Ctrl+Q";

            if (this.FindName("HotKeyTextBox") is TextBox hotKeyTextBox)
            {
                hotKeyTextBox.Text = defaultHotKey;
            }

            // If hotkey manager is not available, try to initialize it first
            if (_hotKeyManager == null)
            {
                Log.Debug("HotKeyManager not available when setting default hotkey, trying to initialize");
                InitializeHotKey();
            }
            else
            {
                RegisterHotKey(defaultHotKey);
            }
        }

        // OCR Settings Methods
        private void OCRSettings_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && SettingsManager != null)
            {
                bool useMultiEngine = checkBox.IsChecked ?? false;
                bool previousValue = SettingsManager.Settings.UseMultiEngineOCR;

                // Only reinitialize if the value actually changed
                if (previousValue != useMultiEngine)
                {
                    SettingsManager.Settings.UseMultiEngineOCR = useMultiEngine;
                    SettingsManager.SaveSettings();

                    Log.Information($"OCR mode changed to: {(useMultiEngine ? "Enhanced Accuracy (Multi-Engine)" : "Fast Processing (Single Engine)")}");

                    // Only reinitialize if OCR processor is already initialized
                    if (ocrProcessor != null)
                    {
                        // Show loading message in log area
                        AppendLogMessage($"Reinitializing OCR engine for {(useMultiEngine ? "Enhanced Accuracy" : "Fast Processing")} mode...");

                        // Disable scan button during reinitialization
                        if (FindName("scanButton") is Button scanBtn)
                        {
                            scanBtn.IsEnabled = false;
                        }

                        // Reinitialize OCR in background
                        Task.Run(() =>
                        {
                            try
                            {
                                // Dispose existing engines
                                ocrProcessor.Dispose();

                                // Reinitialize with new settings
                                ocrProcessor = new OCRProcessor(
                                    ServiceLocator.GetService<IImageProcessor>(),
                                    ServiceLocator.GetService<IFileSystem>(),
                                    SettingsManager
                                );

                                Dispatcher.InvokeAsync(() =>
                                {
                                    AppendLogMessage("OCR engine reinitialization complete.");

                                    // Re-enable scan button
                                    if (FindName("scanButton") is Button scanBtn)
                                    {
                                        scanBtn.IsEnabled = true;
                                    }
                                });
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Error reinitializing OCR engine.");
                                Dispatcher.InvokeAsync(() =>
                                {
                                    AppendLogMessage($"Error reinitializing OCR engine: {ex.Message}");

                                    // Re-enable scan button even on failure
                                    if (FindName("scanButton") is Button scanBtn)
                                    {
                                        scanBtn.IsEnabled = true;
                                    }
                                });
                            }
                        });
                    }
                    else
                    {
                        // OCR processor not initialized yet, settings will be used when it is initialized
                        Log.Information("OCR settings saved. Will be applied when OCR is initialized.");
                        AppendLogMessage("OCR settings saved. Will be applied at next scan.");
                    }
                }
            }

            if (sender is Slider slider && SettingsManager != null)
            {
                SettingsManager.Settings.OCRConfidenceThreshold = (float)slider.Value;
                SettingsManager.SaveSettings();
                Log.Information($"OCR confidence threshold set to: {slider.Value:P0}");
            }
        }

        private void OCRConfidenceSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SettingsManager != null)
            {
                SettingsManager.Settings.OCRConfidenceThreshold = (float)e.NewValue;
                SettingsManager.SaveSettings();
                Log.Debug($"OCR confidence threshold changed to: {e.NewValue}%");
            }
        }

        private void InitializeLanguageSelection()
        {
            if (SettingsManager == null) return;

            // Log the initial state
            var initialLanguages = SettingsManager.Settings.SelectedLanguages;
            Log.Information($"[InitializeLanguageSelection] START - SelectedLanguages count: {initialLanguages?.Count ?? 0}, Languages: [{string.Join(", ", initialLanguages ?? new List<string>())}]");

            // Set flag to prevent event handlers from running during initialization
            _isInitializingLanguages = true;
            Log.Information("[InitializeLanguageSelection] Set _isInitializingLanguages = true");

            try
            {
                // Get languages grouped by script
                _languagesByScript = LanguageHelper.GetLanguagesByScript();

                // Set IsSelected based on current settings
                var selectedLanguages = SettingsManager.Settings.SelectedLanguages;
                Log.Information($"[InitializeLanguageSelection] Setting IsSelected for languages based on: [{string.Join(", ", selectedLanguages)}]");

                foreach (var scriptGroup in _languagesByScript.Values)
                {
                    foreach (var lang in scriptGroup)
                    {
                        lang.IsSelected = selectedLanguages.Contains(lang.Code);
                        if (lang.IsSelected)
                        {
                            Log.Debug($"[InitializeLanguageSelection] Setting {lang.Code} IsSelected = true");
                        }
                    }
                }

                // Bind to UI
                Dispatcher.Invoke(() =>
                {
                    if (FindName("languagesList") is ItemsControl languagesList)
                    {
                        languagesList.ItemsSource = _languagesByScript;
                        Log.Information("[InitializeLanguageSelection] Bound languagesList to UI");
                    }
                });

                Log.Information($"[InitializeLanguageSelection] END - SelectedLanguages count: {SettingsManager.Settings.SelectedLanguages?.Count ?? 0}, Languages: [{string.Join(", ", SettingsManager.Settings.SelectedLanguages ?? new List<string>())}]");
            }
            finally
            {
                // Clear flag after initialization is complete
                _isInitializingLanguages = false;
                Log.Information("[InitializeLanguageSelection] Set _isInitializingLanguages = false");
            }
        }

        private async void LanguageCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            // Ignore events during initialization to prevent duplicating the language list
            if (_isInitializingLanguages)
            {
                Log.Information($"[LanguageCheckBox_Changed] BLOCKED - Event fired during initialization, ignoring");
                return;
            }

            if (sender is CheckBox checkBox && checkBox.Tag is string languageCode && SettingsManager != null)
            {
                var isChecked = checkBox.IsChecked == true;
                Log.Information($"[LanguageCheckBox_Changed] START - Language: {languageCode}, IsChecked: {isChecked}, Current SelectedLanguages count: {SettingsManager.Settings.SelectedLanguages?.Count ?? 0}");

                // Get current selected languages
                var selectedLanguages = new List<string>();
                if (_languagesByScript != null)
                {
                    foreach (var scriptGroup in _languagesByScript.Values)
                    {
                        foreach (var lang in scriptGroup.Where(l => l.IsSelected))
                        {
                            selectedLanguages.Add(lang.Code);
                        }
                    }
                }
                Log.Information($"[LanguageCheckBox_Changed] Gathered selected languages from UI: [{string.Join(", ", selectedLanguages)}]");

                // Validate at least one language is selected
                if (!isChecked && selectedLanguages.Count == 1 && selectedLanguages[0] == languageCode)
                {
                    MessageBox.Show("At least one language must be selected.", "Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    checkBox.IsChecked = true;
                    return;
                }

                // Update the language info
                LanguageInfo? langInfo = null;
                if (_languagesByScript != null)
                {
                    langInfo = _languagesByScript.Values
                        .SelectMany(v => v)
                        .FirstOrDefault(l => l.Code == languageCode);
                }

                if (langInfo != null)
                {
                    langInfo.IsSelected = isChecked;

                    // Show loading indicator
                    var originalContent = checkBox.Content;
                    checkBox.IsEnabled = false;
                    checkBox.Content = isChecked ? "Initializing..." : "Removing...";

                    try
                    {
                        // Update selected languages in settings
                        selectedLanguages = new List<string>();
                        if (_languagesByScript != null)
                        {
                            foreach (var scriptGroup in _languagesByScript.Values)
                            {
                                foreach (var lang in scriptGroup.Where(l => l.IsSelected))
                                {
                                    selectedLanguages.Add(lang.Code);
                                }
                            }
                        }

                        Log.Information($"[LanguageCheckBox_Changed] BEFORE assignment - SettingsManager.Settings.SelectedLanguages count: {SettingsManager.Settings.SelectedLanguages?.Count ?? 0}");
                        Log.Information($"[LanguageCheckBox_Changed] Assigning new list with count: {selectedLanguages.Count}, Languages: [{string.Join(", ", selectedLanguages)}]");
                        SettingsManager.Settings.SelectedLanguages = selectedLanguages;
                        Log.Information($"[LanguageCheckBox_Changed] AFTER assignment - SettingsManager.Settings.SelectedLanguages count: {SettingsManager.Settings.SelectedLanguages?.Count ?? 0}, Languages: [{string.Join(", ", SettingsManager.Settings.SelectedLanguages ?? new List<string>())}]");
                        SettingsManager.SaveSettings();

                        // Update OCR processor in background
                        await Task.Run(() =>
                        {
                            ocrProcessor?.UpdateLanguages(selectedLanguages);
                        });

                        Log.Information($"Language {languageCode} {(isChecked ? "added" : "removed")}");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, $"Failed to {(isChecked ? "add" : "remove")} language {languageCode}");
                        MessageBox.Show($"Failed to update language: {ex.Message}", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);

                        // Revert the change
                        langInfo.IsSelected = !isChecked;
                        checkBox.IsChecked = !isChecked;
                    }
                    finally
                    {
                        // Restore UI
                        checkBox.Content = originalContent;
                        checkBox.IsEnabled = true;
                    }
                }
            }
        }

        // AI Enhancement Settings Methods (Legacy UI - uses deprecated properties for backward compatibility)
#pragma warning disable CS0618 // Type or member is obsolete
        private void AISettings_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && SettingsManager != null)
            {
                var useAIEnhanced = checkBox.IsChecked ?? false;
                SettingsManager.Settings.UseAIEnhancedOCR = useAIEnhanced;

                // Update status text
                UpdateAIStatusText();

                // Save settings
                SettingsManager.SaveSettings();

                Log.Information($"AI-Enhanced OCR {(useAIEnhanced ? "enabled" : "disabled")}");
            }
        }

        private void AIProviderComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem && SettingsManager != null)
            {
                var provider = selectedItem.Tag?.ToString() ?? "None";
                SettingsManager.Settings.AIProvider = provider;

                // Show/hide provider-specific settings
                UpdateAIProviderUI(provider);

                // Update status text
                UpdateAIStatusText();

                // Save settings
                SettingsManager.SaveSettings();

                Log.Information($"AI Provider changed to: {provider}");
            }
        }

        private void UpdateAIProviderUI(string provider)
        {
            Dispatcher.Invoke(() =>
            {
                // Hide all provider settings first
                if (FindName("nvidiaSettingsGroup") is GroupBox nvidiaGroup)
                {
                    nvidiaGroup.Visibility = Visibility.Collapsed;
                }

                // Show the selected provider's settings
                switch (provider)
                {
                    case "NvidiaBuild":
                        if (FindName("nvidiaSettingsGroup") is GroupBox nvidiaSettings)
                        {
                            nvidiaSettings.Visibility = Visibility.Visible;

                            // Load saved API key if exists
                            if (FindName("nvidiaApiKeyBox") is PasswordBox apiKeyBox && SettingsManager != null)
                            {
                                apiKeyBox.Password = SettingsManager.Settings.NvidiaApiKey;
                            }
                        }
                        break;
                        // Future providers will be added here
                }
            });
        }

        private void NvidiaApiKey_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox && SettingsManager != null)
            {
                SettingsManager.Settings.NvidiaApiKey = passwordBox.Password;
                SettingsManager.SaveSettings();

                // Update status text
                UpdateAIStatusText();

                // Enable/disable test button based on API key presence
                if (FindName("testNvidiaConnectionButton") is Button testButton)
                {
                    testButton.IsEnabled = !string.IsNullOrWhiteSpace(passwordBox.Password);
                }
            }
        }

        private void NvidiaModel_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (sender is ComboBox comboBox && comboBox.SelectedItem is ComboBoxItem selectedItem && SettingsManager != null)
            {
                var model = selectedItem.Tag?.ToString() ?? "baidu/paddleocr";
                SettingsManager.Settings.NvidiaModel = model;
                SettingsManager.SaveSettings();

                Log.Information($"NVIDIA model changed to: {model}");
            }
        }

        private async void TestNvidiaConnection_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button)
            {
                button.IsEnabled = false;
                button.Content = "Testing...";

                try
                {
                    // Validate that we have the required settings
                    if (SettingsManager == null || string.IsNullOrWhiteSpace(SettingsManager.Settings.NvidiaApiKey))
                    {
                        MessageBox.Show(
                            "Please enter an API key before testing the connection.",
                            "Missing API Key",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    // Get the NVIDIA AI service and test the connection
                    var nvidiaService = ServiceLocator.GetService<INvidiaOCRService>();

                    // Update service configuration with current settings
                    nvidiaService.UpdateConfiguration(
                        SettingsManager.Settings.NvidiaApiKey,
                        SettingsManager.Settings.NvidiaModel);

                    // Test the connection
                    Log.Information("Testing NVIDIA Build API connection...");
                    bool isSuccessful = await nvidiaService.TestConnectionAsync();

                    if (isSuccessful)
                    {
                        MessageBox.Show(
                            $"Connection successful!\n\nProvider: NVIDIA Build\nModel: {SettingsManager.Settings.NvidiaModel}\n\nThe AI service is ready to use.",
                            "Connection Test Passed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);

                        Log.Information("NVIDIA Build API connection test passed");
                    }
                    else
                    {
                        MessageBox.Show(
                            "Connection test failed. Please check your API key and try again.\n\nCheck the log for more details.",
                            "Connection Test Failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);

                        Log.Warning("NVIDIA Build API connection test failed");
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        $"Error testing connection:\n\n{ex.Message}\n\nPlease check your API key and network connection.",
                        "Connection Test Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    Log.Error(ex, "Failed to test NVIDIA Build connection");
                }
                finally
                {
                    button.IsEnabled = true;
                    button.Content = "Test Connection";
                }
            }
        }

        private void UpdateAIStatusText()
        {
            Dispatcher.Invoke(() =>
            {
                if (FindName("aiStatusText") is TextBlock statusText && SettingsManager != null)
                {
                    if (!SettingsManager.Settings.UseAIEnhancedOCR)
                    {
                        statusText.Text = "AI enhancement is disabled";
                        statusText.Foreground = new SolidColorBrush(Colors.Gray);
                    }
                    else if (SettingsManager.Settings.AIProvider == "None")
                    {
                        statusText.Text = "AI enhancement is enabled but no provider is selected";
                        statusText.Foreground = new SolidColorBrush(Colors.Orange);
                    }
                    else if (SettingsManager.Settings.AIProvider == "NvidiaBuild")
                    {
                        if (string.IsNullOrWhiteSpace(SettingsManager.Settings.NvidiaApiKey))
                        {
                            statusText.Text = "NVIDIA Build selected but API key is missing";
                            statusText.Foreground = new SolidColorBrush(Colors.Orange);
                        }
                        else
                        {
                            statusText.Text = $"NVIDIA Build configured with model: {SettingsManager.Settings.NvidiaModel}";
                            statusText.Foreground = new SolidColorBrush(Colors.Green);
                        }
                    }
                }
            });
        }
#pragma warning restore CS0618 // Type or member is obsolete

        // AI Provider Management Methods
        private void AddAIProvider_Click(object sender, RoutedEventArgs e)
        {
            // Pass the next available priority (count + 1) to the dialog
            var nextPriority = (SettingsManager?.Settings.AIProviders.Count ?? 0) + 1;
            var dialog = new AIProviderEditorDialog(null, nextPriority);
            if (dialog.ShowDialog() == true && dialog.Result != null)
            {
                SettingsManager?.Settings.AIProviders.Add(dialog.Result);
                ReorderAndNormalizeProviderPriorities();
                SettingsManager?.SaveSettings();
                Log.Information($"Added new AI provider: {dialog.Result.DisplayName}");
            }
        }

        private void EditAIProvider_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("aiProvidersDataGrid") is DataGrid dataGrid &&
                dataGrid.SelectedItem is AIProviderConfiguration selectedProvider)
            {
                var dialog = new AIProviderEditorDialog(selectedProvider);
                if (dialog.ShowDialog() == true && dialog.Result != null)
                {
                    // Find and replace the provider
                    var index = SettingsManager?.Settings.AIProviders.IndexOf(selectedProvider) ?? -1;
                    if (index >= 0 && SettingsManager != null)
                    {
                        SettingsManager.Settings.AIProviders[index] = dialog.Result;
                        ReorderAndNormalizeProviderPriorities();
                        SettingsManager.SaveSettings();
                        
                        // Re-select the edited provider
                        dataGrid.SelectedItem = dialog.Result;
                        Log.Information($"Updated AI provider: {dialog.Result.DisplayName}");
                    }
                }
            }
        }

        private void DeleteAIProvider_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("aiProvidersDataGrid") is DataGrid dataGrid &&
                dataGrid.SelectedItem is AIProviderConfiguration selectedProvider)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete '{selectedProvider.DisplayName}'?",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    SettingsManager?.Settings.AIProviders.Remove(selectedProvider);
                    ReorderAndNormalizeProviderPriorities();
                    SettingsManager?.SaveSettings();
                    Log.Information($"Deleted AI provider: {selectedProvider.DisplayName}");
                }
            }
        }

        /// <summary>
        /// Reorders the AI providers list by their priority values and normalizes
        /// priorities to sequential numbers (1, 2, 3, ...) to avoid duplicates.
        /// </summary>
        private void ReorderAndNormalizeProviderPriorities()
        {
            if (SettingsManager == null) return;

            var providers = SettingsManager.Settings.AIProviders;
            if (providers.Count == 0) return;

            // Sort by priority, then by display name for stable ordering when priorities are equal
            var sorted = providers.OrderBy(p => p.Priority).ThenBy(p => p.DisplayName).ToList();

            // Clear and re-add in sorted order with normalized priorities
            providers.Clear();
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Priority = i + 1; // Normalize to 1, 2, 3, ...
                providers.Add(sorted[i]);
            }

            Log.Debug($"Normalized AI provider priorities: {string.Join(", ", providers.Select(p => $"{p.DisplayName}={p.Priority}"))}");
        }

        private void MoveProviderUp_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("aiProvidersDataGrid") is DataGrid dataGrid &&
                dataGrid.SelectedItem is AIProviderConfiguration selectedProvider &&
                SettingsManager != null)
            {
                var providers = SettingsManager.Settings.AIProviders;
                var index = providers.IndexOf(selectedProvider);

                if (index > 0)
                {
                    // Swap priorities
                    var temp = providers[index - 1].Priority;
                    providers[index - 1].Priority = selectedProvider.Priority;
                    selectedProvider.Priority = temp;

                    // Re-sort the collection
                    var sorted = providers.OrderBy(p => p.Priority).ToList();
                    providers.Clear();
                    foreach (var provider in sorted)
                    {
                        providers.Add(provider);
                    }

                    SettingsManager.SaveSettings();
                    dataGrid.SelectedItem = selectedProvider;
                    Log.Information($"Moved provider '{selectedProvider.DisplayName}' up");
                }
            }
        }

        private void MoveProviderDown_Click(object sender, RoutedEventArgs e)
        {
            if (FindName("aiProvidersDataGrid") is DataGrid dataGrid &&
                dataGrid.SelectedItem is AIProviderConfiguration selectedProvider &&
                SettingsManager != null)
            {
                var providers = SettingsManager.Settings.AIProviders;
                var index = providers.IndexOf(selectedProvider);

                if (index < providers.Count - 1 && index >= 0)
                {
                    // Swap priorities
                    var temp = providers[index + 1].Priority;
                    providers[index + 1].Priority = selectedProvider.Priority;
                    selectedProvider.Priority = temp;

                    // Re-sort the collection
                    var sorted = providers.OrderBy(p => p.Priority).ToList();
                    providers.Clear();
                    foreach (var provider in sorted)
                    {
                        providers.Add(provider);
                    }

                    SettingsManager.SaveSettings();
                    dataGrid.SelectedItem = selectedProvider;
                    Log.Information($"Moved provider '{selectedProvider.DisplayName}' down");
                }
            }
        }

#pragma warning disable CS0618 // Type or member is obsolete
        private void InitializeAISettings()
        {
            Dispatcher.Invoke(() =>
            {
                if (SettingsManager != null)
                {
                    // Set initial UI state based on saved settings
                    if (FindName("useAIEnhancedOCRCheckBox") is CheckBox aiCheckBox)
                    {
                        aiCheckBox.IsChecked = SettingsManager.Settings.UseAIEnhancedOCR;
                    }

                    if (FindName("aiProviderComboBox") is ComboBox providerCombo)
                    {
                        providerCombo.SelectedValue = SettingsManager.Settings.AIProvider;
                    }

                    // Update UI based on saved provider
                    UpdateAIProviderUI(SettingsManager.Settings.AIProvider);

                    // Update status text
                    UpdateAIStatusText();
                }
            });
        }
#pragma warning restore CS0618 // Type or member is obsolete

        // Handle the local time in exports checkbox
        private void UseLocalTimeCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox checkBox && SettingsManager != null)
            {
                SettingsManager.Settings.UseLocalTimeInExports = checkBox.IsChecked ?? false;
                SettingsManager.SaveSettings(); // Auto-save

                Log.Information($"Use local time in exports set to: {SettingsManager.Settings.UseLocalTimeInExports}");

                // No need to refresh UI since this only affects exports
            }
        }

        // Handle expander expanded/collapsed state changes
        private void Expander_ExpandedCollapsed(object sender, RoutedEventArgs e)
        {
            // Save settings whenever an expander state changes
            if (SettingsManager != null)
            {
                // The binding will automatically update the Settings object
                // We just need to persist it to disk
                SettingsManager.SaveSettings();

                // Log the state change for debugging
                if (sender is Expander expander)
                {
                    var headerText = expander.Header?.ToString() ?? "Unknown";
                    var isExpanded = expander.IsExpanded;
                    Log.Debug($"Expander '{headerText}' state changed to: {(isExpanded ? "Expanded" : "Collapsed")}");
                }
            }
        }

        /// <summary>
        /// Handles AI Queue item processed events and updates the UI.
        /// </summary>
        private void OnAIQueueItemProcessed(object? sender, AIOCRResult result)
        {
            try
            {
                Log.Information($"AI Queue item processed: Category={result.CategoryName}, Field={result.FieldName}, Success={result.Success}, TesseractTime={result.TesseractDurationMs}ms, AITime={result.AIDurationMs}ms");

                // Update the AI Queue Monitor ViewModel
                if (_aiQueueMonitorViewModel != null)
                {
                    Log.Debug($"[MainWindow] Updating ViewModel with result: OriginalText='{result.OriginalOCRText}', AIText='{result.Text}', TesseractDuration={result.TesseractDurationMs}ms");
                    _aiQueueMonitorViewModel.MoveToProcessed(result);
                    Log.Debug($"[MainWindow] ViewModel updated. ProcessedCount={_aiQueueMonitorViewModel.ProcessedCount}, AvgTesseract={_aiQueueMonitorViewModel.AverageTesseractMs:F2}ms, AvgAI={_aiQueueMonitorViewModel.AverageAIMs:F2}ms");
                }
                else
                {
                    Log.Warning($"[MainWindow] AIQueueMonitorViewModel is NULL, cannot update UI!");
                }

                // Optionally update the main UI with the enhanced OCR result
                // (This would update the actual field in the scan tab with the AI result)
                // Only apply if successful and has text
                if (result.Success && !string.IsNullOrWhiteSpace(result.Text))
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateFieldWithAIResult(result);
                    });
                }
                else if (result.Success)
                {
                    // AI succeeded but produced empty text, keep original
                    result.WasApplied = false;
                    result.ApplicationReason = "AI result was empty";
                    Log.Information($"[OnAIQueueItemProcessed] AI result not applied: {result.ApplicationReason}");
                }
                else
                {
                    // AI processing failed
                    result.WasApplied = false;
                    result.ApplicationReason = !string.IsNullOrEmpty(result.ErrorMessage)
                        ? $"AI processing failed: {result.ErrorMessage}"
                        : "AI processing failed";
                    Log.Information($"[OnAIQueueItemProcessed] AI result not applied: {result.ApplicationReason}");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to process AI queue item result");
            }
        }

        /// <summary>
        /// Updates a field in the scan UI with the AI-enhanced OCR result.
        /// Also updates the underlying DataRecord in DataManager and the Data tab's ObservableCollection.
        /// Only applies the AI result if its confidence is higher than the original Tesseract confidence.
        /// </summary>
        private void UpdateFieldWithAIResult(AIOCRResult result)
        {
            try
            {
                Log.Information($"[UpdateFieldWithAIResult] Processing AI result: Category='{result.CategoryName}', Field='{result.FieldName}', Text='{result.Text}', Confidence={result.Confidence:F2}%, OriginalConfidence={result.OriginalConfidence:F2}%, ImageHash='{result.ImageHash}', RecordHash='{result.RecordHash}'");

                // Compare AI confidence vs Tesseract confidence
                bool shouldApplyAIResult = result.Confidence > result.OriginalConfidence;

                if (!shouldApplyAIResult)
                {
                    result.WasApplied = false;
                    result.ApplicationReason = $"Tesseract confidence ({result.OriginalConfidence:F2}%) was higher than AI confidence ({result.Confidence:F2}%)";
                    Log.Information($"[UpdateFieldWithAIResult] AI result NOT applied: {result.ApplicationReason}");
                    return; // Don't apply the AI result, keep the original
                }

                // AI confidence is higher, apply the result
                result.WasApplied = true;
                result.ApplicationReason = $"AI confidence ({result.Confidence:F2}%) was higher than Tesseract ({result.OriginalConfidence:F2}%)";
                Log.Information($"[UpdateFieldWithAIResult] AI result WILL BE APPLIED: {result.ApplicationReason}");

                // Cache the AI OCR result if we have the image hash
                if (!string.IsNullOrEmpty(result.ImageHash) && ocrProcessor != null)
                {
                    ocrProcessor.UpdateCacheResult(result.ImageHash, result.Text, result.Confidence);
                    Log.Information($"[UpdateFieldWithAIResult] Cached AI OCR result: ImageHash='{result.ImageHash}', Text='{result.Text}', Confidence={result.Confidence:F2}%");
                }
                else
                {
                    if (string.IsNullOrEmpty(result.ImageHash))
                        Log.Warning($"[UpdateFieldWithAIResult] ImageHash is empty, cannot cache AI result");
                    if (ocrProcessor == null)
                        Log.Warning($"[UpdateFieldWithAIResult] OCRProcessor is null, cannot cache AI result");
                }

                // Update the Scan tab textbox ONLY if it's the currently displayed record
                // Check if the result's record hash matches the current record
                bool isCurrentRecord = !string.IsNullOrEmpty(result.RecordHash) && result.RecordHash == currentRecordHash;

                if (isCurrentRecord)
                {
                    // Note: fieldTextBoxes uses just the field name as key, not category_field
                    if (fieldTextBoxes.TryGetValue(result.FieldName, out var textBox))
                    {
                        // Update the textbox with the AI result
                        textBox.Text = result.Text;

                        // Optionally add a visual indicator that this was AI-enhanced
                        textBox.Background = new SolidColorBrush(Color.FromArgb(30, 46, 204, 113)); // Light green tint

                        // Store the value to prevent Scanner from overwriting it
                        lastFieldValues[result.FieldName] = result.Text;

                        Log.Information($"[UpdateFieldWithAIResult] Updated Scan tab field '{result.FieldName}' with AI result: '{result.Text}'");
                    }
                    else
                    {
                        Log.Warning($"[UpdateFieldWithAIResult] Could not find textbox for field '{result.FieldName}'");
                    }
                }
                else
                {
                    Log.Debug($"[UpdateFieldWithAIResult] Skipping Scan tab update - AI result is for a different record (RecordHash: '{result.RecordHash}' vs Current: '{currentRecordHash}')");
                }

                // Update the underlying DataRecord in DataManager using the RecordHash from the AI result
                // This ensures we update the CORRECT record, even if the scanner has moved on
                if (GameProfileManager?.ActiveProfile != null && dataManager != null && !string.IsNullOrEmpty(result.RecordHash))
                {
                    if (dataManager.DataRecordDictionary.TryGetValue(result.RecordHash, out var existingRecord))
                    {
                        // Verify this record matches the category
                        if (existingRecord.Category == result.CategoryName)
                        {
                            Log.Debug($"[UpdateFieldWithAIResult] Found existing record with hash '{result.RecordHash}', updating field '{result.FieldName}'");

                            // Update the field value
                            existingRecord.Fields[result.FieldName] = result.Text;
                            existingRecord.ScanDate = DateTime.UtcNow; // Update scan date

                            // Update the field confidence with the AI confidence
                            if (existingRecord.FieldConfidences == null)
                            {
                                existingRecord.FieldConfidences = new Dictionary<string, float>();
                            }
                            existingRecord.FieldConfidences[result.FieldName] = result.Confidence;
                            Log.Information($"[UpdateFieldWithAIResult] Updated FieldConfidence for '{result.FieldName}' to {result.Confidence:F2}");

                            // Mark as unsaved
                            dataManager.MarkAsUnsaved();

                            // Force immediate save to persist AI confidence update
                            if (GameProfileManager?.ActiveProfile != null)
                            {
                                dataManager.SaveDataRecords(GameProfileManager.ActiveProfile);
                                Log.Information($"[UpdateFieldWithAIResult] Immediately saved DataRecords to persist AI confidence update for '{result.FieldName}'");
                            }

                            Log.Information($"[UpdateFieldWithAIResult] Updated DataRecord field '{result.FieldName}' to '{result.Text}'");

                            // Update the Data tab's ObservableCollection
                            if (categoryCollections.TryGetValue(result.CategoryName, out var collection))
                            {
                                // Use cached hash for fast lookup instead of regenerating
                                var recordInCollection = collection.FirstOrDefault(r =>
                                    r.CachedHash == result.RecordHash ||
                                    (string.IsNullOrEmpty(r.CachedHash) && GameProfileManager?.ActiveProfile != null && dataManager.GenerateDataHash(r, GameProfileManager.ActiveProfile) == result.RecordHash));

                                if (recordInCollection != null)
                                {
                                    // The record reference is the same, but we need to trigger UI refresh
                                    var index = collection.IndexOf(recordInCollection);
                                    if (index >= 0)
                                    {
                                        // Force refresh by removing and re-adding
                                        collection.RemoveAt(index);
                                        collection.Insert(index, recordInCollection);

                                        Log.Information($"[UpdateFieldWithAIResult] Refreshed Data tab collection at index {index}");
                                    }
                                }
                                else
                                {
                                    Log.Warning($"[UpdateFieldWithAIResult] Could not find record in collection for category '{result.CategoryName}'");
                                }
                            }
                            else
                            {
                                Log.Warning($"[UpdateFieldWithAIResult] Could not find collection for category '{result.CategoryName}'");
                            }

                            // Update unsaved indicator
                            UpdateUnsavedIndicator();
                        }
                        else
                        {
                            Log.Warning($"[UpdateFieldWithAIResult] Record category mismatch: expected '{result.CategoryName}', got '{existingRecord.Category}'");
                        }
                    }
                    else
                    {
                        Log.Warning($"[UpdateFieldWithAIResult] Could not find record with hash '{result.RecordHash}' in DataRecordDictionary");
                    }
                }
                else
                {
                    if (GameProfileManager?.ActiveProfile == null)
                        Log.Warning($"[UpdateFieldWithAIResult] No active profile");
                    if (dataManager == null)
                        Log.Warning($"[UpdateFieldWithAIResult] DataManager is null");
                    if (string.IsNullOrEmpty(result.RecordHash))
                        Log.Warning($"[UpdateFieldWithAIResult] RecordHash is empty - cannot update DataRecord (this may be from an older queue item before the fix)");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"Failed to update field with AI result: {result.CategoryName}/{result.FieldName}");
            }
        }

        // OnClosed is already implemented in the class
    }
}