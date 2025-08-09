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

- Windows 10 or Windows 11 operating system
- At least 4GB of free disk space for Visual Studio installation
- Internet connection for downloading software
- Supported games must be running in windowed mode for capture

### Installation

Since there are no pre-built releases yet, you'll need to build the application from source. Don't worry - we'll guide you through each step!

#### Step 1: Install Visual Studio Community (Free)

1. Go to [https://visualstudio.microsoft.com/downloads/](https://visualstudio.microsoft.com/downloads/)
2. Click the "Free download" button under **Visual Studio Community**
3. Run the downloaded installer (`VisualStudioSetup.exe`)
4. When the installer opens, you'll see a list of workloads. Check the box for:
   - **.NET desktop development**
5. On the right side, make sure these are checked:
   - .NET Framework 4.8 development tools
   - Windows Presentation Foundation (WPF)
6. Click "Install" (this may take 15-30 minutes depending on your internet speed)
7. Once installed, you can close Visual Studio if it opens automatically

#### Step 2: Download the Source Code

**Option A: Using Visual Studio's Built-in Git (Recommended)**
1. Open **Visual Studio**
2. On the start window, click "Clone a repository"
3. In the "Repository location" field, paste:
   ```
   https://github.com/YourUsername/BlackoutScanner.git
   ```
4. Choose where you want to save it (like `C:\Users\YourUsername\Documents\`)
5. Click "Clone"
6. Visual Studio will download the code and open the project automatically

**Option B: Download as ZIP**
1. Go to the BlackoutScanner repository page on GitHub
2. Click the green "Code" button
3. Click "Download ZIP"
4. Extract the ZIP file to a folder like `C:\Users\YourUsername\Documents\BlackoutScanner`
5. Open Visual Studio and click "Open a project or solution"
6. Navigate to the extracted folder and open the `.sln` file

#### Step 3: Build and Run the Application

1. Once the project is open in Visual Studio, wait for it to finish loading (you'll see activity in the bottom status bar)
2. At the top of Visual Studio, find the dropdown that says either "Debug" or "Release" - change it to **Release**
3. Press **Ctrl+Shift+B** or go to menu **Build → Build Solution**
   - You'll see build output in the bottom panel
   - Wait for it to say "Build succeeded"
4. Press **F5** or click the green "Start" button to run the application

#### Step 4: Create a Desktop Shortcut (Optional)

After building successfully:
1. In Visual Studio, right-click on the project name in Solution Explorer
2. Select "Open Folder in File Explorer"
3. Navigate to `bin\Release` folder
4. Find `BlackoutScanner.exe`
5. Right-click on it and select "Create shortcut"
6. Move the shortcut to your desktop

### Troubleshooting

**"Build failed" errors:**
- Make sure you selected **.NET desktop development** when installing Visual Studio
- Try going to **Tools → NuGet Package Manager → Manage NuGet Packages for Solution** and click "Restore" if you see a restore button

**"Cannot find the game window":**
- Make sure your game is running in windowed mode (not fullscreen)
- The game window title must match exactly what you enter in the profile

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
