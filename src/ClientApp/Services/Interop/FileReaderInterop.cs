using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components;
using DatasetStudio.Core.Utilities;
using DatasetStudio.Core.Utilities.Logging;

namespace DatasetStudio.ClientApp.Services.Interop;

/// <summary>Provides JavaScript interop for reading files from the browser.</summary>
public class FileReaderInterop(IJSRuntime jsRuntime)
{
    public IJSRuntime JsRuntime { get; } = jsRuntime;

    /// <summary>Reads a file as text using FileReader API.</summary>
    /// <param name="inputElement">Reference to the input element containing the file.</param>
    /// <returns>File content as string.</returns>
    public async Task<string> ReadFileAsTextAsync(ElementReference inputElement)
    {
        try
        {
            string result = await JsRuntime.InvokeAsync<string>("interop.readFileAsText", inputElement);
            Logs.Info("File read as text successfully");
            return result;
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to read file as text", ex);
            throw;
        }
    }

    /// <summary>Reads a file as a data URL (base64 encoded).</summary>
    /// <param name="inputElement">Reference to the input element containing the file.</param>
    /// <returns>File content as base64 data URL.</returns>
    public async Task<string> ReadFileAsDataUrlAsync(ElementReference inputElement)
    {
        try
        {
            string result = await JsRuntime.InvokeAsync<string>("fileReader.readAsDataURL", inputElement);
            Logs.Info("File read as data URL successfully");
            return result;
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to read file as data URL", ex);
            throw;
        }
    }

    /// <summary>Gets file information without reading the content.</summary>
    /// <param name="inputElement">Reference to the input element containing the file.</param>
    /// <returns>File metadata (name, size, type).</returns>
    public async Task<FileInfo> GetFileInfoAsync(ElementReference inputElement)
    {
        try
        {
            FileInfo info = await JsRuntime.InvokeAsync<FileInfo>("interop.getFileInfo", inputElement);
            Logs.Info($"File info retrieved: {info.Name}, {info.Size} bytes");
            return info;
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to get file info", ex);
            throw;
        }
    }

    /// <summary>Checks if a file is selected in the input element.</summary>
    /// <param name="inputElement">Reference to the input element.</param>
    /// <returns>True if file is selected, false otherwise.</returns>
    public async Task<bool> HasFileAsync(ElementReference inputElement)
    {
        try
        {
            bool hasFile = await JsRuntime.InvokeAsync<bool>("interop.hasFile", inputElement);
            return hasFile;
        }
        catch (Exception ex)
        {
            Logs.Error("Failed to check if file exists", ex);
            return false;
        }
    }

    /// <summary>Reads a file in chunks for large file handling.</summary>
    /// <param name="inputElement">Reference to the input element containing the file.</param>
    /// <param name="chunkSize">Size of each chunk in bytes.</param>
    /// <returns>Async enumerable of file chunks.</returns>
    public async IAsyncEnumerable<string> ReadFileInChunksAsync(ElementReference inputElement, int chunkSize = 1024 * 1024)
    {
        try
        {
            // This is a placeholder - actual implementation would require more complex JS interop
            // For MVP, we'll read the entire file and yield it as a single chunk
            string content = await ReadFileAsTextAsync(inputElement);
            yield return content;
            
            // TODO: Implement actual chunked reading for files larger than memory can handle
        }
        finally
        {
            Logs.Info("Chunked file reading completed");
        }
    }
    
    // TODO: Add progress reporting for large file reads
    // TODO: Add support for reading multiple files
    // TODO: Add support for reading binary files
    // TODO: Add file validation (size limits, mime type checking)
}

/// <summary>Represents metadata about a file.</summary>
public class FileInfo
{
    /// <summary>Name of the file including extension.</summary>
    public string Name { get; set; } = string.Empty;
    
    /// <summary>Size of the file in bytes.</summary>
    public long Size { get; set; }
    
    /// <summary>MIME type of the file.</summary>
    public string Type { get; set; } = string.Empty;
    
    /// <summary>Last modified timestamp.</summary>
    public DateTime LastModified { get; set; }
}
