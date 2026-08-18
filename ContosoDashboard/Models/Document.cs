using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ContosoDashboard.Models;

public class Document
{
    [Key]
    public int DocumentId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? Description { get; set; }

    [Required]
    [MaxLength(50)]
    public string Category { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Tags { get; set; }

    [Required]
    public int UploadedByUserId { get; set; }

    public int? ProjectId { get; set; }

    // Original user-supplied filename, for display only — never used to build a file path
    [Required]
    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    // System-generated relative path: {userId}/{projectId|"personal"}/{guid}.{ext}
    [Required]
    [MaxLength(400)]
    public string FilePath { get; set; } = string.Empty;

    public long FileSizeBytes { get; set; }

    [Required]
    [MaxLength(255)]
    public string FileType { get; set; } = string.Empty;

    // SHA-256 hex digest of file content, used for duplicate detection (FR-031)
    [Required]
    [MaxLength(64)]
    public string ContentHash { get; set; } = string.Empty;

    [Required]
    public DocumentScanStatus ScanStatus { get; set; } = DocumentScanStatus.PendingScan;

    public DateTime UploadedDate { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedDate { get; set; } = DateTime.UtcNow;

    [Timestamp]
    public byte[] RowVersion { get; set; } = Array.Empty<byte>();

    // Navigation properties
    [ForeignKey("UploadedByUserId")]
    public virtual User UploadedByUser { get; set; } = null!;

    [ForeignKey("ProjectId")]
    public virtual Project? Project { get; set; }

    public virtual ICollection<DocumentShare> Shares { get; set; } = new List<DocumentShare>();
    public virtual ICollection<DocumentActivityLog> ActivityLogs { get; set; } = new List<DocumentActivityLog>();
}

public enum DocumentScanStatus
{
    PendingScan,
    Available,
    Rejected
}
