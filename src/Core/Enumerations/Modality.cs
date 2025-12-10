namespace DatasetStudio.Core.Enumerations;

/// <summary>Defines the type of data modality in a dataset</summary>
public enum Modality
{
    /// <summary>Image dataset (photos, pictures, screenshots)</summary>
    Image = 0,

    /// <summary>Text dataset (documents, captions, prompts) - TODO: Implement text support</summary>
    Text = 1,

    /// <summary>Video dataset (clips, recordings) - TODO: Implement video support</summary>
    Video = 2,

    /// <summary>3D model dataset (meshes, point clouds) - TODO: Implement 3D support</summary>
    ThreeD = 3,

    /// <summary>Audio dataset (sound clips, music) - TODO: Implement audio support</summary>
    Audio = 4,

    /// <summary>Unknown or mixed modality - fallback option</summary>
    Unknown = 99
}
