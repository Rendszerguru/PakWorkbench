<p align="center">
  <img src="docs/images/hero_cinematic.gif" alt="PakWorkbench Banner" width="100%" />
</p>

<h1 align="center"><sub><img src="https://github.com/user-attachments/assets/e17a1531-d059-40e5-bf9e-a608e3b20383" alt="PakWorkbench Logo" width="48" height="48"></sub> PakWorkbench</h1>

<p align="center">
  <b>Interactive Asset Analysis Workstation, Modding Toolkit & High-Performance PAK Extractor for Arma Reforger</b>
</p>

<p align="center">
  <i>Explore • Inspect • Search • Diff • Graph • Live Sync • Extract</i>
</p>

<p align="center">
  <a href="https://github.com/Rendszerguru/PakWorkbench/releases"><img src="https://img.shields.io/badge/Release-v1.0.0-blue?style=for-the-badge&logo=github" alt="Release" /></a>
  <a href="https://enfusionengine.com"><img src="https://img.shields.io/badge/Engine-Enfusion-orange?style=for-the-badge" alt="Engine" /></a>
  <a href="https://dotnet.microsoft.com/en-us/download/dotnet/8.0"><img src="https://img.shields.io/badge/Framework-.NET%208.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0" /></a>
  <a href="https://www.microsoft.com/windows"><img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows" alt="Platform" /></a>
  <a href="https://modiscover.eu"><img src="https://img.shields.io/badge/MoDiscover-Integrated-purple?style=for-the-badge" alt="MoDiscover" /></a>
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-MPL--2.0-green?style=for-the-badge" alt="License" /></a>
</p>

<p align="center">
  <a href="#-what-is-pakworkbench"><b>Overview</b></a> •
  <a href="#-key-features"><b>Features</b></a> •
  <a href="#-high-performance-extraction-engine"><b>Extraction Engine</b></a> •
  <a href="#-command-line-interface-cli"><b>CLI</b></a> •
  <a href="#-requirements"><b>Requirements</b></a> •
  <a href="#-license"><b>License</b></a>
</p>

---

<p align="center">
  <img src="https://github.com/user-attachments/assets/ba7d345d-5aca-426c-a4a8-427bac1a4e0b" alt="PakWorkbench Interface Preview" width="100%" />
</p>

---

## ⚡ What is PakWorkbench?

**PakWorkbench** is a modern analysis and support workstation built specifically **for Arma Reforger**, designed to let you rapidly explore, inspect, analyze, and extract the contents of `.pak` files without first unpacking entire archives to disk.

Rather than acting as a conventional package extractor or an asset editor, PakWorkbench provides an **intelligent visual environment** for working with Enfusion assets. It resolves relationships between referenced assets, exposes dependencies, and helps identify missing files and potential modding issues quickly.

> **💡 The core advantage:** *No unnecessary extraction, no wasted disk space. Open, inspect, and understand your assets immediately.*

---

## ⚡ Key Features

### 📂 Intelligent Asset Browser & Preview

* **Browse Without Extraction:** Open multi-gigabyte `.pak` archives and navigate their file structure without first extracting them to disk.
* **Multi-PAK & Append Mode:** Dynamically add additional `.pak` files and dependencies to an existing workspace, creating a unified virtual file system for analyzing cross-references and overrides.
* **Instant File Preview:** Quickly inspect text and source files, including large files, without unnecessary extraction.
* **Workbench-Style Syntax Highlighting (`XFusion`):** A custom high-performance rendering engine designed around the visual style of Arma Reforger Workbench, with tailored syntax support for Enfusion Script (`.c`), Enfusion Data (`.et`, `.layout`, `.conf`, `.emat`, `.gproj`, `.physmat`, etc.), and XML syntax used by formats such as `.st` and `.svg`.
* **Interactive Context & Search:** Preview search results directly inside the `XFusion` view with precise line numbers, match indicators, and multi-line context highlighting.
* **Automatic Vanilla Content Resolution:** When analyzing mods, the system can automatically resolve references and dependencies originating from the base game content.
* **Binary Hex View:** Inspect raw binary and non-text files directly through a dedicated hexadecimal view.

