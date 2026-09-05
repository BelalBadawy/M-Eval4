using MEval.Api.DTOs;

namespace MEval.Api.Services;

public interface IOrgStructureService
{
    Task<OrgStructureTreeDto> GetOrgStructureTreeAsync();
    Task<List<CompanyDto>> GetCompaniesAsync(bool includeInactive = false);
    Task<List<DepartmentDto>> GetDepartmentsByCompanyAsync(int companyId, bool includeInactive = false);
    Task<List<SectionDto>> GetSectionsByDepartmentAsync(int departmentId, bool includeInactive = false);
    Task<List<PositionDto>> GetPositionsAsync(bool includeInactive = false);
}
