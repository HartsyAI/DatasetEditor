namespace DatasetStudio.Core.Enumerations;

/// <summary>Defines supported dataset formats for parsing and export</summary>
public enum DatasetFormat
{
    /// <summary>Tab-separated values format (generic TSV files)</summary>
    TSV = 0,

    /// <summary>Comma-separated values format (generic CSV files) - TODO: Implement CSV support</summary>
    CSV = 1,

    /// <summary>COCO JSON format (Common Objects in Context) - TODO: Implement COCO support</summary>
    COCO = 2,

    /// <summary>YOLO text format (bounding box annotations) - TODO: Implement YOLO support</summary>
    YOLO = 3,

    /// <summary>Pascal VOC XML format - TODO: Implement Pascal VOC support</summary>
    PascalVOC = 4,

    /// <summary>HuggingFace Arrow/Parquet format - TODO: Implement HuggingFace support</summary>
    HuggingFace = 5,

    /// <summary>ImageNet folder structure - TODO: Implement ImageNet support</summary>
    ImageNet = 6,

    /// <summary>CVAT XML format - TODO: Implement CVAT support</summary>
    CVAT = 7,

    /// <summary>Labelbox JSON format - TODO: Implement Labelbox support</summary>
    Labelbox = 8,

    /// <summary>Generic JSON format with auto-detection - TODO: Implement generic JSON support</summary>
    JSON = 9,

    /// <summary>Unknown format requiring manual specification</summary>
    Unknown = 99
}
