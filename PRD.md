# Product Requirements Document: Blackout Scanner

**Author:** Throndir (mozart2b@gmail.com)
**Date:** July 8, 2025
**Version:** 2.0

---

## 1. Introduction

The Blackout Scanner is a desktop application designed to automate the collection of data from game clients. It captures specific regions of any game window, performs Optical Character Recognition (OCR) to extract text and statistics, and saves the data for analysis. Using configurable game profiles, it can adapt to different games and UI layouts without code changes. The application provides a user-friendly interface to create capture configurations, initiate scans, view extracted data, and manually correct any OCR inaccuracies.

## 2. Objectives

*   **Automate Data Collection:** Eliminate the manual effort required to record player statistics from the game.
*   **Improve Accuracy:** Provide a mechanism for reviewing and correcting OCR results to ensure data integrity.
*   **Centralize Data:** Store collected player data in structured formats (JSON, TSV) for easy access and analysis.
*   **Provide a User-Friendly Interface:** Offer a simple UI for starting/stopping the scanning process and managing data.
*   **Dynamic Configuration:** Allow users to create and manage "Game Profiles" to support different games or UI layouts without code changes.

## 3. Target Audience

This tool is intended for players of the game who are interested in tracking and analyzing player statistics, such as guild leaders, strategists, or data-driven players.

## 4. Features

### 4.1. Game Window Scanning

*   The application can identify and target a specific game window by its title.
*   It brings the game window to the foreground to ensure it is visible for capture.
*   It captures designated rectangular areas of the game window corresponding to specific data points.
*   The application includes a window search dialog that lists all running applications, making it easy to find and select the correct game window during configuration.

### 4.2. Data Extraction (OCR)

*   The application uses the Tesseract OCR engine to extract text from the captured images.
*   **Dynamic Data Fields:** The specific data fields to be extracted are not hard-coded. Instead, they are dynamically defined within each `GameProfile`.
*   **Multi-language Support:** The OCR engine is configured to support English, Korean, Japanese, and both Simplified and Traditional Chinese.
*   **OCR Caching:**
    *   To optimize performance and allow for corrections, OCR results are cached.
    *   An image hash is used as the key for the cache. If an image has been processed before, the cached result is used.
    *   The cache is persisted to a `ocrCache.json` file.

### 4.3. Data Persistence

*   **Data Store:** All collected data is stored in a `data_records.json` file and a `data_records.tsv` file. Each entry represents a single, complete scan of a category (e.g., a player's profile, an item's stats).
*   **Data Manager:** A dedicated `DataManager` class handles loading and saving the data records, ensuring consistency.
*   **Data Integrity:** The system generates a unique hash for each data record based on key fields defined in the `GameProfile`. This allows for updating existing records.

### 4.4. User Interface (WPF)

*   **Main Window:** A single window provides all user controls.
*   **Scan Control:** A "Start/Stop Scan" button to toggle the scanning process.
*   **Data Display:**
    *   Text boxes display the most recently scanned values for each data field.
    *   Image controls show the captured screen snippets for each data field, allowing for visual verification.
*   **Manual Correction:**
    *   Users can edit the text in the data fields.
    *   Saving a corrected value updates both the OCR cache and the corresponding data record in memory. This "teaches" the system the correct value for that specific image hash.
*   **Logging:** A log panel within the UI displays real-time status messages and errors from the application.
*   **Dark Theme:** The application uses a dark theme for better visibility and reduced eye strain, especially important during extended gaming sessions.

### 4.5. Game Profile Management

*   **Game Profiles:** The application will support multiple game profiles, where each profile is a JSON file that defines how to scan a specific game. This removes all hard-coded values from the application logic.
*   **Profile Contents:** Each profile will define:
    *   The target game's window title.
    *   A list of "Categories" or screens to be scanned (e.g., "Player Profile", "Alliance Member List").
    *   For each category, the rectangular bounds to identify it.
    *   A list of data "Fields" to be extracted from each category (e.g., "Player Name", "Combat Power").
    *   For each field, its rectangular bounds and data type.
*   **Profile Management UI:** A new "Configuration" tab will be added to the UI, allowing users to:
    *   Create, edit, and delete game profiles.
    *   Select the active profile to be used for scanning.
*   **Interactive Area Selection:** The profile editor features interactive tools that allow the user to:
    *   Search for and select the target game window from a list of running applications.
    *   Capture a screenshot of the target game window.
    *   Draw and adjust rectangles directly on the screenshot to define the bounds for categories and fields.
    *   See real-time coordinate information while selecting areas.
    *   Use relative coordinate system (percentages) to ensure compatibility across different screen resolutions.

## 5. Data Model

### 5.1. `DataRecord`

