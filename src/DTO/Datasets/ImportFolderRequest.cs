namespace DatasetStudio.DTO.Datasets;

/// <summary>Request payload for ingesting a dataset from a folder already on the server's disk.</summary>
public sealed record ImportFolderRequest(string FolderPath);
