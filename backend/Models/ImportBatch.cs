using System.ComponentModel.DataAnnotations;

namespace MEval.Api.Models;

public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public int TotalRows { get; set; } = 0;
    public int ValidRows { get; set; } = 0;
    public int InvalidRows { get; set; } = 0;
    public int CreatedRows { get; set; } = 0;
    public int UpdatedRows { get; set; } = 0;
    public int FailedRows { get; set; } = 0;

    public ImportStatus Status { get; set; } = ImportStatus.Pending;
    public DuplicateStrategy DuplicateStrategy { get; set; } = DuplicateStrategy.Skip;
    public CommitPolicy CommitPolicy { get; set; } = CommitPolicy.PartialValidOnly;

    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }

    // Navigation properties
    public User CreatedByUser { get; set; } = null!;
    public ICollection<ImportBatchRow> Rows { get; set; } = new List<ImportBatchRow>();
    public ICollection<ImportRowError> Errors { get; set; } = new List<ImportRowError>();
    public ICollection<User> CreatedUsers { get; set; } = new List<User>();
}
