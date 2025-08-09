# Blackout Scanner Task List

## Version 1.0 (Completed)

- [x] Implement game window scanning for a specific game ("Grand Cross: Age of Titans").
- [x] Implement OCR to extract player data fields (Name, Kingdom, Alliance, Combat Power, etc.).
- [x] Implement multi-language support (English, Korean, Japanese, Chinese) for OCR.
- [x] Implement OCR caching to improve performance and allow for corrections.
- [x] Implement data persistence, saving player data to both JSON and TSV files.
- [x] Create a `DataManager` class to handle loading and saving of player data.
- [x] Develop a WPF user interface for starting/stopping scans and viewing/correcting data.
- [x] Implement a real-time log view in the UI.
- [x] Uniquely identify players using a hash of their Name, Kingdom, and Alliance.

## Version 2.0: Game Profiles (In Progress)

### Architecture & Data Models
- [x] Design the `GameProfile` data model. This will be a C# class that can be serialized to/from JSON. It should include:
    - Game window title.
    - A list of `CaptureCategory` objects.
- [x] Design the `CaptureCategory` model, which represents a screen or menu to scan (e.g., "Player Profile"). It should include:
    - Category name.
    - Rectangle bounds for identifying the category.
    - A list of `CaptureField` objects.
- [x] Design the `CaptureField` model, which represents a single piece of data to extract. It should include:
    - Field name.
    - Rectangle bounds for the field.
    - Optional: Data type (string, number) and validation rules (e.g., regex).
- [x] Refactor `PlayerData` to be a more dynamic data structure (e.g., `Dictionary<string, object>`) that can accommodate fields defined in a Game Profile. **This will be renamed to `DataRecord`.**

### Game Profile Management
- [x] Implement a `GameProfileManager` class responsible for:
    - Loading all `*.json` profiles from a `profiles` directory.
    - Getting the active profile.
    - Saving new or edited profiles.
- [x] On startup, the application should load a default or the last used game profile.

### UI - Game Profile Editor
- [x] Implement a new "Profiles" or "Configuration" tab in the main window.
- [x] In this tab, add controls to:
    - List all available game profiles.
    - Select the active profile for scanning.
    - Create, edit, and delete profiles.
- [x] Create a dedicated window or view for the Game Profile Editor. This editor should allow a user to:
    - Set the game's window title.
    - Manage capture fields for each category.

### UI - Interactive Area Selection
- [x] Implement a feature that allows the user to define capture areas interactively.
- [x] This could involve:
    - Taking a screenshot of the game window.
    - Saving these coordinates into the profile being edited.

### Refactor Core Logic
- [x] Modify `Scanner.cs` to operate using the currently loaded `GameProfile`.
    - All hard-coded rectangles, window titles, and field logic must be removed.
    - The scanner should loop through the categories and fields defined in the profile to perform OCR.
    - Events for data updates should be made generic (e.g., a single event that passes a dictionary of updated data).
- [x] Modify `MainWindow.xaml` and `MainWindow.xaml.cs` to be dynamic.
    - [x] The "Scan" tab should dynamically generate its UI elements (text boxes, image controls) based on the fields in the active `GameProfile`.
    - [x] Event handlers for saving corrected data must be generalized.
- [x] Modify `DataManager.cs` to handle dynamic data.
    - [x] The method for generating a unique player hash should be configurable from the profile (e.g., specify which fields are key fields).
    - [x] The TSV export function must generate headers and rows dynamically based on the profile's fields.
    - [x] **Successfully refactored to use `DataRecord` instead of `PlayerData`.**

### V2.1: Refactor from PlayerData to Generic DataRecord (Completed)
- [x] Rename `Models/PlayerData.cs` to `Models/DataRecord.cs` and update the class name.
- [x] Update `DataManager.cs`:
    - Replace all `PlayerData` references with `DataRecord`.
    - Change file names from `player_data.json` and `player_data.tsv` to `data_records.json` and `data_records.tsv`.
- [x] Update `Scanner.cs` to create and process `DataRecord` objects instead of `PlayerData`.
- [x] Update `MainWindow.xaml.cs` to remove any lingering `PlayerData` concepts and use `DataRecord` where necessary.

### Testing
- [x] Thoroughly test the profile creation and editing UI.
- [x] Test the scanning process using a loaded game profile to ensure it works as expected.
- [x] Extract interfaces for all major components
- [x] Implement dependency injection (consider using a DI container)
- [x] Abstract external dependencies (file system, Win32 APIs)
- [x] Use the Repository pattern for data access
- [ ] Separate UI from business logic using MVVM properly
- [ ] Implement a command/query separation for operations
- [ ] Create testable wrappers for Windows API calls
- [x] Use IScheduler or similar for time-based operations
- [ ] Create tests.

## Version 2.0 Additional Features (Completed)
- [x] Implement Dark Theme for better UI experience
- [x] Add DPI awareness support for better display scaling
- [x] Create window search dialog for finding and selecting game windows
- [x] Add an interactive area selection tool for defining capture regions
- [x] Add profile comparison converter for active profile indication
- [x] Image comparison mode for scanning, set as default
- [x] UI improvements in configuration menu

## Bug Fixes (2025-08-09)
- [x] Game Profiles list not visible when app height reduced
- [x] App settings values in appdata are not reflected in the UI causing confusion

## Known Issues
- [ ] Editing key fields results in duplicates data entries

## Upcoming Features
- [ ] Include validation when defining an area, with "Category name does not equal <text found>" warning
- [ ] Multi-object scan (leaderboards)
- [ ] Autostop scan when modifying name/invalid OCR, and restart on Save click
- [ ] Category scanning optional requirement for pixel color
- [ ] Hotkey to stop/start scan
- [ ] Language configuration
- [ ] Fallback OCR must be a configurable option, default false, as it adds too much time with little gain
- [ ] Configurable OCR Threshhold, anything below will be considered no detection
- [ ] Linking categories together (Chieftain page to More Info page)
- [ ] Package to singular EXE
- [ ] Include Help text
- [ ] Submit file to Microsoft security
