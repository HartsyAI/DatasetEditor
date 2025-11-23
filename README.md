# Hartsy's Dataset Editor

An API-first platform for ingesting, browsing, and analysing billion-scale AI image datasets. Built with ASP.NET Core minimal APIs and a Blazor WebAssembly client.

## 🚀 Features

- **API-Driven Lifecycle**: Dataset creation, ingestion status, and item retrieval exposed via REST endpoints.
- **Virtualized Viewing**: Only render what the user sees while prefetching nearby items for buttery scrolling.
- **Sliding-Window Infinite Scroll**: Browse very large image datasets with a fixed-size in-memory window, loading pages ahead/behind as you scroll while evicting old items to avoid WebAssembly out-of-memory crashes.
- **Streaming Ingestion (Roadmap)**: Designed for chunked uploads and background parsing to avoid memory spikes.
- **Shared Contracts**: Typed DTOs shared between client and server for end-to-end consistency.
- **Modular Extensibility**: Pluggable parsers, modalities, and viewers via dependency injection.
- **Observability Ready**: Hooks for telemetry, structured logging, and health endpoints.

## 📋 Requirements

- .NET 8.0 SDK or later
- Modern web browser (Chrome, Firefox, Safari, Edge)
- ~2GB RAM for development
- ~100MB disk space

## 🛠️ Getting Started

### 1. Clone the Repository

```bash
git clone <your-repo-url>
cd HartsysDatasetEditor
```

### 2. Restore Dependencies

```bash
dotnet restore
```

### 3. Build the Solution

```bash
dotnet build
```

### 4. Run the API and Client

```bash
# Terminal 1 - Minimal API (serves dataset lifecycle routes)
dotnet run --project src/HartsysDatasetEditor.Api

# Terminal 2 - Blazor WebAssembly client
dotnet run --project src/HartsysDatasetEditor.Client
```

Both projects share contracts via `HartsysDatasetEditor.Contracts`. The API currently uses in-memory repositories for smoke testing.

### 5. Open in Browser

Navigate to: `https://localhost:5001` (client dev server). Ensure the API is running at `https://localhost:7085` (default Kestrel HTTPS port) or update the client's `appsettings.Development.json` accordingly.

## 📊 Testing with Unsplash Dataset

Support for uploading and ingesting datasets is being rebuilt for the API-first architecture. The previous client-only ingestion flow has been removed. Follow the roadmap below to help implement the new streaming ingestion pipeline. For now, smoke-test the API using the built-in in-memory dataset endpoints:

```http
POST /api/datasets      // create dataset stub
GET  /api/datasets      // list datasets
GET  /api/datasets/{id} // inspect dataset detail
GET  /api/datasets/{id}/items?pageSize=100
```

## 🏗️ Project Structure

```
HartsysDatasetEditor/
├── src/
│   ├── HartsysDatasetEditor.Api/         # ASP.NET Core minimal APIs for dataset lifecycle + items
│   │   ├── Extensions/                   # Service registration helpers
│   │   ├── Models/                       # Internal persistence models
│   │   └── Services/                     # In-memory repositories, ingestion stubs
│   ├── HartsysDatasetEditor.Client/      # Blazor WASM UI
│   │   ├── Components/                   # Viewer, Dataset, Filter, Common UI pieces
│   │   ├── Services/                     # State management, caching, API clients (roadmap)
│   │   └── wwwroot/                      # Static assets, CSS, JS
│   └── HartsysDatasetEditor.Contracts/   # Shared DTOs (pagination, datasets, filters)
│
├── tests/                                # Unit tests
└── README.md
```

## 🏛️ Architecture Summary

The editor follows a strictly API-first workflow so that every client action flows through the HTTP layer before touching storage. High-level components:

- **Blazor WebAssembly Client** – virtualized viewers, upload wizard, and caching services that call the API via typed `HttpClient` wrappers. Prefetch and IndexedDB caching are planned per [docs/architecture.md](docs/architecture.md).
- **ASP.NET Core Minimal API** – orchestrates dataset lifecycle, ingestion coordination, and cursor-based item paging. Background hosted services handle ingestion and stub persistence today.
- **Backing Services** – pluggable storage (blob), database (PostgreSQL/Dynamo), and search index (Elastic/OpenSearch) abstractions so we can swap implementations as we scale.

See the detailed blueprint, data flows, and phased roadmap in `docs/architecture.md` for deeper dives.

### Dataset Viewer Sliding-Window Cache

- Uses cursor-based paging from the API to request small, contiguous chunks of items.
- Keeps a fixed-size in-memory window (`DatasetState.Items`) instead of materializing all N items on the client.
- Slides the window forward and backward as you scroll, evicting old items from memory to avoid WebAssembly out-of-memory crashes.
- Rehydrates earlier or later regions of the dataset from IndexedDB (when enabled) or the API when you scroll back.

## ▶️ Running the API + Client Together

1. **Start the API**
   ```bash
   dotnet run --project src/HartsysDatasetEditor.Api
   ```
   By default this listens on `https://localhost:7085`. Trust the dev certificate the first time.

