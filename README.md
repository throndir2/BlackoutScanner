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

## Getting Started

### Prerequisites

- Windows operating system
- .NET Framework
- Supported games must be running in windowed mode for capture

### Installation

1. Clone this repository or download the latest release
2. Open the solution in Visual Studio
3. Build and run the application

## Usage

1. **Create a Game Profile**:
   - Click "New Profile" and enter a profile name
   - Set the game window title
   - Add categories and fields to capture

2. **Define Capture Areas**:
   - Use the visual editor to select screen regions
   - The selected areas will be saved as relative coordinates

3. **Start Scanning**:
   - Select your game profile
   - Click "Start Scan"
   - Data will be automatically captured and saved

4. **View and Export Data**:
   - Captured data is saved in both JSON and TSV formats
   - Review and edit data within the application

## Game Profiles

Game profiles define what data should be captured from specific game windows. Each profile includes:

- **Profile Name**: A user-friendly name for the profile
- **Game Window Title**: The title of the game window to capture
- **Categories**: Groups of data fields (e.g., "Player Profile", "Guild Info")
- **Fields**: Individual data elements to capture, with their screen positions

## License

This project is licensed under the GNU General Public License v3.0 - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- This tool was initially developed for "Grand Cross: Age of Titans" data collection
- Further extended to dynamically handle any game for the Blackout gaming community
- Built with WPF and .NET technologies
