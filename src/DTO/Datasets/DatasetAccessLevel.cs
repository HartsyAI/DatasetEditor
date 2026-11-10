namespace DatasetStudio.DTO.Datasets;

/// <summary>What a share lets its user do with a dataset, besides the optional delete and share rights.</summary>
public enum DatasetAccessLevel
{
    /// <summary>See the dataset and its items.</summary>
    Read = 0,

    /// <summary>Also edit the dataset and its items.</summary>
    Write = 1
}