2. **Start the Blazor WASM client**
   ```bash
   dotnet run --project src/HartsysDatasetEditor.Client
   ```
   The dev server hosts the static client at `https://localhost:5001`.

3. **Configure the client-to-API base address**
   - The client reads `DatasetApi:BaseAddress` from `wwwroot/appsettings.Development.json`. Leave it at the default `https://localhost:7085` or update it if the API port changes.

4. **Browse the app**
   - Navigate to `https://localhost:5001`. The client will call the API for dataset lists/items.
   - Verify CORS is enabled for the WASM origin once the API CORS policy is implemented (see roadmap).

When deploying as an ASP.NET Core hosted app, the API project can serve the WASM assets directly; until then, the two projects run side-by-side as above.

## 🎯 Key Technologies

- **ASP.NET Core 8.0**: Minimal API hosting and background services
- **Blazor WebAssembly**: Client-side SPA targeting the API
- **MudBlazor**: Material Design component library
- **CsvHelper**: Planned streaming ingestion parsing
- **IndexedDB / LocalStorage**: Client-side caching strategy (roadmap)
- **Virtualization**: Blazor's built-in `<Virtualize>` component

## 📦 NuGet Packages

### Client Project
- `Microsoft.AspNetCore.Components.WebAssembly`
- `MudBlazor` - Material Design UI components
- `Blazored.LocalStorage` - Browser storage
- `CsvHelper` - CSV/TSV parsing

### Core Project
- No external dependencies (lightweight by design)

## 🔧 Configuration

- Client configuration lives in `wwwroot/appsettings*.json`. Update the `DatasetApi:BaseAddress` once the API host changes.
- API configuration is stored in `appsettings*.json` under the `src/HartsysDatasetEditor.Api` project. Adjust logging and CORS settings here.

## 🎨 Customization

### Adding New Dataset Formats (Roadmap)

1. Create a parser implementing `IDatasetParser` in the ingestion pipeline.
2. Register it in DI through a parser registry service.
3. Add format to `DatasetFormat` enum and expose via API capability endpoint.

```csharp
public class MyFormatParser : IDatasetParser
{
    public bool CanParse(string data) { /* ... */ }
    public IAsyncEnumerable<IDatasetItem> ParseAsync(string data) { /* ... */ }
}
```

### Adding New Modalities

1. Create a provider implementing `IModalityProvider`
2. Register in `ModalityProviderRegistry`
3. Add modality to `Modality` enum
4. Create viewer component

## 🚀 Performance

- Virtualized rendering via `<Virtualize>` keeps browser memory flat while streaming new pages.
- API pagination uses cursor tokens and configurable page sizes to keep server memory bounded.
- Future ingestion jobs will stream upload parsing to avoid buffering entire files.

## 📝 Development

### Building for Production

```bash
dotnet publish -c Release
```

Output in: `src/HartsysDatasetEditor.Client/bin/Release/net8.0/publish/`

### Deployment

#### GitHub Pages
1. Build for production
2. Copy `wwwroot` contents to `gh-pages` branch
3. Enable GitHub Pages in repo settings

#### Azure Static Web Apps
1. Create Static Web App in Azure Portal
2. Configure build:
   - App location: `src/HartsysDatasetEditor.Client`
   - Output location: `wwwroot`
3. Deploy via GitHub Actions

## 🐛 Troubleshooting

- Ensure both API and client are running before testing. API defaults to HTTPS, so trust the development certificate when prompted.
- Use Swagger/OpenAPI (coming soon) or tools like `curl`/`httpie`/Postman to verify endpoint availability.
- When modifying contracts, update both server and client references to avoid serialization errors.

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

## 📄 License

This project is licensed under the MIT License - see the LICENSE file for details.

## 🙏 Acknowledgments

- [Unsplash](https://unsplash.com/data) - Dataset provider
- [MudBlazor](https://mudblazor.com/) - UI component library
- [Blazor](https://dotnet.microsoft.com/apps/aspnet/web-apps/blazor) - Framework

## 📞 Support

For issues, questions, or suggestions:
- Open an issue on GitHub
- Check existing documentation
- Review the MVP completion status document

## 🗺️ Roadmap

The detailed architecture, phased roadmap, and task checklist live in [docs/architecture.md](docs/architecture.md). Highlights:

1. **Infrastructure** – ✅ API and shared contracts scaffolded; configure hosted solution + README updates.
2. **API Skeleton** – In progress; dataset CRUD endpoints implemented with in-memory storage, upload endpoint pending.
3. **Client Refactor** – Pending; migrate viewer to API-backed pagination and caching services.
4. **Ingestion & Persistence** – Pending; implement streaming ingestion worker and backing database.
5. **Advanced Features** – Pending; CDN integration, SignalR notifications, plugin architecture.

---

**Current Version**: 0.2.0-alpha  
**Status**: API-first migration in progress  
**Last Updated**: 2025

