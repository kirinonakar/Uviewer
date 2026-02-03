# Uviewer

> Created with Vibe Coding 🚀

A versatile Windows application for viewing images, text files, and EPUB documents built with WinUI and .NET.

## Features

### Image Viewing
- Support for multiple image formats including animated WebP
- Zoom and pan functionality with customizable zoom levels (0.1x to 10x)
- Fullscreen mode with auto-hiding toolbar and sidebar
- Image navigation through folders and archives

### Text File Viewing
- Support for various text file formats
- Markdown rendering with Markdig
- Syntax highlighting and text preprocessing
- Page navigation for large text files

### EPUB Reader
- Complete EPUB document support
- Page-based navigation system
- WebView2 integration for rich content rendering
- Table of contents and chapter navigation

### File Management
- Built-in file explorer for easy navigation
- Support for compressed archives (using SharpCompress)
- Favorites system for quick access to frequently used files
- Drag-and-drop file support

### User Interface
- Modern WinUI 3 interface with Windows App SDK
- Responsive design with sidebar and toolbar
- Keyboard shortcuts for efficient navigation
- Customizable themes and appearance

## System Requirements

- Windows 10 version 19041 or higher
- Windows 11 (recommended)
- .NET 10.0 runtime
- Windows App SDK 1.8 or later
- WebView2 runtime (included with Windows)

## Installation

### From Source
1. Clone this repository:
   ```bash
   git clone https://github.com/kirinonakar/Uviewer.git
   cd Uviewer
   ```

2. Open the solution in Visual Studio 2022:
   - Ensure you have the .NET desktop development workload
   - Install the Windows App SDK extension if not already installed

3. Build and run the project:
   - Select your desired platform (x64, x86, or ARM64)
   - Press F5 or click "Start Debugging"

### Build from Command Line
```bash
dotnet build Uviewer.sln
dotnet run --project Uviewer/Uviewer.csproj
```

## Usage

### Opening Files
- **Drag and Drop**: Simply drag files onto the Uviewer window
- **File Menu**: Use File > Open to browse for files
- **Command Line**: Launch with file path as argument
- **File Explorer**: Double-click supported file types

### Navigation
- **Images**: Use arrow keys or toolbar buttons to navigate between images
- **Text Files**: Scroll through content or use page navigation
- **EPUB**: Navigate chapters using the table of contents or page controls

### Keyboard Shortcuts
- `Space` or `Enter`: Next file/page
- `Backspace`: Previous file/page
- `F11`: Toggle fullscreen mode
- `Ctrl+O`: Open file dialog
- `Ctrl+F`: Toggle favorites sidebar
- `Ctrl+E`: Toggle file explorer
- `+/-`: Zoom in/out
- `0`: Reset zoom to 100%

### Supported Formats

#### Image Formats
- JPEG, PNG, GIF, BMP, TIFF
- WebP (including animated WebP)
- ICO, SVG
- RAW formats (via ImageSharp)

#### Text Formats
- Plain text (.txt)
- Markdown (.md, .markdown)
- Source code files (.cs, .js, .html, .css, etc.)
- Configuration files (.json, .xml, .yaml, etc.)

#### Document Formats
- EPUB (.epub)
- Compressed archives containing supported formats

## Architecture

The application is built using:
- **.NET 10.0** with WinUI 3
- **Windows App SDK** for modern Windows development
- **ImageSharp** for image processing
- **Markdig** for Markdown rendering
- **SharpCompress** for archive support
- **WebView2** for rich content display

The codebase is organized into partial classes:
- `MainWindow.xaml.cs`: Core window functionality
- `MainWindow.files.cs`: File management and navigation
- `MainWindow.text.cs`: Text file handling
- `MainWindow.epub.cs`: EPUB document support
- `MainWindow.animatedWebp.cs`: Animated image support
- `MainWindow.favorites.cs`: Favorites system
- `MainWindow.keys.cs`: Keyboard shortcuts
- `MainWindow.etcs.cs`: Additional utilities

## Contributing

1. Fork the repository
2. Create a feature branch: `git checkout -b feature-name`
3. Make your changes and test thoroughly
4. Commit your changes: `git commit -am 'Add feature'`
5. Push to the branch: `git push origin feature-name`
6. Submit a pull request

## License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

## Author

**kirinonakar** - *Initial work* - [kirinonakar](https://github.com/kirinonakar)

## Acknowledgments

- Microsoft for Windows App SDK and WinUI 3
- ImageSharp team for excellent image processing library
- Markdig contributors for Markdown parsing
- SharpCompress team for archive support
