# Uviewer

> Created with Vibe Coding 🚀

![Uviewer Screenshot](Uviewer/Assets/screenshot.jpg)

**Uviewer** is a versatile, integrated viewer for Windows that delivers a modern, high-performance experience built on **WinUI 3** and **.NET 10**. It seamlessly handles images, text, PDF and EPUB documents, offering advanced capabilities such as direct image viewing from **archives**, **side-by-side viewing modes**, and robust support for **Aozora Bunko**—complete with **vertical text rendering**. Designed for flexibility, it also features integrated **Markdown support**, smart encoding detection, and **WebDAV connectivity** to streamline your digital reading and viewing workflow.

## ✨ Key Features

### 🖼️ Image & PDF Viewing
- **Broad Format Support**: PDF, JPEG, PNG, GIF, BMP, TIFF, ICO, SVG, WebP, AVIF (AV1 Video Extension required), JXL (JPEG XL Image Extension required) and **Animated WebP**.
- **High-Performance Rendering**: Powered by **Win2D** (Direct2D) for smooth zooming and panning.
- **Smart Scaling**: Customizable zoom levels (0.1x to 10x), fit-to-window, and actual size.
- **EXIF & Image Metadata**: Click the file name in the status bar while viewing an image to inspect EXIF details and embedded metadata such as prompts or parameters.
- **Continuous Scrolling & Zoom Persistence**: Supports PDF-like continuous vertical scrolling for zoomed images—**perfect for reading Webtoons**. Maintains zoom level across images for a seamless viewing experience.
- **Tools**: Sharpening filter (Right-click for settings), Side-by-Side view mode, **Auto Side-by-Side in Archive**, **Match Control Direction** (R-to-L navigation support), and Fast Navigation overlay.

### 📝 Text & Novel Viewing
- **Advanced Text Engine**: 
  - **Immediate loading of large text files**
  - **Aozora Bunko format Support**: Native rendering of Ruby text (Furigana), emphasis, indentation and images.
  - **Markdown Support**: Custom-built Markdown rendering including tables, code blocks, and inline formatting.
  - **Smart Encoding Detection**: Automatically detects and handles various encodings (UTF-8, Unicode, **EUC-KR**, **Shift-JIS**, **Johab**).
