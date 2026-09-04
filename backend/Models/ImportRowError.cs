using System.ComponentModel.DataAnnotations;

namespace MEval.Api.Models;

public class ImportRowError
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid BatchId { get; set; }

    public int RowNumber { get; set; }

    [Required]
    [MaxLength(50)]
    public string ColumnName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(100)]
    public string? RawValue { get; set; }

    // Navigation property
    public ImportBatch Batch { get; set; } = null!;
}
