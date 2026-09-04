namespace MEval.Api.Models;

public class Position
{
    public int PositionId { get; set; } // External HR ID
    public string Name { get; set; } = string.Empty;
    public int NLevel { get; set; } // 1 = Top, increasing downward
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
