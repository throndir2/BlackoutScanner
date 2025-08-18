# BlackoutScanner

A WPF application for scanning game windows and extracting data using OCR technology, with a focus on profile-based game data capture.

<a href="https://www.buymeacoffee.com/throndir" target="_blank"><img src="https://cdn.buymeacoffee.com/buttons/v2/default-yellow.png" alt="Buy Me A Coffee" style="height: 40px !important;" ></a>

## Overview

BlackoutScanner is designed to extract information from game windows using OCR (Optical Character Recognition). It allows users to define specific areas of a game's UI to scan and capture data, organizing the information into structured formats. This tool is particularly useful for collecting and analyzing in-game data without needing direct access to the game's internal systems.

## Features

- **Game Window Scanning**: Automatically detect and capture information from specific game windows
- **Custom Game Profiles**: Create and manage profiles for different games with specific capture areas
- **OCR Processing**: Extract text data from captured screen regions with multi-language support
- **Data Management**: Save extracted data in both JSON and TSV formats for easy analysis
- **Visual Editor**: Define capture areas using an intuitive visual interface
- **Live Preview**: See real-time previews of selected screen regions
- **Hotkey Support**: Use keyboard shortcuts for quick scanning
- **Auto-save**: Automatically save captured data with customizable intervals

## Installation

### Prerequisites

- Windows 10 or Windows 11 operating system
- .NET Framework 4.8 or later
- Supported games must be running in windowed or borderless windowed mode

### Download and Install

1. Go to the [Releases](https://github.com/throndir2/BlackoutScanner/releases) page
2. Download the latest `BlackoutScanner.zip` file
3. Extract the ZIP file to your desired location
4. Run `BlackoutScanner.exe`

## Usage Guide

### Creating Your First Game Profile

1. **Launch BlackoutScanner** and click the **"Configuration"** tab, and click on the **"New"** button
2. **Enter Profile Details**:
   - **Profile Name**: Give your profile a descriptive name (e.g., "MyGame - Character Stats")
   - **Game Window Title**: Enter the exact title of your game window as it appears in the title bar, or press the **"Search..."** button and look for your running game

### Setting Up Capture Areas

#### Category

1. **Category**: Click **"Add Category"** in your profile
   - **Category Name**: Label for the captured data (e.g., "Player", "Alliance")
   - **Comparison Mode**: Specifies comparing pixels to determine when to take a snapshot of the game
   - **Category Area**: Click **"Define Category Area"** then select a part of the screen that is unique to that menu (e.g., the title of the menu)
2. **Configure Each Field**: Click **"Add Field"** 
   - **Field Name**: Label for the captured data (e.g., "Player Name", "Level", "Gold")
   - **Key Field**: Check if this value is what makes this entry unique (e.g., this should be checked for an Id field) 
   - **Area**: Click **"Define"** then select the text on the game that has this value for the app to scan

### Scanning

1. **Scan**: Click **"Start Scanning"**, then navigate to each of the defined menus in-game to start scraping data.

#### Editing and Validation

1. **Edit Values**: If OCR result is incorrect, click on the field in the **"Scan"** tab, manually modify, then click on **"Save"**

#### Exporting Data

1. **Export Formats**:
   - **JSON**: Structured data perfect for APIs and programming
   - **TSV**: Tab-separated values for Excel/Google Sheets

## Support

- **Issues**: Report bugs on the [GitHub Issues](https://github.com/throndir2/BlackoutScanner/issues) page
- **Discussions**: Join community discussions for tips and profile sharing
- **Wiki**: Check the [Wiki](https://github.com/throndir2/BlackoutScanner/wiki) for detailed guides

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- This tool was initially developed for "Grand Cross: Age of Titans" data collection
- Further extended to dynamically handle any game for the Blackout gaming community
- Built with WPF and .NET technologies
- OCR powered by Tesseract