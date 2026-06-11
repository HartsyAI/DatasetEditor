# Extension Development Guide

**Status**: TODO - Phase 3
**Last Updated**: 2025-12-10

## Overview

This guide provides comprehensive instructions for developing extensions for Dataset Studio. Extensions allow you to add new capabilities, viewers, tools, and integrations to the platform.

## Table of Contents

1. [Getting Started](#getting-started)
2. [Extension Structure](#extension-structure)
3. [Manifest File](#manifest-file)
4. [Development Workflow](#development-workflow)
5. [Core APIs](#core-apis)
6. [Best Practices](#best-practices)
7. [Testing](#testing)
8. [Distribution](#distribution)
9. [Troubleshooting](#troubleshooting)

## Getting Started

### Prerequisites

- TODO: Phase 3 - Document .NET version requirements
- TODO: Phase 3 - Document Visual Studio / VS Code setup requirements
- TODO: Phase 3 - Document SDK package installation
- TODO: Phase 3 - Document tooling requirements

### Quick Start

TODO: Phase 3 - Create quick start template

Steps:
1. Install the Extension SDK NuGet package
2. Create a new class library project
3. Create your extension class inheriting from `BaseExtension`
4. Create an `extension.manifest.json` file
5. Build and deploy

## Extension Structure

### Directory Layout

```
MyExtension/
├── extension.manifest.json    # Extension metadata and configuration
├── MyExtension.csproj         # Project file
├── src/
│   ├── MyExtension.cs         # Main extension class
│   ├── Features/
│   │   ├── Viewer.cs          # Feature implementations
│   │   └── Tools.cs
│   └── Resources/
│       ├── icons/             # Extension icons
│       └── localization/       # Localization files
├── tests/
│   └── MyExtension.Tests.cs   # Unit tests
├── README.md                   # Extension documentation
└── LICENSE                     # License file
```

### TODO: Phase 3 - Provide Detailed Structure Documentation

Details needed:
- What goes in each directory
- File naming conventions
- Resource file guidelines
- Test project structure
- Documentation requirements

## Manifest File

### File Format

The `extension.manifest.json` file defines your extension's metadata, capabilities, and configuration.

### Example Manifest

```json
{
  "schemaVersion": 1,
  "id": "my-awesome-extension",
  "name": "My Awesome Extension",
  "version": "1.0.0",
  "description": "A helpful extension for Dataset Studio",
  "author": {
    "name": "Your Name",
    "email": "you@example.com"
  },
  "license": "MIT",
  "homepage": "https://example.com/my-extension",
  "repository": "https://github.com/username/my-extension",
  "tags": ["viewer", "dataset"],
  "entryPoint": "MyNamespace.MyExtensionClass",
  "capabilities": {
    "dataset-viewer": {
      "displayName": "Dataset Viewer",
      "description": "Custom viewer for datasets",
      "category": "viewers",
      "parameters": ["datasetId", "viewMode"]
    }
  },
  "configuration": {
    "schema": {
      "type": "object",
      "properties": {
        "enableFeature": {
          "type": "boolean",
          "default": true
        }
      }
    }
  },
  "requiredPermissions": [
    "dataset.read",
    "dataset.write"
  ],
  "minimumCoreVersion": "1.0.0",
  "activationEvents": [
    "onDatasetOpen",
    "onCommand:my-extension.showViewer"
  ],
  "platforms": ["Windows", "Linux", "macOS"]
}
```

### TODO: Phase 3 - Document Manifest Schema

Schema documentation needed:
- All manifest fields and types
- Required vs optional fields
- Allowed values for enumerations
- Validation rules
- JSON Schema definition
- Version migration guide

## Development Workflow

### TODO: Phase 3 - Create Development Workflow Documentation

Documentation needed:

1. **Project Setup**
   - Creating extension project from template
   - Configuring project dependencies
   - Setting up build process
   - Configuring debugging

2. **Extension Development**
   - Implementing BaseExtension class
   - Using the extension context
   - Accessing core services
   - Handling configuration
   - Implementing logging

3. **Local Testing**
   - Loading extension in development mode
   - Debugging extensions
   - Running with test datasets
   - Checking logs

4. **Version Management**
   - Versioning strategy (semantic versioning)
   - Changelog requirements
   - Migration guide for breaking changes

## Core APIs

### TODO: Phase 3 - Document Core Extension APIs

API documentation needed:

1. **BaseExtension Class**
   ```csharp
   // TODO: Phase 3 - Document abstract methods that must be implemented
   // TODO: Phase 3 - Document lifecycle methods
   // TODO: Phase 3 - Document event handlers
   ```

2. **ExtensionContext Interface**
   ```csharp
   // TODO: Phase 3 - Document context properties
   // TODO: Phase 3 - Document service resolution methods
   // TODO: Phase 3 - Document event subscription methods
   ```

3. **Core Services Available**
   ```csharp
   // TODO: Phase 3 - Document available services
   // - IDatasetService
   // - IStorageService
   // - INotificationService
   // - ILoggingService
   // - ICachingService
   // - etc.
   ```

4. **Extension Request/Response Model**
   ```csharp
   // TODO: Phase 3 - Document request/response structures
   // TODO: Phase 3 - Document error handling
   // TODO: Phase 3 - Document async patterns
   ```

### TODO: Phase 3 - Add API Code Examples

Examples needed:
- Basic extension skeleton
- Using core services
- Handling configuration
- Logging and error handling
- Async operations
- Event handling

## Best Practices

### TODO: Phase 3 - Document Extension Best Practices

Best practices documentation needed:

1. **Code Quality**
   - Code style guidelines
   - Naming conventions
   - Documentation requirements
   - Async/await patterns
   - Exception handling

2. **Performance**
   - Resource management
   - Caching strategies
   - Async operations
   - Memory leak prevention
   - Large dataset handling

3. **Security**
   - Input validation
   - Permission checking
   - Secure configuration storage
   - Data encryption
   - Third-party library vetting

4. **User Experience**
   - Progress indication
   - Error messaging
   - Localization support
   - Accessibility
   - Configuration validation

5. **Extension Compatibility**
   - Version compatibility management
   - Graceful degradation
   - Platform-specific handling
   - Dependency management

## Testing

### TODO: Phase 3 - Create Testing Guide

Testing documentation needed:

1. **Unit Testing**
   - Testing framework recommendations
   - Mocking core services
   - Test fixtures and helpers
   - Example unit tests

2. **Integration Testing**
   - Testing with core system
   - Test dataset creation
   - Functional test examples
   - Performance benchmarks

3. **Compatibility Testing**
   - Testing multiple core versions
   - Platform-specific testing (Windows, Linux, macOS)
   - Testing with different configurations

## Distribution

### TODO: Phase 3 - Create Distribution Guide

Distribution documentation needed:

1. **Publishing**
   - Extension marketplace submission
   - Versioning and releases
   - Release notes format
   - Security review process

2. **Installation**
   - User installation methods
   - Marketplace installation
   - Manual installation from ZIP
   - Version updates

3. **Support**
   - Documentation requirements
   - Issue tracking setup
   - User support guidelines
   - Feedback mechanisms

## Troubleshooting

### TODO: Phase 3 - Create Troubleshooting Guide

Troubleshooting section needed:

1. **Common Issues**
   - Extension not loading
   - Manifest validation errors
   - Service resolution failures
   - Configuration problems
   - Permission denied errors

2. **Debugging**
   - Debug output inspection
   - Attaching debugger
   - Common breakpoints
   - Log analysis

3. **Performance Issues**
   - Profiling extensions
   - Identifying bottlenecks
   - Memory leak detection
   - Optimization techniques

## Related Documentation

- See `REFACTOR_PLAN.md` Phase 3 for extension system architecture details
- See `src/Extensions/SDK/BaseExtension.cs` for base class reference
- See `src/Extensions/SDK/ExtensionMetadata.cs` for metadata structure
- See built-in extensions in `src/Extensions/BuiltIn/` for examples

## Questions and Support

TODO: Phase 3 - Add support channels:
- GitHub Issues: [Link]
- Discussion Forum: [Link]
- Email: [Link]
