using HartsysDatasetEditor.Core.Utilities;
using CoreInterfaces = HartsysDatasetEditor.Core.Interfaces;

namespace HartsysDatasetEditor.Api.Services;

/// <summary>Initializes database with sample data for development</summary>
public class DatabaseInitializationService
{
    private readonly CoreInterfaces.IDatasetRepository _datasetRepository;
    private readonly CoreInterfaces.IDatasetItemRepository _itemRepository;
    
    public DatabaseInitializationService(
        CoreInterfaces.IDatasetRepository datasetRepository,
        CoreInterfaces.IDatasetItemRepository itemRepository)
    {
        _datasetRepository = datasetRepository;
        _itemRepository = itemRepository;
    }
    
    /// <summary>Seeds database with sample data if empty</summary>
    public void Initialize()
    {
        long datasetCount = _datasetRepository.GetDatasetCount();
        
        if (datasetCount == 0)
        {
            Logs.Info("Database is empty, seeding with sample data...");
            SeedSampleData();
        }
        else
        {
            Logs.Info($"Database already contains {datasetCount} datasets");
        }
    }
    
    private void SeedSampleData()
    {
        // TODO: Add sample datasets for testing
        // This will be used during development
        Logs.Info("Sample data seeding placeholder");
    }
}
