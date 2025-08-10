```markdown
# Blackout Scanner Changelog

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
- Editing key fields results in duplicates data entries
```
