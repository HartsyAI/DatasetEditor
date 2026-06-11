# Built-In Extensions

**Status**: TODO - Phase 3
**Last Updated**: 2025-12-10

## Overview

This directory contains the built-in extensions that are shipped with Dataset Studio. These extensions provide core functionality and serve as reference implementations for the extension system.

## Table of Contents

1. [Purpose](#purpose)
2. [Available Extensions](#available-extensions)
3. [Architecture](#architecture)
4. [Built-In Extension List](#built-in-extension-list)
5. [Development Workflow](#development-workflow)
6. [Integration with Core](#integration-with-core)

## Purpose

Built-in extensions demonstrate best practices for extending Dataset Studio and provide essential functionality that is part of the standard application. These extensions:

- Provide core viewers, tools, and utilities
- Serve as reference implementations for custom extension developers
- Enable modular architecture by separating core features into extensions
- Are maintained and tested by the Dataset Studio team
- Are always available in every installation

## Available Extensions

The following built-in extensions are planned for Phase 3 implementation:

### TODO: Phase 3 - List Built-In Extensions

Each extension subdirectory contains:
- `extension.manifest.json` - Extension metadata and configuration
- Source code implementing the extension functionality
- Unit tests for the extension
- Documentation and examples

Current structure:
```
BuiltIn/
├── CoreViewer/      # TODO: Phase 3 - Basic dataset viewer
├── Editor/          # TODO: Phase 3 - Dataset editing tools
├── AITools/         # TODO: Phase 3 - AI/ML integration tools
├── AdvancedTools/   # TODO: Phase 3 - Advanced dataset manipulation
└── Creator/         # TODO: Phase 3 - Dataset creation tools
```

## Architecture

### TODO: Phase 3 - Document Built-In Extension Architecture

Built-in extensions follow this architecture:

1. **Standard Structure**
   - All built-in extensions inherit from `BaseExtension`
   - Each extension implements required lifecycle methods
   - Extensions are self-contained and modular

2. **Capabilities**
   - Each extension declares its capabilities in the manifest
   - Capabilities are registered with the core system
   - Extensions can depend on other extensions' capabilities

3. **Loading**
   - Built-in extensions are loaded during application startup
   - They are loaded before user extensions
   - Extensions can specify their load order/priority

4. **Testing**
   - All built-in extensions have comprehensive unit tests
   - Integration tests verify extension interactions
   - Reference implementations are well-documented

## Built-In Extension List

### CoreViewer

**Status**: TODO - Phase 3

**Purpose**: Provides the basic dataset viewer functionality

**Key Features**:
- TODO: Display dataset contents in grid/table format
- TODO: Support for different data types (numbers, strings, dates, etc.)
- TODO: Basic sorting and filtering
- TODO: Column visibility toggle
- TODO: Pagination for large datasets

**Manifest**: `CoreViewer/extension.manifest.json`
**Entry Point**: TODO: Define entry point class

### Editor

**Status**: TODO - Phase 3

**Purpose**: Provides dataset editing and manipulation tools

**Key Features**:
- TODO: Add/remove rows and columns
- TODO: Edit cell values
- TODO: Find and replace functionality
- TODO: Undo/redo support
- TODO: Data type conversion tools

**Manifest**: `Editor/extension.manifest.json`
**Entry Point**: TODO: Define entry point class

### AITools

**Status**: TODO - Phase 3

**Purpose**: Provides AI and machine learning integration tools

**Key Features**:
- TODO: Data preprocessing pipelines
- TODO: Statistical analysis tools
- TODO: Model integration support
- TODO: Prediction and inference tools
- TODO: Data transformation utilities

**Manifest**: `AITools/extension.manifest.json`
**Entry Point**: TODO: Define entry point class

### AdvancedTools

**Status**: TODO - Phase 3

**Purpose**: Provides advanced dataset manipulation and analysis

**Key Features**:
- TODO: Data pivoting and reshaping
- TODO: Aggregation and grouping
- TODO: Data validation and profiling
- TODO: Advanced filtering and querying
- TODO: Data quality assessment

**Manifest**: `AdvancedTools/extension.manifest.json`
**Entry Point**: TODO: Define entry point class

### Creator

**Status**: TODO - Phase 3

**Purpose**: Provides tools for creating new datasets

**Key Features**:
- TODO: Import from various formats (CSV, Excel, JSON, etc.)
- TODO: Data schema definition
- TODO: Sample data generation
- TODO: Format conversion utilities
- TODO: Batch import support

**Manifest**: `Creator/extension.manifest.json`
**Entry Point**: TODO: Define entry point class

## Development Workflow

### TODO: Phase 3 - Document Development Workflow

To develop or modify a built-in extension:

1. **Edit the Extension**
   - Navigate to the extension directory
   - Update the source code
   - Update the extension manifest if capabilities change

2. **Test the Extension**
   - Run unit tests: `dotnet test`
   - Test in development mode
   - Verify integration with core system

3. **Document Changes**
   - Update extension documentation
   - Add comments explaining significant changes
   - Update the changelog

4. **Submit for Review**
   - Create a pull request with changes
   - Include test results and documentation
   - Follow code review guidelines

## Integration with Core

Built-in extensions integrate with the core Dataset Studio system through:

1. **Dependency Injection**
   - Extensions receive core services via constructor
   - Services include data access, storage, logging, etc.
   - Services are registered at application startup

2. **Event System**
   - Extensions can subscribe to core events
   - Extensions can raise events for other components
   - Event handling follows publisher/subscriber pattern

3. **Configuration**
   - Extensions read configuration from manifest and settings
   - Settings can be overridden by users
   - Configuration is persisted and loaded on startup

4. **Permissions**
   - Extensions declare required permissions in manifest
   - User must approve permissions before extension loads
   - Permissions are checked at runtime

## Related Documentation

- **Extension Development Guide**: `src/Extensions/SDK/DevelopmentGuide.md`
- **Extension SDK**: `src/Extensions/SDK/` directory
- **User Extensions**: `src/Extensions/UserExtensions/README.md`
- **Refactor Plan**: `REFACTOR_PLAN.md` Phase 3 for detailed implementation plan

## Status Notes

This document represents the planned structure for built-in extensions. The implementation will proceed according to the roadmap in `REFACTOR_PLAN.md` Phase 3. Each extension will be implemented, tested, and documented during Phase 3 of the project.

---

**Note**: All built-in extensions are marked as "TODO: Phase 3" and will be implemented during Phase 3 of the refactoring project. See `REFACTOR_PLAN.md` for the detailed implementation schedule.
