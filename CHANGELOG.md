```markdown
# Blackout Scanner Changelog

## [2.0.5] - 2025-08-18

### Added
- Configurable language support in the Configuration tab
- Dynamic language engine management for OCR processing
- Support for 20+ languages organized by script type
- Smart language engine instantiation to optimize memory usage
- Improved Configuration tab with collapsible sections for better organization
- Dynamic profile list height based on content
- Consistent styling for expanded/collapsed sections
- Streamlined scrolling behavior in the Configuration tab
- Added Russian as default languages.
- Optimization of code and made it more performant for OCR scanning

### Fixed
- Fixed critical OCR error "Value cannot be null. (Parameter 'encoder')" by using explicit PNG format for bitmap processing
- Improved memory management with proper bitmap disposal to prevent memory leaks
- Enhanced OCR performance with optimized bitmap handling
- Ensured consistent image data caching for UI display in all processing paths
- Fixed duplicate method definitions in OCRProcessor.cs
- Fixed potential null reference issues in language selection handling
- Eliminated nested scrollbars in OCR language selection area
- Enhanced theme consistency in collapsible UI elements

## [2.0.4] - 2025-08-12

### Added
- Multi-entity scanning support for capturing multiple data rows at once (leaderboards, player lists)
- Configurable row height offset for multi-entity captures
- Maximum entity count setting to limit the number of rows scanned
- Row index tracking in exports and data records
- Group ID for associating multiple entities from the same scan
- Enhanced export formats for multi-entity data
- OCR performance mode selection in Configuration tab
  - "Fast Processing" mode (default) - uses a single combined language engine for better speed
  - "Enhanced Accuracy" mode - uses multiple language engines for better OCR results at cost of speed
- Configurable OCR confidence threshold via slider control
- Button to reinitialize OCR engines after changing settings
- Added comprehensive debug logging for troubleshooting data update issues

### Fixed
- Data tab only showing the first scanned record and not updating with subsequent records
- Manual field edits in the Scan tab not properly reflected in the Data tab or in exports
- Key field updates causing potential data loss when editing in the Scan tab
- Added protection against duplicate records when updating key fields
- Fixed bug where editing non-key fields created duplicate entries in the Data tab
- Fixed issue where manually saving fields failed after editing with "No currentRecordHash" warning
- Fixed critical bug where category information wasn't being correctly passed from the Scanner to UI
- Improved handling of image-based category detection to properly maintain record history
- Enhanced robustness of record tracking when switching between categories
- Improved UI synchronization between Scan tab and Data tab

## [2.0.3] - 2025-08-10

### Added
- Configurable hotkey to start and stop scanning
- Hotkey configuration UI in the Configuration tab
- Scanning automatically pauses when editing a field in the Scan tab
- Scanning resumes when Save button is clicked after editing
- Option to use local time in exports instead of UTC

## [2.0.2] - 2025-08-09

### Fixed
- Game Profiles list not visible when app height reduced
- App settings values in appdata are not reflected in the UI causing confusion

### Changed
- Replaced "Verbose Logging" with configurable "Log Level" parameter
- Log level control allows selection from Verbose, Debug, Information, Warning, Error, and Fatal levels
- All logs still recorded to file regardless of UI log level setting

### Added
- Version information embedded in executable file
- Window title now shows application version number
- Image comparison mode for scanning, now set as default
- UI improvements in configuration menu

### Known Issues
- None currently identified
```
