```markdown
# Blackout Scanner Changelog

## [2.1.2] - 2025-12-25

### Added
- **Groq Provider Support**: New AI provider integration with GroqCloud
  - Support for Llama 4 vision models: `llama-4-maverick-17b-128e-instruct` and `llama-4-scout-17b-16e-instruct`
  - Rate limit header logging for monitoring API usage
- **Gemini 3 Flash Support**: Added support for `gemini-3-flash` and other preview models via v1alpha endpoint
- **NVIDIA Nemotron-Parse Support**: Added support for `nvidia/nemotron-parse` model for document OCR

### Improved
- **Performance Optimizations**:
  - Cached hash lookups on DataRecord objects to avoid repeated SHA256 calculations during collection searches
  - Replaced slow `GetPixel()` calls with `LockBits` direct memory access for ~10x faster image comparison
  - Added early-exit optimization in pixel comparison when threshold cannot be reached
  - Upper cap limits on memory usage for OCR cache, hash cache, and image data cache

### Fixed
- AI Provider Editor dialog now preserves custom display names and rate limits when editing existing providers
- Groq connection test now uses 2x2 pixel image (API requires minimum 2 pixels per dimension)

## [2.1.1] - 2025-10-03

### Added
- **Multi-Provider AI Support**: Configure multiple AI providers with priority-based cascade fallback
  - Google Gemini integration with gemini-2.5-flash optimized for special characters and mixed-language text
  - Provider management UI: add, edit, delete, reorder, and test connections
  - Automatic settings migration from single to multi-provider format
  - AI Queue Monitor now shows expandable cascade attempt details for each processed item

### Fixed
- Application crash on startup due to converter type mismatch in AI provider buttons
- Inconsistent button hover behavior across dialogs and management UI
- Dark theme compatibility issues in AI Provider Editor dialog

## [2.1.0] - 2025-10-03

### Added
- **AI Queue Processor**: Automatically enhances low-confidence Tesseract OCR results using AI
  - Background processing queue for AI-enhanced OCR
  - NVIDIA Build API integration with support for multiple OCR models
  - Configurable confidence threshold to trigger AI enhancement
  - AI Enhancement Settings in Configuration tab with provider selection and API key management
  - Real-time monitoring window showing queue status and processing metrics
- Extensible AI provider framework for future integration with OpenAI, Google Gemini, and custom endpoints

### Fixed
- OCR cache not properly storing AI-enhanced confidence levels, causing unnecessary re-processing
- AI confidence values not persisting correctly after app restart

### Changed
- Configuration sections now remember their expanded/collapsed state across app sessions

## [2.0.6] - 2025-10-02

### Added
- Smart change detection to skip OCR processing when screen content is unchanged
- Hash-based tracking of category and field areas for efficient change detection

### Fixed
- Excessive CPU usage when game screen is static
- Application crashes during Tesseract OCR engine initialization (AccessViolationException)
- Thread-safety issues with concurrent engine creation
- Memory corruption when initializing multiple OCR engines simultaneously

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
