namespace MEval.Api.DTOs;

public record CompanyDto(
    int CompanyId,
    string Name,
    bool IsActive
);

public record DepartmentDto(
    int DepartmentId,
    int CompanyId,
    string Name,
    bool IsActive
);

public record SectionDto(
    int SectionId,
    int DepartmentId,
    string Name,
    bool IsActive
);

public record PositionDto(
    int PositionId,
    string Name,
    int NLevel,
    bool IsActive
);

public record SectionTreeNodeDto(
    int SectionId,
    string Name
);

public record DepartmentTreeNodeDto(
    int DepartmentId,
    string Name,
    List<SectionTreeNodeDto> Sections
);

public record CompanyTreeNodeDto(
    int CompanyId,
    string Name,
    List<DepartmentTreeNodeDto> Departments
);

public record OrgStructureTreeDto(
    List<CompanyTreeNodeDto> Companies
);
