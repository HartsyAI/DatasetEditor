# 🚀 Phase 1 Execution Guide - Step-by-Step

## Overview
This guide walks through the exact steps to complete Phase 1 of the Dataset Studio refactor.

---

## ✅ Pre-Flight Checklist

- [x] Refactor plan created (REFACTOR_PLAN.md)
- [x] Backup branch created (pre-refactor-backup)
- [x] Current code committed
- [ ] All tests passing (run before starting)
- [ ] Application runs successfully (verify before starting)

---

## 📋 Phase 1 Tasks

### Task 1: Verify Current State Works ✅
**Goal:** Ensure everything works before we start moving files

```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Run the application
dotnet run --project src/HartsysDatasetEditor.Api
```

**Success Criteria:**
- ✅ Build succeeds with no errors
- ✅ Tests pass
- ✅ Application launches successfully
- ✅ Can view datasets
- ✅ Can upload datasets

---

### Task 2: Create New Directory Structure
**Goal:** Create all new folders

**Folders to Create:**
```
src/Core/
src/Core/DomainModels/
src/Core/DomainModels/Datasets/
src/Core/DomainModels/Items/
src/Core/DomainModels/Users/ (TODO scaffold)
src/Core/Enumerations/
src/Core/Abstractions/
src/Core/Abstractions/Parsers/
src/Core/Abstractions/Storage/ (TODO scaffold)
src/Core/Abstractions/Captioning/ (TODO scaffold)
src/Core/Abstractions/Extensions/ (TODO scaffold)
src/Core/Abstractions/Repositories/
src/Core/BusinessLogic/
src/Core/BusinessLogic/Parsers/
src/Core/BusinessLogic/Storage/ (TODO scaffold)
src/Core/BusinessLogic/Modality/
src/Core/BusinessLogic/Layouts/
src/Core/BusinessLogic/Extensions/ (TODO scaffold)
src/Core/Utilities/
src/Core/Utilities/Logging/
src/Core/Utilities/Helpers/
src/Core/Utilities/Encryption/ (TODO scaffold)
src/Core/Constants/

src/DTO/
src/DTO/Common/
src/DTO/Datasets/
src/DTO/Items/
src/DTO/Users/ (TODO scaffold)
src/DTO/Extensions/ (TODO scaffold)
src/DTO/AI/ (TODO scaffold)

src/APIBackend/
src/APIBackend/Configuration/
src/APIBackend/Controllers/ (TODO scaffold)
src/APIBackend/Services/
src/APIBackend/Services/DatasetManagement/
src/APIBackend/Services/Caching/ (TODO scaffold)
src/APIBackend/Services/Authentication/ (TODO scaffold)
src/APIBackend/Services/Extensions/ (TODO scaffold)
src/APIBackend/Services/Integration/
src/APIBackend/DataAccess/
src/APIBackend/DataAccess/LiteDB/
src/APIBackend/DataAccess/LiteDB/Repositories/
src/APIBackend/DataAccess/PostgreSQL/ (TODO scaffold)
src/APIBackend/DataAccess/Parquet/ (TODO scaffold)
src/APIBackend/Middleware/ (TODO scaffold)
src/APIBackend/BackgroundWorkers/ (TODO scaffold)
src/APIBackend/Models/
src/APIBackend/Endpoints/

src/ClientApp/
src/ClientApp/Configuration/
src/ClientApp/wwwroot/
src/ClientApp/wwwroot/css/
src/ClientApp/wwwroot/js/
src/ClientApp/wwwroot/Themes/ (TODO scaffold)
src/ClientApp/Features/
src/ClientApp/Features/Home/
src/ClientApp/Features/Home/Pages/
src/ClientApp/Features/Installation/ (TODO scaffold)
src/ClientApp/Features/Datasets/
src/ClientApp/Features/Datasets/Pages/
src/ClientApp/Features/Datasets/Components/
src/ClientApp/Features/Datasets/Services/
src/ClientApp/Features/Authentication/ (TODO scaffold)
src/ClientApp/Features/Administration/ (TODO scaffold)
src/ClientApp/Features/Settings/
src/ClientApp/Features/Settings/Pages/
src/ClientApp/Features/Settings/Components/
src/ClientApp/Shared/
src/ClientApp/Shared/Layout/
src/ClientApp/Shared/Components/
src/ClientApp/Shared/Services/
src/ClientApp/Services/
src/ClientApp/Services/StateManagement/
src/ClientApp/Services/ApiClients/
src/ClientApp/Services/Caching/
src/ClientApp/Services/Interop/

src/Extensions/
src/Extensions/SDK/ (TODO scaffold)
src/Extensions/BuiltIn/ (TODO scaffold)
src/Extensions/BuiltIn/CoreViewer/ (TODO scaffold)
src/Extensions/BuiltIn/Creator/ (TODO scaffold)
src/Extensions/BuiltIn/Editor/ (TODO scaffold)
src/Extensions/BuiltIn/AITools/ (TODO scaffold)
src/Extensions/BuiltIn/AdvancedTools/ (TODO scaffold)
src/Extensions/UserExtensions/ (TODO scaffold)

Docs/
Docs/Installation/ (TODO scaffold)
Docs/UserGuides/ (TODO scaffold)
Docs/API/ (TODO scaffold)
Docs/Development/ (TODO scaffold)

Scripts/ (TODO scaffold)
```

