using HartsysDatasetEditor.Core.Models;

namespace HartsysDatasetEditor.Core.Interfaces;

/// <summary>Repository interface for dataset CRUD operations</summary>
public interface IDatasetRepository
{
    /// <summary>Creates a new dataset and returns its ID</summary>
    Guid CreateDataset(Dataset dataset);
    
    /// <summary>Gets a dataset by ID</summary>
    Dataset? GetDataset(Guid id);
    
    /// <summary>Gets all datasets with pagination</summary>
    List<Dataset> GetAllDatasets(int page = 0, int pageSize = 50);
    
    /// <summary>Updates an existing dataset</summary>
    void UpdateDataset(Dataset dataset);
    
    /// <summary>Deletes a dataset and all its items</summary>
    void DeleteDataset(Guid id);
    
    /// <summary>Gets total count of datasets</summary>
    long GetDatasetCount();
    
    /// <summary>Searches datasets by name or description</summary>
    List<Dataset> SearchDatasets(string query, int page = 0, int pageSize = 50);
}
