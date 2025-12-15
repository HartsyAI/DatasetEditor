namespace DatasetStudio.DTO.Datasets;

/// <summary>Extension methods for DatasetItemDto to provide formatted display values</summary>
public static class DatasetItemDtoExtensions
{
    /// <summary>Gets formatted dimension string (e.g., "1920x1080")</summary>
    public static string GetFormattedDimensions(this DatasetItemDto item)
    {
        if (item.Width > 0 && item.Height > 0)
        {
            return $"{item.Width}x{item.Height}";
        }
        return "Unknown";
    }

    /// <summary>Gets formatted file size (e.g., "2.5 MB")</summary>
    public static string GetFormattedFileSize(this DatasetItemDto item)
    {
        // File size is not in the DTO, return placeholder
        // TODO: Add FileSize property to DatasetItemDto if needed
        return "N/A";
    }

    /// <summary>Gets aspect ratio as a string (e.g., "16:9")</summary>
    public static string GetAspectRatioString(this DatasetItemDto item)
    {
        if (item.Width <= 0 || item.Height <= 0)
        {
            return "Unknown";
        }

        int gcd = GCD(item.Width, item.Height);
        int ratioWidth = item.Width / gcd;
        int ratioHeight = item.Height / gcd;

        // Simplify common ratios
        if (ratioWidth == ratioHeight)
        {
            return "1:1 (Square)";
        }
        if (ratioWidth == 16 && ratioHeight == 9)
        {
            return "16:9 (Widescreen)";
        }
        if (ratioWidth == 4 && ratioHeight == 3)
        {
            return "4:3 (Standard)";
        }
        if (ratioWidth == 3 && ratioHeight == 2)
        {
            return "3:2";
        }

        return $"{ratioWidth}:{ratioHeight}";
    }

    /// <summary>Gets engagement summary (views, likes, downloads)</summary>
    public static string GetEngagementSummary(this DatasetItemDto item)
    {
        // These properties don't exist in DTO, return empty
        // TODO: Add Views, Likes, Downloads properties to DatasetItemDto if needed
        return string.Empty;
    }

    /// <summary>Gets the photographer name (placeholder property)</summary>
    public static string? Photographer(this DatasetItemDto item)
    {
        // Photographer is not in the DTO
        // Check metadata dictionary for photographer
        if (item.Metadata.TryGetValue("photographer", out var photographer))
        {
            return photographer;
        }
        if (item.Metadata.TryGetValue("Photographer", out var photographerCap))
        {
            return photographerCap;
        }
        if (item.Metadata.TryGetValue("author", out var author))
        {
            return author;
        }
        if (item.Metadata.TryGetValue("Author", out var authorCap))
        {
            return authorCap;
        }
        return null;
    }

    /// <summary>Gets the format (file extension)</summary>
    public static string Format(this DatasetItemDto item)
    {
        // Format is not in the DTO
        // Try to extract from image URL or metadata
        if (item.Metadata.TryGetValue("format", out var format))
        {
            return format;
        }
        if (item.Metadata.TryGetValue("Format", out var formatCap))
        {
            return formatCap;
        }

        // Try to extract from URL
        string url = item.ImageUrl ?? item.ThumbnailUrl ?? string.Empty;
        if (!string.IsNullOrEmpty(url))
        {
            string extension = System.IO.Path.GetExtension(url).TrimStart('.');
            if (!string.IsNullOrEmpty(extension))
            {
                return extension.ToUpperInvariant();
            }
        }

        return "Unknown";
    }

    /// <summary>Gets views count (placeholder property)</summary>
    public static int Views(this DatasetItemDto item)
    {
        // Views is not in the DTO
        if (item.Metadata.TryGetValue("views", out var viewsStr) && int.TryParse(viewsStr, out int views))
        {
            return views;
        }
        return 0;
    }

    /// <summary>Gets likes count (placeholder property)</summary>
    public static int Likes(this DatasetItemDto item)
    {
        // Likes is not in the DTO
        if (item.Metadata.TryGetValue("likes", out var likesStr) && int.TryParse(likesStr, out int likes))
        {
            return likes;
        }
        return 0;
    }

    /// <summary>Gets downloads count (placeholder property)</summary>
    public static int Downloads(this DatasetItemDto item)
    {
        // Downloads is not in the DTO
        if (item.Metadata.TryGetValue("downloads", out var downloadsStr) && int.TryParse(downloadsStr, out int downloads))
        {
            return downloads;
        }
        return 0;
    }

    /// <summary>Gets dominant colors list (placeholder property)</summary>
    public static List<string> DominantColors(this DatasetItemDto item)
    {
        // DominantColors is not in the DTO
        // Try to get from metadata
        if (item.Metadata.TryGetValue("dominant_colors", out var colorsStr))
        {
            return colorsStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        if (item.Metadata.TryGetValue("colors", out var colorsStr2))
        {
            return colorsStr2.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        }
        return new List<string>();
    }

    /// <summary>Gets the location (placeholder property)</summary>
    public static string? Location(this DatasetItemDto item)
    {
        // Location is not in the DTO
        // Check metadata dictionary for location
        if (item.Metadata.TryGetValue("location", out var location))
        {
            return location;
        }
        if (item.Metadata.TryGetValue("Location", out var locationCap))
        {
            return locationCap;
        }
        if (item.Metadata.TryGetValue("photo_location_name", out var photoLocation))
        {
            return photoLocation;
        }
        return null;
    }

    /// <summary>Gets the average color (placeholder property)</summary>
    public static string? AverageColor(this DatasetItemDto item)
    {
        // AverageColor is not in the DTO
        // Check metadata dictionary for average color
        if (item.Metadata.TryGetValue("average_color", out var avgColor))
        {
            return avgColor;
        }
        if (item.Metadata.TryGetValue("AverageColor", out var avgColorCap))
        {
            return avgColorCap;
        }
        if (item.Metadata.TryGetValue("color_hex", out var colorHex))
        {
            return colorHex;
        }
        if (item.Metadata.TryGetValue("dominant_color", out var dominantColor))
        {
            return dominantColor;
        }
        return null;
    }

    /// <summary>Greatest Common Divisor for aspect ratio calculation</summary>
    private static int GCD(int a, int b)
    {
        while (b != 0)
        {
            int temp = b;
            b = a % b;
            a = temp;
        }
        return a;
    }
}