---

### Task 3: Create New Project Files
**Goal:** Create the new .csproj files with updated names and namespaces

#### 3.1 Create Core.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>DatasetStudio.Core</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="CsvHelper" Version="33.*" />
  </ItemGroup>

</Project>
```

#### 3.2 Create DTO.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>DatasetStudio.DTO</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

</Project>
```

#### 3.3 Create APIBackend.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RootNamespace>DatasetStudio.APIBackend</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="LiteDB" Version="5.0.17" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.Server" Version="8.0.10" />
    <PackageReference Include="Swashbuckle.AspNetCore" Version="6.5.0" />
    <PackageReference Include="CsvHelper" Version="33.0.2" />
    <PackageReference Include="Parquet.Net" Version="5.3.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DTO\DTO.csproj" />
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\ClientApp\ClientApp.csproj" />
  </ItemGroup>

</Project>
```

#### 3.4 Create ClientApp.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk.BlazorWebAssembly">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <RootNamespace>DatasetStudio.ClientApp</RootNamespace>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>

  <ItemGroup>
    <!-- Blazor WebAssembly -->
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly" Version="8.0.*" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebAssembly.DevServer" Version="8.0.*" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.Http" Version="8.0.*" />

    <!-- MudBlazor UI Framework -->
    <PackageReference Include="MudBlazor" Version="7.8.*" />

    <!-- LocalStorage for browser storage -->
    <PackageReference Include="Blazored.LocalStorage" Version="4.5.*" />

    <!-- CSV/TSV parsing -->
    <PackageReference Include="CsvHelper" Version="33.*" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Core\Core.csproj" />
    <ProjectReference Include="..\DTO\DTO.csproj" />
  </ItemGroup>

</Project>
```

#### 3.5 Update Solution File
Create new `DatasetStudio.sln`:
```
dotnet new sln -n DatasetStudio
dotnet sln add src/Core/Core.csproj
dotnet sln add src/DTO/DTO.csproj
dotnet sln add src/APIBackend/APIBackend.csproj
dotnet sln add src/ClientApp/ClientApp.csproj
```

---

### Task 4: Copy Files with Namespace Updates
**Goal:** Copy all existing files to new locations and update namespaces

#### Strategy:
1. Copy file to new location
2. Update namespace in file
3. Update any internal using statements
4. Build and fix errors incrementally

#### 4.1 Core Migration Priority Order:
1. Enumerations (no dependencies)
2. Constants (no dependencies)
3. Utilities (minimal dependencies)
4. Domain Models (depends on enums)
5. Abstractions/Interfaces (depends on models)
6. Business Logic (depends on everything)

#### 4.2 DTO Migration Priority Order:
1. Common DTOs (no dependencies)
2. Dataset DTOs
3. Item DTOs

#### 4.3 API Migration Priority Order:
1. Models
2. Repositories
3. Services
4. Endpoints
5. Configuration/Program.cs

#### 4.4 Client Migration Priority Order:
1. wwwroot (static files, no namespace)
2. Services/Interop
3. Services/ApiClients
4. Services/StateManagement
5. Shared/Components
6. Shared/Layout
7. Features/Datasets/Components
8. Features/Datasets/Pages
9. Features/Settings
10. Features/Home
11. Configuration (Program.cs, App.razor)

---

### Task 5: Create TODO Scaffold Files
**Goal:** Create placeholder files for future features

**Files to Create with TODO Comments:**

#### Phase 2 (Database) TODOs:
- `src/APIBackend/DataAccess/PostgreSQL/DbContext.cs`
- `src/APIBackend/DataAccess/PostgreSQL/Repositories/DatasetRepository.cs`
- `src/APIBackend/DataAccess/PostgreSQL/Repositories/UserRepository.cs`
- `src/APIBackend/DataAccess/Parquet/ParquetItemRepository.cs`
- `src/APIBackend/DataAccess/Parquet/ParquetWriter.cs`

#### Phase 3 (Extensions) TODOs:
- `src/Extensions/SDK/BaseExtension.cs`
- `src/Extensions/SDK/ExtensionMetadata.cs`
- `src/Extensions/SDK/DevelopmentGuide.md`
- `src/APIBackend/Services/Extensions/ExtensionLoaderService.cs`

#### Phase 4 (Installation) TODOs:
- `src/ClientApp/Features/Installation/Pages/Install.razor`
- `src/ClientApp/Features/Installation/Services/InstallationService.cs`

#### Phase 5 (Authentication) TODOs:
- `src/DTO/Users/UserDto.cs`
- `src/APIBackend/Controllers/UsersController.cs`
- `src/APIBackend/Services/Authentication/AuthService.cs`
- `src/ClientApp/Features/Authentication/Pages/Login.razor`