A class that represents a single data record. This is now a dynamic structure that accommodates the fields defined in a Game Profile.

| Field               | Type                          | Description                               |
| ------------------- | ----------------------------- | ----------------------------------------- |
| `Fields`            | `Dictionary<string, object>`  | Dynamic collection of field values captured from scanning. |
| `ScanDate`          | `DateTime`                    | The timestamp of when the data was scanned. |
| `Category`          | `string`                      | The category (screen/view) this record was captured from. |
| `GameProfile`       | `string`                      | The name of the game profile used to capture this record. |

### 5.2. `OCRResult`

A class that holds the result of an OCR operation.

| Field             | Type                          | Description                                           |
| ----------------- | ----------------------------- | ----------------------------------------------------- |
| `ImageHash`       | `string`                      | A unique hash of the processed image.                 |
| `Text`            | `string`                      | The recognized text from the image.                   |
| `WordConfidences` | `List<(string, float)>`       | A list of recognized words and their confidence levels. |

### 5.3. `GameProfile`

A class representing a game profile configuration.

| Field             | Type                          | Description                                           |
| ----------------- | ----------------------------- | ----------------------------------------------------- |
| `ProfileName`     | `string`                      | The unique name for this profile configuration.       |
| `GameWindowTitle` | `string`                      | The window title of the game executable.              |
| `Categories`      | `List<CaptureCategory>`       | A list of scannable categories within the game.       |

### 5.4. `CaptureCategory`

A class representing a specific screen or view to be scanned.

| Field             | Type                          | Description                                           |
| ----------------- | ----------------------------- | ----------------------------------------------------- |
| `Name`            | `string`                      | The name of the category (e.g., "Player Profile").    |
| `RelativeBounds`  | `RelativeBounds`              | The relative coordinates (0.0 to 1.0) to capture for identifying this category. |
| `Bounds`          | `Rectangle`                   | The absolute screen coordinates calculated from RelativeBounds. |
| `Fields`          | `ObservableCollection<CaptureField>` | A list of data fields to extract from this category.  |
| `PreviewImage`    | `BitmapImage`                 | Preview image for this category area (UI only).       |

### 5.5. `CaptureField`

A class representing a single data point to be extracted.

| Field             | Type                          | Description                                           |
| ----------------- | ----------------------------- | ----------------------------------------------------- |
| `Name`            | `string`                      | The name of the field (e.g., "Combat Power").         |
| `RelativeBounds`  | `RelativeBounds`              | The relative coordinates (0.0 to 1.0) for this field. |
| `Bounds`          | `Rectangle`                   | The absolute screen coordinates calculated from RelativeBounds. |
| `IsKeyField`      | `bool`                        | Indicates if this field is part of the unique identifier for a data record. |
| `PreviewImage`    | `BitmapImage`                 | Preview image for this field area (UI only).          |

### 5.6. `RelativeBounds`

A class representing coordinates as relative values (percentages) rather than absolute pixels, enabling resolution independence.

| Field             | Type                          | Description                                           |
| ----------------- | ----------------------------- | ----------------------------------------------------- |
| `X`               | `double`                      | The X coordinate as a percentage (0.0 to 1.0) of the container width. |
| `Y`               | `double`                      | The Y coordinate as a percentage (0.0 to 1.0) of the container height. |
| `Width`           | `double`                      | The width as a percentage (0.0 to 1.0) of the container width. |
| `Height`          | `double`                      | The height as a percentage (0.0 to 1.0) of the container height. |


## 6. Non-Functional Requirements

*   **Performance:** The application should perform screen capture and OCR with minimal impact on game performance. Caching is implemented to reduce redundant processing.
*   **Reliability:** The application should handle cases where the game window is not found and log errors gracefully.
*   **Usability:** The interface should be intuitive, allowing a non-technical user to easily operate the scanner, correct data, and configure new game profiles.
*   **DPI Awareness:** The application is DPI-aware, ensuring consistent capture and display across various screen resolutions and scaling settings.

## 7. Future Enhancements (Out of Scope for Current Version)

*   ~~**Configuration UI:** Allow users to define the screen capture coordinates through a graphical interface instead of hardcoded values.~~ (Addressed in Game Profile Management)
*   ~~**Plugin System:** Allow for different games to be supported by creating new configuration profiles or plugins.~~ (Addressed by Game Profiles)
*   **Historical Data Tracking:** Implement features to view and chart the changes in a player's stats over time.
*   **Automatic Game Detection:** Automatically find the game process without requiring the exact window title.
*   **Multi-Category Batch Scanning:** Ability to automatically navigate through multiple game screens and scan each defined category in sequence.
*   **Export to External Analytics:** Integration with external tools like Excel or Power BI for advanced data analysis.
