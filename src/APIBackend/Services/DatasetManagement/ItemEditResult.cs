using DatasetStudio.DTO.Datasets;

namespace DatasetStudio.APIBackend.Services.DatasetManagement;

/// <summary>
/// The outcome of <see cref="IDatasetItemRepository.EditItemAsync"/>. <see cref="Item"/> is the item
/// as written when <see cref="Status"/> is Updated, the unchanged current item when it is
/// PreconditionFailed, and null when it is NotFound.
/// </summary>
/// <param name="Status">What happened.</param>
/// <param name="Item">The item after the edit, or as it stands when the edit was refused.</param>
public sealed record ItemEditResult(ItemEditStatus Status, DatasetItemDto? Item);