#### Phase 6 (AI Tools) TODOs:
- `src/DTO/AI/CaptionRequest.cs`
- `src/APIBackend/Controllers/AIController.cs`
- `src/Extensions/BuiltIn/AITools/AIToolsExtension.cs`

**Template for TODO Files:**
```csharp
// TODO: Phase X - [Feature Name]
//
// Purpose: [Brief description]
//
// Implementation Plan:
// 1. [Step 1]
// 2. [Step 2]
// 3. [Step 3]
//
// Dependencies:
// - [Dependency 1]
// - [Dependency 2]
//
// References:
// - See REFACTOR_PLAN.md Phase X for details

namespace DatasetStudio.[Namespace];

// TODO: Implement this class
public class PlaceholderClass
{
    // Implementation will be added in Phase X
}
```

---

### Task 6: Update Configuration Files
**Goal:** Update all config files to reference new paths and namespaces

#### Files to Update:
- `src/APIBackend/Configuration/appsettings.json`
- `src/APIBackend/Configuration/appsettings.Development.json`
- `src/APIBackend/Configuration/Program.cs`
- `src/ClientApp/Configuration/Program.cs`
- `src/ClientApp/Configuration/_Imports.razor`
- `src/ClientApp/wwwroot/index.html`

---

### Task 7: Build & Test Incrementally
**Goal:** Ensure everything compiles and works

```bash
# Build Core first
dotnet build src/Core/Core.csproj

# Build DTO
dotnet build src/DTO/DTO.csproj

# Build ClientApp
dotnet build src/ClientApp/ClientApp.csproj

# Build APIBackend (last, depends on all)
dotnet build src/APIBackend/APIBackend.csproj

# Build entire solution
dotnet build DatasetStudio.sln

# Run tests
dotnet test

# Run application
dotnet run --project src/APIBackend/APIBackend.csproj
```

**Fix errors as they appear:**
- Missing using statements
- Incorrect namespaces
- Broken references
- Path issues

---

### Task 8: Clean Up Old Files
**Goal:** Remove old project structure after verifying new structure works

```bash
# Verify new structure works first!
# Then delete old folders:
rm -rf src/HartsysDatasetEditor.Core
rm -rf src/HartsysDatasetEditor.Contracts
rm -rf src/HartsysDatasetEditor.Api
rm -rf src/HartsysDatasetEditor.Client

# Delete old solution file
rm HartsysDatasetEditor.sln
```

---

### Task 9: Update Documentation
**Goal:** Update README and other docs

**Files to Update:**
- `README.md` - Update project name, structure, build instructions
- Create `ARCHITECTURE.md` - Document new architecture
- Update any other documentation references

---

### Task 10: Final Verification
**Goal:** Ensure everything works end-to-end

**Test Checklist:**
- [ ] Solution builds with no warnings
- [ ] All tests pass
- [ ] Application runs
- [ ] Can navigate to home page
- [ ] Can view datasets
- [ ] Can upload a new dataset (local file)
- [ ] Can upload a ZIP archive
- [ ] Can import from HuggingFace
- [ ] Can filter datasets
- [ ] Can search datasets
- [ ] Can view image details
- [ ] Can edit image metadata
- [ ] Settings page works
- [ ] Theme switching works
- [ ] View mode switching works

---

## 🎯 Phase 1 Definition of Done

Phase 1 is complete when:

1. ✅ New directory structure exists
2. ✅ All 4 new projects build successfully
3. ✅ All namespaces updated to `DatasetStudio.*`
4. ✅ All existing features work (see test checklist)
5. ✅ All future features have TODO scaffolds
6. ✅ Old project folders removed
7. ✅ Documentation updated
8. ✅ Code committed with clear commit message
9. ✅ No build warnings
10. ✅ Application runs without errors

---

## 📊 Progress Tracking

### Completed:
- [x] Refactor plan created
- [x] Backup branch created
- [x] Execution guide created

### In Progress:
- [ ] Current state verification
- [ ] Directory structure creation
- [ ] New project files
- [ ] File migration
- [ ] Namespace updates
- [ ] TODO scaffolds
- [ ] Configuration updates
- [ ] Testing
- [ ] Cleanup
- [ ] Documentation

### Remaining:
- All of Phase 2-8 (see REFACTOR_PLAN.md)

---

## 🚨 Important Reminders

1. **Commit Often:** After each successful task
2. **Test Incrementally:** Don't wait until the end
3. **Keep Notes:** Document any issues or decisions
4. **Don't Break Working Code:** Move, don't rewrite
5. **Use TODOs Liberally:** Mark everything that's incomplete
6. **Ask for Help:** If stuck, check the refactor plan

---

## 📞 Next Steps After Phase 1

Once Phase 1 is complete:
1. Review and celebrate! 🎉
2. Commit final changes
3. Create PR for review (optional)
4. Plan Phase 2: Database Migration
5. Start implementing extension system foundation

---

*Last Updated: 2025-12-08*
*Phase: 1 - Restructure & Scaffold*
*Status: Ready to Execute*
