<p align="center">
  <img src="docs/images/hero_cinematic.gif" alt="PakWorkbench Banner" width="100%" />
</p>

<h1 align="center">
  <img src="https://github.com/user-attachments/assets/e17a1531-d059-40e5-bf9e-a608e3b20383" alt="PakWorkbench Logo" width="48" height="48" style="vertical-align: middle; margin-right: 10px;" />
  PakWorkbench
</h1>

<p align="center">
  <b>Interactive Asset Analysis Workstation, Modding Toolkit & High-Performance PAK Extractor<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 500 500" width="100%" height="100%">
  <defs>
    <!-- Cyan Neon Ragyogás Filter (Erősebb a kisméretű rendereléshez) -->
    <filter id="cyan-glow" x="-50%" y="-50%" width="200%" height="200%">
      <feGaussianBlur stdDeviation="4" result="blur1" />
      <feGaussianBlur stdDeviation="1" result="blur2" />
      <feMerge>
        <feMergeNode in="blur1" />
        <feMergeNode in="blur2" />
        <feMergeNode in="SourceGraphic" />
      </feMerge>
    </filter>

    <!-- Szöveg Árnyék Filter a Mikro Olvashatósághoz -->
    <filter id="text-shadow" x="-20%" y="-20%" width="140%" height="140%">
      <feDropShadow dx="0" dy="4" stdDeviation="4" flood-color="#000000" flood-opacity="0.9"/>
    </filter>

    <!-- Taktikai Szürke Háttér Átmenet (Probléma megoldása) -->
    <linearGradient id="tactical-bg" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#2D3A4F" /> <!-- Világosabb taktikai szürke -->
      <stop offset="100%" stop-color="#192231" /> <!-- Mélyebb taktikai szürke -->
    </linearGradient>

    <!-- Lencse Üveg Tükröződés Átmenet (Magas Kontraszt) -->
    <linearGradient id="lens-reflection" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#00E5FF" stop-opacity="0.4" />
      <stop offset="40%" stop-color="#141D2C" stop-opacity="0.95" />
      <stop offset="100%" stop-color="#0A1017" stop-opacity="1" />
    </linearGradient>

    <!-- Fogantyú Metál Átmenet -->
    <linearGradient id="handle-grad" x1="0%" y1="0%" x2="100%" y2="100%">
      <stop offset="0%" stop-color="#B0C2DE" />
      <stop offset="50%" stop-color="#64748B" />
      <stop offset="100%" stop-color="#334155" />
    </linearGradient>
  </defs>

  <!-- Alap Taktikai Szürke App Ikon Kártya -->
  <rect x="15" y="15" width="470" height="470" rx="80" ry="80" fill="url(#tactical-bg)" stroke="#3E4F6A" stroke-width="6" />
  <rect x="21" y="21" width="458" height="458" rx="74" ry="74" fill="none" stroke="#111823" stroke-width="4" />

  <!-- Sarok Szegecs Részletek -->
  <circle cx="52" cy="52" r="5" fill="#3E4F6A" />
  <circle cx="448" cy="52" r="5" fill="#3E4F6A" />
  <circle cx="52" cy="448" r="5" fill="#3E4F6A" />
  <circle cx="448" cy="448" r="5" fill="#3E4F6A" />

  <!-- Háttér Absztrakt Tech Vonalak (Sötétebb kék a szürke háttéren) -->
  <g stroke="#1E293B" stroke-width="3" fill="none">
    <path d="M 20,180 Q 250,260 480,140" />
    <path d="M 20,330 Q 250,220 480,380" />
  </g>

  <!-- Csillagkép Ragyogó Vonalak (Vastagabb a láthatóságért) -->
  <g stroke="#00E5FF" stroke-width="7" stroke-linecap="round" stroke-linejoin="round" filter="url(#cyan-glow)">
    <!-- Bal Csillagkép -->
    <polyline points="126,72 65,138 104,330 195,425" fill="none" />
    
    <!-- Jobb Felső Vonal -->
    <line x1="365" y1="78" x2="438" y2="138" />
    
    <!-- Jobb Alsó Vonal -->
    <line x1="310" y1="425" x2="395" y2="330" />
  </g>

  <!-- Csillagkép Csomópontok (Nagyobb pöttyök) -->
  <g fill="#00E5FF" filter="url(#cyan-glow)">
    <circle cx="126" cy="72" r="14" />
    <circle cx="65" cy="138" r="14" />
    <circle cx="104" cy="330" r="14" />
    <circle cx="195" cy="425" r="14" />
    
    <circle cx="365" cy="78" r="14" />
    <circle cx="438" cy="138" r="14" />
    <circle cx="310" cy="425" r="14" />
  </g>

  <!-- 3D Kocka (Magas Kontraszt & Vastagabb Vonalak) -->
  <g stroke-linecap="round" stroke-linejoin="round" transform="translate(-20, -32) scale(1.08)">
    <!-- Hátsó wireframe -->
    <g stroke="#64748B" stroke-width="3.5" fill="none" opacity="0.8">
      <line x1="190" y1="360" x2="250" y2="325" />
      <line x1="310" y1="360" x2="250" y2="325" />
      <line x1="250" y1="255" x2="190" y2="360" />
    </g>

    <!-- Elülső elsődleges wireframe (Világosabb Pala/Cyan Árnyalat) -->
    <g stroke="#F1F5F9" stroke-width="6" fill="none">
      <!-- Külső Hexagon Körvonal -->
      <polygon points="250,255 310,290 310,360 250,395 190,360 190,290" />
      <!-- Belső Fő Kocka Élek -->
      <line x1="250" y1="255" x2="250" y2="325" />
      <line x1="190" y1="290" x2="250" y2="325" />
      <line x1="310" y1="290" x2="250" y2="325" />
      <line x1="250" y1="325" x2="250" y2="395" />
    </g>
  </g>

  <!-- Professzionális Nagyító / Célkereszt (Megerősített Keret & Közép) -->
  <g transform="translate(-28, -50) scale(1.09)">
    <!-- Fogantyú -->
    <rect x="345" y="340" width="22" height="54" rx="11" fill="url(#handle-grad)" stroke="#94A3B8" stroke-width="2" transform="rotate(-45 345 340)" />
    
    <!-- Külső Lencse Fém Gyűrű -->
    <circle cx="305" cy="298" r="58" fill="none" stroke="#94A3B8" stroke-width="12" />
    <circle cx="305" cy="298" r="64" fill="none" stroke="#0D131D" stroke-width="3" />
    <circle cx="305" cy="298" r="52" fill="none" stroke="#FFFFFF" stroke-width="2.5" />
    
    <!-- Üveg Lencse Kitöltés -->
    <circle cx="305" cy="298" r="51" fill="url(#lens-reflection)" />
    
    <!-- Belső Narancssárga Célkereszt Gyűrű (Élénk & Vastagabb) -->
    <circle cx="305" cy="298" r="40" fill="none" stroke="#FF6D00" stroke-width="5" />
    
    <!-- Üveg Tükröződés Flare Ív -->
    <path d="M 268,268 A 47 47 0 0 1 342,268" fill="none" stroke="#FFFFFF" stroke-width="4" stroke-linecap="round" opacity="0.6" />

    <!-- Célkereszt Szálkereszt Vonalak (Cyan Ragyogás) -->
    <g stroke="#00E5FF" stroke-width="5" stroke-linecap="round" filter="url(#cyan-glow)">
      <line x1="305" y1="270" x2="305" y2="282" />
      <line x1="305" y1="314" x2="305" y2="326" />
      <line x1="277" y1="298" x2="289" y2="298" />
      <line x1="321" y1="298" x2="333" y2="298" />
    </g>

    <!-- Célkereszt Középső Ragyogó Narancssárga Pont -->
    <circle cx="305" cy="298" r="11" fill="#FF6D00" filter="url(#cyan-glow)" />
    <circle cx="305" cy="298" r="10" fill="#FF8A00" />
    <circle cx="305" cy="298" r="4" fill="#FFFFFF" />
  </g>

  <!-- Fő Szöveg: "PAK" (Magas Kontrasztú Narancs + Árnyék) -->
  <text x="250" y="138" 
        text-anchor="middle" 
        font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif" 
        font-weight="900" 
        font-size="86" 
        fill="#FF7700" 
        filter="url(#text-shadow)"
        letter-spacing="4">PAK</text>

  <!-- Alcím Szöveg: "WORKBENCH" (Tiszta Fehér/Pala + Árnyék) -->
  <text x="250" y="184" 
        text-anchor="middle" 
        font-family="-apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif" 
        font-weight="800" 
        font-size="26" 
        fill="#F1F5F9" 
        filter="url(#text-shadow)"
        letter-spacing="9">WORKBENCH</text>
</svg>
 for Arma Reforger</b>
</p>

<p align="center">
  <i>Explore • Inspect • Search • Diff • Graph • Live Sync • Extract</i>
</p>

<p align="center">
  <a href="https://github.com/Rendszerguru/PakWorkbench/releases"><img src="https://img.shields.io/badge/Release-v1.0.0-blue?style=for-the-badge&logo=github" alt="Release" /></a>
  <img src="https://img.shields.io/badge/Engine-Enfusion-orange?style=for-the-badge" alt="Engine" />
  <img src="https://img.shields.io/badge/Framework-.NET%208.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2F11-0078D6?style=for-the-badge&logo=windows" alt="Platform" />
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
  <b>🚀 PakWorkbench</b>
</p>

<p align="center">
  <i>Explore the package. Understand the assets. Build better mods.</i>
</p>

<p align="center">
  <sub>Released under the Mozilla Public License 2.0 (MPL-2.0)</sub>
</p>
