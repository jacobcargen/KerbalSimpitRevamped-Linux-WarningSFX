#!/bin/bash
# Build and Install KerbalSimpit for Linux
# This script handles the complete build process including symlinks and dependencies

set -e  # Exit on error

# Configuration
KSP_DIR="/home/jacob-dev/.steam/steam/steamapps/common/Kerbal Space Program"
BUILD_DIR="$(pwd)"

echo "========================================"
echo "KerbalSimpit Linux Build & Install"
echo "========================================"
echo ""

# Check if we're in the right directory
if [ ! -f "Makefile" ]; then
    echo "❌ Error: Makefile not found. Are you in the KerbalSimpitRevamped directory?"
    exit 1
fi

# Check if KSP directory exists
if [ ! -d "$KSP_DIR" ]; then
    echo "❌ KSP directory not found: $KSP_DIR"
    echo "Please update KSP_DIR in this script to point to your KSP installation"
    exit 1
fi

# Check for required tools
echo "Checking dependencies..."
if ! command -v m4 &> /dev/null; then
    echo "❌ m4 not found. Installing..."
    sudo apt install m4 -y
fi

if ! command -v msbuild &> /dev/null; then
    echo "❌ msbuild not found. Please install mono-complete:"
    echo "   sudo apt install mono-complete"
    exit 1
fi

echo "✓ All dependencies found"
echo ""

# Create symlinks if they don't exist
echo "Setting up symlinks..."
if [ ! -L "KerbalSpaceProgram" ]; then
    echo "Creating symlink: KerbalSpaceProgram -> $KSP_DIR"
    ln -s "$KSP_DIR" KerbalSpaceProgram
else
    echo "✓ KerbalSpaceProgram symlink already exists"
fi

if [ ! -L "install" ]; then
    echo "Creating symlink: install -> $KSP_DIR/GameData"
    ln -s "$KSP_DIR/GameData" install
else
    echo "✓ install symlink already exists"
fi

echo ""

# Create version-info.m4 if it doesn't exist
if [ ! -f "version-info.m4" ]; then
    echo "Creating version-info.m4 from VERSION.txt..."
    
    # Read version info from VERSION.txt
    if [ -f "VERSION.txt" ]; then
        source VERSION.txt
        cat > version-info.m4 << EOF
dnl version-info.m4 for KerbalSimpit
dnl Generated from VERSION.txt
define(\`MAJORVER', \`${MAJOR}')
define(\`MINORVER', \`${MINOR}')
define(\`PATCHVER', \`${PATCH}')
define(\`BUILDVER', \`${BUILD}')
define(\`KSPMAJOR', \`${KSPMAJOR}')
define(\`KSPMINOR', \`${KSPMINOR}')
define(\`KSPPATCH', \`${KSPPATCH}')
EOF
        echo "✓ version-info.m4 created"
    else
        echo "⚠ VERSION.txt not found, using defaults"
        cat > version-info.m4 << EOF
dnl version-info.m4 for KerbalSimpit
define(\`MAJORVER', \`2')
define(\`MINORVER', \`3')
define(\`PATCHVER', \`1')
define(\`BUILDVER', \`0')
define(\`KSPMAJOR', \`1')
define(\`KSPMINOR', \`12')
define(\`KSPPATCH', \`2')
EOF
    fi
fi

echo ""

# Clean previous build
echo "Cleaning previous build..."
make clean 2>/dev/null || true
echo ""

# Build
echo "========================================"
echo "Building KerbalSimpit..."
echo "========================================"

# Run make install (may fail on localization copy, we'll handle it after)
make install 2>&1 | grep -v "cannot stat.*Localisation" || true

echo ""
echo "Copying additional files..."

# Copy DLL manually to ensure it's installed
if [ -f "Bin/KerbalSimpit.dll" ]; then
    mkdir -p "$KSP_DIR/GameData/KerbalSimpit"
    cp Bin/KerbalSimpit.dll "$KSP_DIR/GameData/KerbalSimpit/"
    echo "✓ Copied KerbalSimpit.dll"
else
    echo "❌ Build failed - KerbalSimpit.dll not found in Bin/"
    exit 1
fi

# Copy localization files to both folders (KSP looks in different places)
if [ -d "distrib/Localisation" ]; then
    mkdir -p "$KSP_DIR/GameData/KerbalSimpit/Localisation"
    mkdir -p "$KSP_DIR/GameData/KerbalSimpit/Localisations"
    cp -r distrib/Localisation/* "$KSP_DIR/GameData/KerbalSimpit/Localisation/" 2>/dev/null || true
    cp -r distrib/Localisation/* "$KSP_DIR/GameData/KerbalSimpit/Localisations/" 2>/dev/null || true
    echo "✓ Copied localization files to both Localisation and Localisations folders"
fi

# Copy PluginData
if [ -d "distrib/PluginData" ]; then
    mkdir -p "$KSP_DIR/GameData/KerbalSimpit/PluginData"
    cp -r distrib/PluginData/* "$KSP_DIR/GameData/KerbalSimpit/PluginData/" 2>/dev/null || true
    echo "✓ Copied PluginData"
fi

# Verify installation
echo ""
echo "========================================"
echo "Verifying installation..."
echo "========================================"

if [ -f "$KSP_DIR/GameData/KerbalSimpit/KerbalSimpit.dll" ]; then
    DLL_SIZE=$(ls -lh "$KSP_DIR/GameData/KerbalSimpit/KerbalSimpit.dll" | awk '{print $5}')
    DLL_DATE=$(ls -l "$KSP_DIR/GameData/KerbalSimpit/KerbalSimpit.dll" | awk '{print $6, $7, $8}')
    echo "✓ KerbalSimpit.dll installed successfully"
    echo "  Size: $DLL_SIZE"
    echo "  Date: $DLL_DATE"
    echo "  Location: $KSP_DIR/GameData/KerbalSimpit/"
else
    echo "❌ Installation failed - DLL not found in GameData"
    exit 1
fi

echo ""
echo "========================================"
echo "✅ BUILD AND INSTALLATION COMPLETE!"
echo "========================================"
echo ""
echo "KerbalSimpit has been built and installed to:"
echo "  $KSP_DIR/GameData/KerbalSimpit/"
echo ""
echo "Features enabled:"
echo "  ✓ X11 keyboard simulation (all keys supported)"
echo "  ✓ Full VK code to X11 KeySym mapping"
echo "  ✓ F1-F12, A-Z, 0-9, arrows, numpad, modifiers"
echo "  ✓ Fallback KSP API mode (F1/F2 only)"
echo ""
echo "You can now launch KSP and test keyboard emulation from your Arduino."
echo ""
