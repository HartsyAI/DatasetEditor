using HartsysDatasetEditor.Core.Interfaces;
using HartsysDatasetEditor.Core.Models;
using HartsysDatasetEditor.Core.Utilities;
using LiteDB;

namespace HartsysDatasetEditor.Api.Repositories;

/// <summary>LiteDB implementation of dataset repository</summary>
public class LiteDbDatasetRepository : IDatasetRepository
{
    private readonly LiteDatabase _database;
    private readonly string _collectionName = "datasets";
    
    public LiteDbDatasetRepository(string databasePath)
    {
        // Ensure directory exists
        string? directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        
        _database = new LiteDatabase(databasePath);
        Logs.Info($"LiteDB dataset repository initialized: {databasePath}");
    }
    
    public Guid CreateDataset(Dataset dataset)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        
        // Generate ID if not set
        if (string.IsNullOrEmpty(dataset.Id))
        {
            dataset.Id = Guid.NewGuid().ToString();
        }
        
        dataset.CreatedAt = DateTime.UtcNow;
        dataset.UpdatedAt = DateTime.UtcNow;
        
        collection.Insert(dataset);
        
        Logs.Info($"Created dataset: {dataset.Id} - {dataset.Name}");
        return Guid.Parse(dataset.Id);
    }
    
    public Dataset? GetDataset(Guid id)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        return collection.FindById(id.ToString());
    }
    
    public List<Dataset> GetAllDatasets(int page = 0, int pageSize = 50)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        
        return collection.Query()
            .OrderByDescending(x => x.UpdatedAt)
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
    }
    
    public void UpdateDataset(Dataset dataset)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        dataset.UpdatedAt = DateTime.UtcNow;
        collection.Update(dataset);
        
        Logs.Info($"Updated dataset: {dataset.Id}");
    }
    
    public void DeleteDataset(Guid id)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        collection.Delete(id.ToString());
        
        Logs.Info($"Deleted dataset: {id}");
    }
    
    public long GetDatasetCount()
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        return collection.Count();
    }
    
    public List<Dataset> SearchDatasets(string query, int page = 0, int pageSize = 50)
    {
        ILiteCollection<Dataset> collection = _database.GetCollection<Dataset>(_collectionName);
        
        string lowerQuery = query.ToLowerInvariant();
        
        return collection.Query()
            .Where(x => x.Name.ToLower().Contains(lowerQuery) || 
                       x.Description.ToLower().Contains(lowerQuery))
            .OrderByDescending(x => x.UpdatedAt)
            .Skip(page * pageSize)
            .Limit(pageSize)
            .ToList();
    }
}
