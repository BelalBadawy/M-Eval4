namespace MEval.Api.Models;

public enum EmploymentStatus
{
    Active = 1,
    Resigned = 2,
    Terminated = 3
}

public class Employee
{
    public int EmployeeId { get; set; } // External HR ID
    public string EmployeeNumber { get; set; } = string.Empty; // Immutable business key
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }

    public int CompanyId { get; set; }
    public int? DepartmentId { get; set; }
    public int? SectionId { get; set; }
    public int PositionId { get; set; }

    public int? DirectManagerId { get; set; }

    public EmploymentStatus EmploymentStatus { get; set; } = EmploymentStatus.Active;
    public int? UserId { get; set; } // 1:1 link to Module 1 User

    public bool IsEvaluationEligible { get; set; } = true; // Local-only
    public DateOnly HireDate { get; set; }
    public DateOnly? ResignationDate { get; set; }

    public bool IsActive { get; set; } = true; // Local-only record validity
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    // Navigation
    public Company Company { get; set; } = null!;
    public Department? Department { get; set; }
    public Section? Section { get; set; }
    public Position Position { get; set; } = null!;
    public Employee? DirectManager { get; set; }
    public ICollection<Employee> DirectReports { get; set; } = new List<Employee>();
    public User? User { get; set; }
}
