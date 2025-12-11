# User Extensions

**Status**: TODO - Phase 3+
**Last Updated**: 2025-12-10

This directory is for third-party extensions developed by users and community members.

## Overview

User extensions allow you to extend Dataset Studio with custom functionality without modifying the core application. This directory provides a location for installing and managing third-party extensions.

## Table of Contents

1. [Installation](#installation)
2. [Directory Structure](#directory-structure)
3. [Extension Sources](#extension-sources)
4. [Getting Started with Extensions](#getting-started-with-extensions)
5. [Extension Security](#extension-security)
6. [Troubleshooting](#troubleshooting)
7. [Support](#support)

## Installation

### From Extension Marketplace

TODO: Phase 3 - Implement marketplace installation

```
1. Open Dataset Studio Settings
2. Navigate to Extensions > Marketplace
3. Search for desired extension
4. Click "Install"
5. Reload application or restart Dataset Studio
```

### From ZIP File

TODO: Phase 3 - Implement ZIP installation

```
1. Download extension ZIP file
2. Extract to a new subdirectory in UserExtensions/
3. Verify extension.manifest.json exists
4. Restart Dataset Studio to load the extension
```

Example directory structure after ZIP installation:
```
UserExtensions/
├── my-awesome-extension/
│   ├── extension.manifest.json
│   ├── my-awesome-extension.dll
│   └── dependencies/
└── another-extension/
    ├── extension.manifest.json
    └── another-extension.dll
```

### From Git Repository

TODO: Phase 3 - Implement Git-based installation

```
1. Open terminal in UserExtensions directory
2. Clone repository:
   git clone https://github.com/user/extension-name
3. Build extension (if necessary):
   dotnet build my-extension/
4. Restart Dataset Studio
```

### From NPM (for web-based extensions)

TODO: Phase 4 - Implement NPM-based installation

```
npm install @datasetstudio-extensions/my-extension
```

## Directory Structure

Each extension should follow this structure:

```
UserExtensions/
├── README.md (this file)
├── extension-id-1/
│   ├── extension.manifest.json      # Required: Extension metadata
│   ├── extension-id-1.dll           # Compiled extension assembly
│   ├── extension-id-1.xml           # Optional: Documentation comments
│   ├── icon.png                     # Optional: Extension icon (256x256)
│   ├── dependencies/
│   │   ├── dependency1.dll
│   │   └── dependency2.dll
│   ├── resources/
│   │   ├── localization/
│   │   │   ├── en-US.json
│   │   │   └── fr-FR.json
│   │   └── assets/
│   │       ├── styles.css
│   │       └── icons/
│   └── README.md                    # Recommended: Extension documentation
│
├── extension-id-2/
│   ├── extension.manifest.json
│   ├── extension-id-2.dll
│   └── README.md
│
└── ... more extensions
```

### TODO: Phase 3 - Document Extension Directory Format

Details needed:
- File naming conventions
- Required vs optional files
- Resource organization guidelines
- Dependency management
- Localization file format

## Extension Sources

### Official Extension Marketplace

TODO: Phase 4 - Set up official marketplace

- **URL**: https://marketplace.datasetstudio.dev (TODO)
- **Features**: Search, reviews, ratings, version history
- **Requirements**: Verified publisher, security scan
- **Support**: Official support and updates

### Community Extensions

TODO: Phase 4 - Set up community extension registry

- **URL**: https://community.datasetstudio.dev/extensions (TODO)
- **Features**: Community-submitted extensions
- **Requirements**: Basic validation, license compliance
- **Support**: Community-driven support

### GitHub Extensions

Extensions can be hosted on GitHub and installed via direct link:

```
Clone from GitHub:
git clone https://github.com/user/datasetstudio-extension.git
```

### Self-Hosted Extensions

You can host extensions on your own server:

TODO: Phase 4 - Document self-hosted extension installation

```
Manual installation from URL:
1. Download extension ZIP from your server
2. Extract to UserExtensions/
3. Restart Dataset Studio
```

## Getting Started with Extensions

### Finding Extensions

1. **Search Marketplace**: Use the built-in marketplace search
   - Navigate to Settings > Extensions > Marketplace
   - Search by name, tag, or capability

2. **GitHub Search**: Search GitHub for "datasetstudio-extension"
   - Look for active projects with documentation
   - Check for recent updates and community reviews

3. **Community Resources**: Check community forums and resources
   - Dataset Studio discussions
   - Community showcase pages
   - Blog posts and tutorials

### Installing Your First Extension

TODO: Phase 3 - Create beginner-friendly installation guide

**Example: Installing a CSV viewer extension**

```
1. Open Dataset Studio
2. Go to Settings > Extensions
3. Click "Browse Marketplace"
4. Search for "CSV Viewer"
5. Click "Install" on the desired extension
6. Grant required permissions if prompted
7. Restart Dataset Studio
8. The extension should now appear in your tools menu
```

### Managing Extensions

**Enabling/Disabling Extensions**:

TODO: Phase 3 - Implement extension management UI

```
1. Go to Settings > Extensions
2. Find extension in the list
3. Toggle the "Enabled" checkbox
4. Changes take effect immediately
```

**Updating Extensions**:

TODO: Phase 3 - Implement update mechanism

```
1. Go to Settings > Extensions
2. Look for "Update Available" indicators
3. Click "Update" for available updates
4. Follow on-screen prompts
```

**Uninstalling Extensions**:

```
1. Go to Settings > Extensions
2. Find extension in the list
3. Click the three-dot menu > "Uninstall"
4. Confirm the removal
5. Restart Dataset Studio
```

## Extension Security

### Permissions Model

TODO: Phase 3 - Implement permission system

Extensions request permissions for sensitive operations:

- **dataset.read** - Read dataset contents
- **dataset.write** - Modify datasets
- **dataset.delete** - Delete datasets
- **storage.read** - Read from storage
- **storage.write** - Write to storage
- **file.read** - Access files on disk
- **network.access** - Make network requests
- **gpu.access** - Use GPU resources

**Grant permissions carefully** - Only approve extensions from trusted sources.

### Verified Publishers

TODO: Phase 4 - Implement publisher verification

Extensions from verified publishers are marked with a badge:
- ✓ **Verified** - Published by Dataset Studio team
- ✓ **Trusted** - Published by community member with good track record
- ⚠ **Unverified** - Published by unknown source

### Security Scanning

TODO: Phase 4 - Implement security scanning

Extensions on the official marketplace are:
- Scanned for malware
- Analyzed for suspicious code patterns
- Checked for security vulnerabilities
- Required to use only whitelisted dependencies

### Safe Installation Practices

1. **Only install from trusted sources**
   - Official marketplace is the safest option
   - Verify publisher reputation
   - Check recent reviews and ratings

2. **Review requested permissions**
   - Only grant necessary permissions
   - Be cautious with network and file access
   - Avoid extensions requesting excessive permissions

3. **Keep extensions updated**
   - Enable automatic updates when available
   - Review update changelogs
   - Disable extensions with long update gaps

4. **Monitor extension behavior**
   - Watch for unusual activity or performance issues
   - Check logs for errors from extensions
   - Disable suspicious extensions immediately

## Troubleshooting

### Extension Not Loading

**Problem**: Extension doesn't appear in the extension list

**Solutions**:

TODO: Phase 3 - Create troubleshooting guide

1. Verify extension directory structure
   - Check that `extension.manifest.json` exists
   - Verify manifest format is valid (use validator)
   - Check that compiled assembly exists (for .NET extensions)

2. Check application logs
   - View logs in Settings > Diagnostics > Logs
   - Look for errors during extension loading phase
   - Note any specific error messages

3. Validate extension manifest
   - Use the manifest validator: Tools > Validate Extension
   - Fix any reported schema violations
   - Check for typos in extension ID or entry point

4. Check permissions
   - Ensure application can read extension files
   - Verify no antivirus software is blocking extensions
   - Check Windows security logs for access denied errors

5. Restart application
   - Close all instances of Dataset Studio
   - Clear extension cache if available
   - Restart application

### Extension Load Error

**Problem**: Extension fails to load with error message

**Common causes**:

TODO: Phase 3 - Document common extension errors

- Missing dependencies
- Incompatible .NET version
- Invalid manifest file
- Corrupt assembly file
- Missing required files

**Solution**: Check error details and logs:
1. Navigate to Settings > Extensions > Details for failing extension
2. Review error message and stack trace
3. Check extension marketplace for known issues
4. Contact extension developer with error details

### Extension Crashes Application

**Problem**: Opening extension causes Dataset Studio to crash

**Solutions**:

TODO: Phase 3 - Document crash troubleshooting

1. Disable the problematic extension immediately
2. Check for updates to the extension
3. Report crash with extension logs to developers
4. Consider using alternative extension with similar functionality

### Performance Issues from Extensions

**Problem**: Application runs slowly with certain extensions

**Solutions**:

TODO: Phase 3 - Document performance troubleshooting

1. Disable suspicious extensions one by one
2. Monitor system resources while extensions are active
3. Check extension logs for errors or warnings
4. Report performance issues to extension developer
5. Consider using alternative extension

### Permission Issues

**Problem**: "Permission Denied" errors from extension

**Solutions**:

TODO: Phase 3 - Document permission troubleshooting

1. Check Settings > Extensions > Permissions
2. Grant required permissions if available
3. Verify file/folder permissions are correct
4. Run Dataset Studio with administrator privileges (if appropriate)
5. Contact extension developer for support

## Support

### Getting Help

**For extension-specific issues**:

1. **Extension Documentation**
   - Read the extension's README.md file
   - Check the extension's help/wiki pages
   - Review FAQs if available

2. **Extension Developer**
   - Report issues on extension's GitHub page
   - Contact developer via email or support channel
   - Check existing issues before reporting

3. **Dataset Studio Community**
   - Post in community forums
   - Join Discord/community chat
   - Search existing discussions for similar issues

**For core Dataset Studio + extension issues**:

1. **Dataset Studio Support**
   - Visit https://datasetstudio.dev/support (TODO)
   - Contact support team
   - Create issue on main project

### Reporting Bugs

When reporting extension bugs, include:

TODO: Phase 4 - Create bug report template

```
Extension Name: [name]
Extension Version: [version]
Dataset Studio Version: [version]
Operating System: [Windows/Linux/macOS and version]
Error Message: [exact error message]
Steps to Reproduce: [steps]
Attached Files: [logs, example dataset if applicable]
```

### Requesting Features

Provide feedback to extension developers:

- Describe the desired functionality clearly
- Explain use cases and benefits
- Check if similar extensions exist
- Upvote existing feature requests

## Contributing Your Own Extension

Ready to develop your own extension?

See the **Extension Development Guide** at:
- `src/Extensions/SDK/DevelopmentGuide.md`

Steps to get started:

1. Read the development guide
2. Set up your development environment
3. Create extension project from template
4. Develop and test your extension
5. Submit to marketplace or GitHub

## Additional Resources

- **Extension SDK Documentation**: `src/Extensions/SDK/DevelopmentGuide.md`
- **API Reference**: `src/Extensions/SDK/` (C# classes and interfaces)
- **Example Extensions**: `src/Extensions/BuiltIn/` (built-in extensions)
- **Refactor Plan**: `REFACTOR_PLAN.md` (architecture and roadmap)

## Version History

**2025-12-10**: TODO - Initial scaffold for extension management documentation

---

**Note**: This document represents planned functionality for the extension system. Features marked as "TODO: Phase X" will be implemented according to the project roadmap in `REFACTOR_PLAN.md`.
