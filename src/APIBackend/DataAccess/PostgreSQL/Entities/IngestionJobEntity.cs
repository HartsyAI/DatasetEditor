using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DatasetStudio.APIBackend.DataAccess.PostgreSQL.Entities;

/// <summary>What an ingestion job does.</summary>
public enum IngestionJobKind
{
    /// <summary>Parse an uploaded file staged under Storage:UploadPath.</summary>
    Upload = 0,

    /// <summary>Import (or set up streaming for) a HuggingFace repository.</summary>
    HuggingFace = 1,

    /// <summary>Scan a folder on the server's disk.</summary>
    Folder = 2
}

/// <summary>Where an ingestion job is in its life.</summary>
public enum IngestionJobState
{
    /// <summary>Waiting for a worker.</summary>
    Queued = 0,

    /// <summary>Claimed by a worker and running.</summary>
    Running = 1,

    /// <summary>Finished successfully.</summary>
    Completed = 2,

    /// <summary>Finished with an error, or interrupted after it had written items.</summary>
    Failed = 3,

    /// <summary>
    /// A slot reserved by an upload whose file is still being written. Counts toward the queue
    /// limit, so concurrent uploads can't all pass it before any of them is queued, but is never
    /// claimed. Becomes Queued when the file is staged, or is deleted if staging fails.
    /// </summary>
    Staging = 4
}

/// <summary>
/// A queued or finished ingestion job (table <c>ingestion_jobs</c>). Jobs used to live in an
/// in-memory channel, so a restart lost every queued job and left its dataset Pending or
/// Processing forever (audit I-1). Workers claim rows with <c>FOR UPDATE SKIP LOCKED</c>.
/// </summary>
[Table("ingestion_jobs")]
public class IngestionJobEntity
{
    /// <summary>Primary key.</summary>
    [Key]
    [Column("id")]
    public Guid Id { get; set; }

    /// <summary>The dataset being filled.</summary>
    [Column("dataset_id")]
    public Guid DatasetId { get; set; }

    /// <summary>What the job does.</summary>
    [Column("kind")]
    public IngestionJobKind Kind { get; set; }

    /// <summary>Lifecycle state.</summary>
    [Column("state")]
    public IngestionJobState State { get; set; } = IngestionJobState.Queued;

    /// <summary>Staged upload to parse (Upload jobs); deleted when the job finishes for good.</summary>
    [MaxLength(1000)]
    [Column("file_path")]
    public string? FilePath { get; set; }

    /// <summary>Folder to scan (Folder jobs).</summary>
    [MaxLength(1000)]
    [Column("folder_path")]
    public string? FolderPath { get; set; }

    /// <summary>
    /// The HuggingFace import request as JSON (HuggingFace jobs). Its access token is encrypted
    /// with ASP.NET Data Protection before storing, never kept in clear text.
    /// </summary>
    [Column("payload_json")]
    public string? PayloadJson { get; set; }

    /// <summary>How many times a worker has claimed the job.</summary>
    [Column("attempts")]
    public int Attempts { get; set; }

    /// <summary>
    /// The dataset's item count when the latest attempt started. Ingestion appends, so after a
    /// restart a job is only safe to run again if the count has not moved since.
    /// </summary>
    [Column("items_before")]
    public long? ItemsBefore { get; set; }

    /// <summary>The worker that holds or last held the job.</summary>
    [MaxLength(200)]
    [Column("claimed_by")]
    public string? ClaimedBy { get; set; }

    /// <summary>The failure, when <see cref="State"/> is Failed.</summary>
    [Column("error")]
    public string? Error { get; set; }

    /// <summary>When the job was queued (claim order).</summary>
    [Column("created_at")]
    public DateTime CreatedAt { get; set; }

    /// <summary>When the latest attempt started.</summary>
    [Column("started_at")]
    public DateTime? StartedAt { get; set; }

    /// <summary>When the job completed or failed for good.</summary>
    [Column("finished_at")]
    public DateTime? FinishedAt { get; set; }

    /// <summary>The dataset (cascade: deleting a dataset deletes its jobs).</summary>
    public DatasetEntity? Dataset { get; set; }
}