---

### 🧾 Diff Workspace

* **PAK ↔ DISK Comparison:** Compare original assets contained inside a `.pak` archive directly against modified versions on disk.
* **Side-by-Side Visual Diffing:** Compare files with clear line-by-line and character/word-level difference highlighting, powered by `XFusion` and `DiffPlex`.
* **Workbench-Style Diff View:** Differences are presented using the same familiar visual language as the Workbench-inspired `XFusion` editor.
* **Binary Protection:** Automatically detects non-text files and prevents invalid text comparisons, preserving workspace stability.

---

### 🎮 Arma Reforger Tools Integration

* **Open in Workbench:** Open supported Enfusion assets such as `.xob`, `.edds`, `.anm`, `.c`, and `.et` directly in the *Arma Reforger Workbench*.
* **Automatic Workbench Detection:** Automatically detects the installed Workbench and selects the appropriate tool for the asset.
* **Steam Integration:** Detects and starts Steam when required for Workbench.
* **Sandbox & Dependencies:** Creates a temporary Workbench project and automatically resolves required dependencies.

#### 🔄 Live Sync & Auto-Validation

* **Automated Script Syncing:** Actively monitors your source `.c` files for changes using a file system watcher and automatically synchronizes them to your target Workbench project directory.
* **Real-time NET API Validation:** Communicates directly with the Arma Reforger Workbench via a TCP NET API connection to automatically trigger script compilation and validation upon file save.
* **Smart Editor Integration & External Parameterization:** Automatically discovers installed IDEs and code editors (Visual Studio, VS Code, Rider, Neovim, Notepad++) and utilizes flexible external parameters/arguments to jump directly to the exact file, line, and column when an error is clicked.
* **Interactive Output Log:** Parses Enfusion script errors and warnings into an interactive view where double-clicking an entry instantly focuses the problematic code block in your external code editor.

---

### 🔍 Smart Search & StringTable Resolution

* **Extension & Exclusion Filtering:** Target specific file types such as `.c`, `.conf`, and `.emat`, while excluding unwanted directories such as `deprecated` or `test`.
* **Multi-Line Block Search:** Search across line breaks by combining a block start, such as a class name, with a condition or other content inside the block.
* **Wildcards & RegEx:** Supports `*` and `?` wildcard searches as well as advanced regular expressions for deeper analysis.
* **Context-Aware Results:** Search results provide structural context instead of showing only the matching line, such as the parent prefab or associated shader.
* **StringTable [ST] Resolution:** Search for a visible UI string and automatically resolve its corresponding localization key from `.conf` StringTable data, such as `#AR-MainMenu_Title`, then locate its usages across scripts and assets including `.c`, `.layout`, and `.et` files.

---

### 🕸 Interactive Dependency Graph

* **Visual Asset Relationships:** Explore how an object, vehicle, script, or other asset is connected to the components it depends on.
* **Missing Reference Detection:** Clearly identify references to assets that are missing from the available package content.
* **Flexible Graph Layouts:** Use different graph layouts, including hierarchical, physics-based, and grid-based views, to make complex relationships easier to understand.

---

### ⊕ ModDiscover Integration

* **Integrated Browser Experience:** Access the `modiscover.eu` platform directly through a dedicated integrated panel inside PakWorkbench.
* **One-Click Dependency Analysis:** Right-click a mod's root node and use **View Dependencies / Graph** to analyze its external project dependencies.
* *Special thanks to **¼Sebi** for creating the MoDiscover web interface and allowing its integration into PakWorkbench!*

---

## 📦 High-Performance Extraction Engine

PakWorkbench is not only an interactive asset inspection environment. It also includes a **high-performance extraction engine** for Enfusion `.pak` archives, optimized for **.NET 8.0**.