- **Reading Comfort**: 
  - **Adjustable Styling**: Change font size, font family (toggle between two user-defined defaults), and background themes (Light/Beige/Dark/Custom). (Recommendation: We suggest installing and using [**Noto Sans/Serif CJK**](https://fonts.google.com/noto) for the best multilingual reading experience.)
  - **Vertical Mode (Tategaki)**: Comprehensive support for vertical text rendering with pixel-accurate layout.
- **Go to Line**: Jump directly to a specific line (G).
- **Search**: Find text in Text, EPUB, and PDF files with a compact overlay near the **G** button. Open it with **Ctrl + F** or right-click **G**, then move to previous/next matches.
- **Table of Contents (TOC)**: Automatically extracts headings from textual documents (Markdown `#` or Aozora `［＃...］` tags) and **PDF Bookmarks** for quick navigation.
- **Line Bookmark**: Save your reading progress (Line number) to Favorites and resume exactly where you left off.

### 📖 EPUB Reader
- **Full EPUB Support**: Page-based navigation with chapter tracking.
- **Table of Contents**: Support for Navigation via TOC.
- **Customization**: Adjustable font settings (font family/size) and background colors.
- **Vertical Support**: View EPUBs in vertical mode


### 📂 File Management
- **Integrated Explorer**: Sidebar with Folder/Thumbnail views for easy navigation, including adjustable thumbnail size and optional folder thumbnails.
- **Archive Support**: Read images directly from compressed archives (`.zip`, `.rar`, `.7z`, etc.) using **SharpCompress** and **SevenZipExtractor**. Includes **Auto Side-by-Side** support for portrait images within archives.
- **Organization**: "Favorites" and "Recent Files" management.
- **WebDAV Support**: Read images directly from WebDAV (HTTPS) servers.

### 🔧 Settings

#### 📌 Pin / Auto-Hide UI
- **Pin Toggle**: Pin or unpin the UI (title bar, toolbar, sidebar, status bar) using the pin button or the **`** (backtick) key.
  - **Pinned** (default): All UI elements are always visible.
  - **Unpinned**: All UI elements are hidden. Hover over the top edge to reveal the title bar, toolbar and status bar, or hover over the left edge to reveal the sidebar. UI auto-hides after 1 second when the mouse leaves.

#### ⚙️ Multiple Instances
- **Allow Multiple Instances**: When enabled, each file opens in a new window instance. When disabled, files open in the already running instance using inter-process communication (NamedPipe).

#### 🖼️ Image Viewing Options
- **Auto Side-by-Side in Archive**: When enabled, the viewer automatically applies side-by-side (2-page) view for portrait-oriented images (height = width * 1.2~3) when browsing archives and EPUB.

#### 🗂️ Explorer Thumbnail Settings
- **Thumbnail Button Settings**: Right-click the sidebar thumbnail/list toggle button to open thumbnail settings.
  - **Thumbnail Size**: Adjust the thumbnail grid size with a slider and preview the result immediately.
  - **Folder Thumbnails**: When enabled, folders show the first image inside the folder as their thumbnail.
  - Settings are saved automatically and restored on the next launch.

#### 🎨 Image Sharpening & Upscaling
- **Advanced Sharpening Control**: Right-click the **Sharpening (S)** button to open the settings flyout.
  - **Upscale Factor**: Adjust the internal rendering scale for clearer details on high-resolution displays.
  - **Sharpening Amount & Threshold**: Fine-tune the basic sharpening intensity and the edge detection threshold.
  - **Unsharp Mask (USM)**: Apply high-quality unsharp mask sharpening with adjustable strength and radius for professional-grade results.

#### 📝 Font Settings
- **Customizable Font Toggle**: Right-click the **Font (F)** button in the text toolbar to set your two preferred fonts (default fonts - Yu Gothic Medium/Yu Mincho).
  - **Font 1 & 2**: Choose any system font for each slot (e.g., one for Sans-serif and one for Serif).
  - Left-clicking the **F** button (or pressing **F**) will instantly toggle between these two selections.

### 🌍 Localization
- **Multi-language Support**: Automatically detects and switches between **English**, **Korean**, **Japanese**, **Chinese (Simplified)**, **Chinese (Traditional)**, and **Vietnamese** based on system settings.

### 🎨 Global Theme & Design
- **Dark & Light Mode**: Seamlessly switch between dark and light themes for the entire interface.
- **Modern Windows Integration**: Features an extended title bar with modern styling, similar to Windows 11 system apps like Explorer and Settings.
- **Unified Aesthetics**: All UI components, including the custom title bar and status bar, dynamically sync with the selected system theme.

## 🛠️ System Requirements
- **OS**: Windows 10 (Version 19041+) or Windows 11.
- **Runtime**: .NET 10.0.
- **Framework**: Windows App SDK 2.2.0.

## ⌨️ Keyboard Shortcuts

| Key | Context | Action |
|-----|---------|--------|
| **← / →** | Image | Previous / Next Image |
| | Text | Previous / Next Page |
| **↑ / ↓** | Global | Previous / Next File |
| **Home / End** | Image / Archive | First / Last Image |
| | Text / PDF | First / Last Page |
| | EPUB | Previous / Next Chapter |
| **Backspace** | Global | Go to Parent Folder |
| **Esc** | Global | Close Window / Exit Fullscreen |
| **F10** | Global | Toggle Maximize / Restore |
| **F11** | Global | Toggle Fullscreen |
| **`** | Global | Toggle Pin (Auto-Hide UI) |
| **D** | Global | Toggle Dark/Light Mode |
| **T** | Global | Toggle Always on Top |
| **Ctrl + O** | Global | Open File |
| **Ctrl + B** | Global | Toggle Sidebar |
| **Ctrl + S** | Global | Add to Favorites |
| **Ctrl + F** | Text / EPUB / PDF | Open Search |
| **+ / -**  | Global | Zoom In/Out / Font Size Up/Down (-/= also works) |
| |  | Blocked in Side-by-Side (2 page) mode |
| **0**  | Global | Fit to Window |
| **1**  | Global | Zoom Actual Size (Fit to width in PDF) |
| **G** | Text/EPUB | Go to Line (Right-click for search)|
| | PDF | Go to Page (Right-click for search)|
| **Space** | Image / EPUB | Toggle 2-Page View (Side-by-Side) |
| **V** | Text / EPUB | Toggle Vertical Mode (Tategaki) |
| **S** | Image | Toggle Sharpening (Right-click for settings) |
| **A** | Text | Toggle Simple text / Advanced rendering (Aozora, Markdown) mode |
| **B** | Text | Change Background Theme |
| **F** | Text | Toggle Default Fonts (Right-click for settings) |

## Mouse & Touch Navigation
- **Click/Touch Left Side**: Previous Page / Image
- **Click/Touch Right Side**: Next Page / Image
- **Ctrl + Mouse Wheel**: Zoom In/Out (panning with mouse right click and drag)
- **Pinch Zoom**: Support for pinch-to-zoom gestures on touchpads and touchscreens.
- **Control Direction Match**: When enabled in Side-by-Side mode (←), controls automatically invert to match the visual page progression.
  - Note: For images only. EPUBs always follow the standard text flow (horizontal/vertical).

## 🚀 Installation

### 📥 Download
You can download the latest version from the [Releases Page](https://github.com/kirinonakar/Uviewer/releases).
Unzip the file and run `Uviewer.exe`.

- [Microsoft Store](https://apps.microsoft.com/detail/9N6DL9Q4S6FK?hl=ko-kr&gl=KR&ocid=pdpshare)


### From Source
1. Clone the repository:
   ```bash
   git clone https://github.com/kirinonakar/Uviewer.git
   cd Uviewer
   ```
2. Open `Uviewer.slnx` in **Visual Studio 2022**.
   - Required workloads: **.NET Desktop Development**.
   - Required extension: **Windows App SDK**.
3. Build and Run:
   - Select `x64`.
   - Press **F5**.

### Build via Command Line
```bash
dotnet build -c Release --self-contained true
```

## 🏗️ Architecture
- **WinUI 3**: Modern UI framework.
- **Win2D**: Hardware-accelerated 2D graphics for images.
- **Windows.Data.Pdf**: Integrated OS-level PDF rendering.
- **PdfPig**: Used for parsing PDF tables of contents.
- **SharpCompress**: Archive extraction for ZIP, RAR, etc.
- **SevenZipExtractor**: High-performance extraction for `.7z` archives using native **7z.dll** (located in the `Libs` directory).

## 📝 License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

### Third-Party Licenses
- **7-Zip (7z.dll)**: Licensed under the **GNU LGPL**. For more information, please visit [www.7-zip.org](https://www.7-zip.org).

## 👤 Author
**kirinonakar** - [kirinonakar](https://github.com/kirinonakar)
