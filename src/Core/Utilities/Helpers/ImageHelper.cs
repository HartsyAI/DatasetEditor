using System.Collections.Generic;

namespace DatasetStudio.Core.Utilities.Helpers;

/// <summary>Helper utilities for working with images and image URLs</summary>
public static class ImageHelper
{
    /// <summary>Adds resize parameters to an image URL (for Unsplash and similar services)</summary>
    public static string AddResizeParams(string imageUrl, int? width = null, int? height = null, int? quality = null)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return string.Empty;
        }

        List<string> queryParameters = new();

        if (width.HasValue)
        {
            queryParameters.Add($"w={width.Value}");
        }

        if (height.HasValue)
        {
            queryParameters.Add($"h={height.Value}");
        }

        if (quality.HasValue)
        {
            queryParameters.Add($"q={quality.Value}");
        }

        if (queryParameters.Count == 0)
        {
            return imageUrl;
        }

        string separator = imageUrl.Contains('?') ? "&" : "?";
        return $"{imageUrl}{separator}{string.Join("&", queryParameters)}";
    }

    /// <summary>Gets a thumbnail URL with common dimensions</summary>
    public static string GetThumbnailUrl(string imageUrl, string size = "medium")
    {
        int width = size.ToLowerInvariant() switch
        {
            "small" => 150,
            "medium" => 320,
            "large" => 640,
            _ => 320
        };

        return AddResizeParams(imageUrl, width: width, quality: 80);
    }

    /// <summary>Calculates aspect ratio from dimensions</summary>
    public static double CalculateAspectRatio(int width, int height)
    {
        return height > 0 ? (double)width / height : 0;
    }

    /// <summary>Gets a human-friendly aspect ratio description</summary>
    public static string GetAspectRatioDescription(double aspectRatio)
    {
        return aspectRatio switch
        {
            > 1.7 => "Wide",
            > 1.4 => "16:9",
            > 1.2 => "3:2",
            > 0.9 and < 1.1 => "Square",
            < 0.75 => "Tall",
            _ => "Standard"
        };
    }

    // TODO: Add support for different image URL patterns (Cloudinary, ImgIX, etc.)
    // TODO: Add support for format conversion parameters
    // TODO: Add support for WebP/AVIF conversion
}
