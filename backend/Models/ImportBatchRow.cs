using System.ComponentModel.DataAnnotations;

namespace MEval.Api.Models;

public class ImportBatchRow
{
    public int Id { get; set; }

    public int BatchId { get; set; }

    public int RowNumber { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Email { get; set; } = string.Empty;

    public RowStatus Status { get; set; } = RowStatus.Pending;

    // Navigation property
    public ImportBatch Batch { get; set; } = null!;
}
