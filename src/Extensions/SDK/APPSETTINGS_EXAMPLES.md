# Extension System Configuration

This document shows how to configure the extension system in appsettings.json for both API and Client projects.

## API Backend Configuration (src/APIBackend/appsettings.json)

Add this to your appsettings.json:

```json
{
  "Extensions": {
    "Enabled": true,
    "AutoLoad": true,
    "Directory": "./Extensions/BuiltIn",
    "UserDirectory": "./Extensions/User",
    "LoadTimeout": 30000
  },
  
  "Extensions:CoreViewer": {
    "DefaultPageSize": 50,
    "EnableVirtualization": true,
    "CacheTimeout": 300
  },
  
  "Extensions:Creator": {
    "MaxUploadSize": 5368709120,
    "AllowedFormats": ["json", "csv", "parquet", "arrow"],
    "TempDirectory": "./temp/uploads"
  },
  
  "Extensions:Editor": {
    "EnableBatchEditing": true,
    "MaxBatchSize": 1000,
    "AutoSaveInterval": 30000
  },
  
  "Extensions:AITools": {
    "HuggingFaceApiKey": "",
    "DefaultCaptioningModel": "Salesforce/blip-image-captioning-base",
    "DefaultTaggingModel": "ViT-L/14",
    "BatchSize": 10,
    "Timeout": 30000,
    "EnableBackgroundProcessing": true
  }
}
```

## Client Application Configuration (src/ClientApp/wwwroot/appsettings.json)

Add this to configure the client-side extension system:

```json
{
  "Api": {
    "BaseUrl": "https://localhost:5001"
  },
  
  "Extensions": {
    "Enabled": true,
    "AutoLoad": true,
    "Directory": "./Extensions/BuiltIn"
  },
  
  "Extensions:CoreViewer": {
    "DefaultView": "grid",
    "ItemsPerPage": 50,
    "EnableInfiniteScroll": true
  },
  
  "Extensions:Creator": {
    "ShowWizard": true,
    "DefaultFormat": "json"
  },
  
  "Extensions:Editor": {
    "EnableRichTextEditor": true,
    "EnableImageEditor": true
  },
  
  "Extensions:AITools": {
    "ShowProgressIndicator": true,
    "AutoRefreshResults": true,
    "PollingInterval": 2000
  }
}
```

## Distributed Deployment Configuration

### Scenario 1: API and Client on Different Servers

**API Server (api.datasetstudio.com) - appsettings.Production.json:**
```json
{
  "Extensions": {
    "Enabled": true,
    "Directory": "/var/www/datasetstudio/extensions"
  },
  
  "Cors": {
    "AllowedOrigins": ["https://app.datasetstudio.com"]
  }
}
```

**Client Server (app.datasetstudio.com) - appsettings.Production.json:**
```json
{
  "Api": {
    "BaseUrl": "https://api.datasetstudio.com"
  },
  
  "Extensions": {
    "Enabled": true
  }
}
```

### Scenario 2: Local Development

**API (localhost:5001) - appsettings.Development.json:**
```json
{
  "Extensions": {
    "Enabled": true,
    "Directory": "../Extensions/BuiltIn"
  },
  
  "Cors": {
    "AllowedOrigins": ["http://localhost:5002"]
  }
}
```

**Client (localhost:5002) - appsettings.Development.json:**
```json
{
  "Api": {
    "BaseUrl": "http://localhost:5001"
  },
  
  "Extensions": {
    "Enabled": true
  }
}
```

## Environment-Specific Configuration

Use different appsettings files for different environments:

- `appsettings.json` - Base configuration
- `appsettings.Development.json` - Local development
- `appsettings.Staging.json` - Staging environment
- `appsettings.Production.json` - Production environment

The configuration system automatically merges these files based on the ASPNETCORE_ENVIRONMENT variable.

## Extension-Specific Secrets

For sensitive configuration (API keys, tokens), use:

1. **Development**: User Secrets
   ```bash
   dotnet user-secrets set "Extensions:AITools:HuggingFaceApiKey" "your-key-here"
   ```

2. **Production**: Environment Variables
   ```bash
   export Extensions__AITools__HuggingFaceApiKey="your-key-here"
   ```

3. **Cloud**: Azure Key Vault, AWS Secrets Manager, etc.

## Configuration Validation

Extensions can validate their configuration on startup:

```csharp
protected override async Task<bool> OnValidateAsync()
{
    var apiKey = Context.Configuration["HuggingFaceApiKey"];
    if (string.IsNullOrEmpty(apiKey))
    {
        Logger.LogError("HuggingFace API key not configured");
        return false;
    }
    
    return true;
}
```
