using System.Collections.Generic;

namespace HartsysDatasetEditor.Core.Models;

public sealed class ApiKeySettings
{
    public Dictionary<string, string> Tokens { get; set; } = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
}