### ✨ Key Characteristics

* **Maximum Throughput:** Multi-threaded data processing designed to utilize available CPU resources efficiently.
* **Memory Efficient:** Stream-based extraction for stable handling of large, multi-gigabyte archives.
* **Intelligent Workflow:** Automatically determines the appropriate operating mode based on the execution environment, including GUI, command-line, and batch processing workflows.

### 🚀 Extraction Methods

1. **Direct Archive Processing:** Pass one or more `.pak` files directly to `PakWorkbench.exe` to extract them automatically.
2. **Drag & Drop:** Drag `.pak` files directly onto `PakWorkbench.exe` for immediate extraction through the command-line argument interface.
3. **File Association:** Associate `.pak` files with PakWorkbench and launch extraction by double-clicking an archive.
4. **Batch Directory Processing:** Pass a directory containing `.pak` files to recursively locate and process all matching archives.
5. **Manual GUI Mode:** Launch `PakWorkbench.exe` without command-line arguments to open the **PakWorkbench GUI** for interactive asset inspection.

---

## 💻 Command-Line Interface (CLI)

PakWorkbench provides an automation-friendly command-line interface suitable for scripting, batch processing, CI/CD workflows, and fast archive operations.

```bash
# Display basic archive information
PakWorkbench.exe inspect data.pak

# List all files contained in the archive
PakWorkbench.exe inspect data.pak --tree

# Display the PAK/IFF chunk layout and archive index information
PakWorkbench.exe inspect data.pak --chunks

# Extract the complete archive
PakWorkbench.exe extract data.pak

# Extract to a custom output directory
PakWorkbench.exe extract data.pak --output "D:\Extracted"

# Extract a specific file from the archive
PakWorkbench.exe extract data.pak --file "scripts/Game/Component.c"
```

### 📦 Batch Processing

PakWorkbench also supports batch extraction by passing one or more `.pak` files or directories containing `.pak` archives:

```bash
# Process a single archive
PakWorkbench.exe data.pak

# Process multiple archives
PakWorkbench.exe data1.pak data2.pak data3.pak

# Process all .pak files in a directory tree
PakWorkbench.exe "D:\ReforgerPaks"
```

Use `PakWorkbench.exe --help` to display the available commands and options.

---

## 📋 Requirements

* Windows 10 or Windows 11
* [.NET 8.0 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
* Arma Reforger / Arma Reforger Tools — required only for optional Workbench integration

---

## 📜 License

**PakWorkbench** is licensed under the **Mozilla Public License 2.0 (MPL-2.0)**.

* [LICENSE](LICENSE)
* [Mozilla Public License 2.0](https://www.mozilla.org/en-US/MPL/2.0/)

The source code may be used, modified, forked, and redistributed in accordance with the MPL-2.0 terms.

Modified versions must retain the applicable copyright and license notices and must not be presented as an official release of the original **PakWorkbench** project.

### Attribution

Forks and derivative projects based on PakWorkbench should clearly credit the original project:

> **Based on PakWorkbench**

Original project: https://github.com/Rendszerguru/PakWorkbench

The **PakWorkbench** name, logo, and visual identity are not covered by the MPL-2.0 license and may not be used in a way that falsely implies official affiliation or endorsement.

---

## 🔗 Links

* **Releases:** [PakWorkbench Releases](https://github.com/Rendszerguru/PakWorkbench/releases)

---

<p align="center">
  <b><sub><img src="https://github.com/user-attachments/assets/e17a1531-d059-40e5-bf9e-a608e3b20383" alt="PakWorkbench Logo" width="18" height="18"></sub> PakWorkbench</b>
</p>

<p align="center">
  <i>Explore the package. Understand the assets. Build better mods.</i>
</p>

<p align="center">
  <sub>Released under the Mozilla Public License 2.0 (MPL-2.0)</sub>
</p>
