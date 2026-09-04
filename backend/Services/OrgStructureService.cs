using MEval.Api.Data;
using MEval.Api.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MEval.Api.Services;

public class OrgStructureService : IOrgStructureService
{
    private readonly AppDbContext _context;

    public OrgStructureService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<OrgStructureTreeDto> GetOrgStructureTreeAsync()
    {
        var companies = await _context.Companies
            .Where(c => c.IsActive)
            .OrderBy(c => c.Name)
            .Select(c => new CompanyTreeNodeDto(
                c.CompanyId,
                c.Name,
                c.Departments
                    .Where(d => d.IsActive)
                    .OrderBy(d => d.Name)
                    .Select(d => new DepartmentTreeNodeDto(
                        d.DepartmentId,
                        d.Name,
                        d.Sections
                            .Where(s => s.IsActive)
                            .OrderBy(s => s.Name)
                            .Select(s => new SectionTreeNodeDto(s.SectionId, s.Name))
                            .ToList()
                    ))
                    .ToList()
            ))
            .ToListAsync();

        return new OrgStructureTreeDto(companies);
    }

    public async Task<List<CompanyDto>> GetCompaniesAsync(bool includeInactive = false)
    {
        var query = _context.Companies.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(c => c.IsActive);
        }

        return await query
            .OrderBy(c => c.Name)
            .Select(c => new CompanyDto(c.CompanyId, c.Name, c.IsActive))
            .ToListAsync();
    }

    public async Task<List<DepartmentDto>> GetDepartmentsByCompanyAsync(int companyId, bool includeInactive = false)
    {
        var query = _context.Departments.Where(d => d.CompanyId == companyId);
        if (!includeInactive)
        {
            query = query.Where(d => d.IsActive);
        }

        return await query
            .OrderBy(d => d.Name)
            .Select(d => new DepartmentDto(d.DepartmentId, d.CompanyId, d.Name, d.IsActive))
            .ToListAsync();
    }

    public async Task<List<SectionDto>> GetSectionsByDepartmentAsync(int departmentId, bool includeInactive = false)
    {
        var query = _context.Sections.Where(s => s.DepartmentId == departmentId);
        if (!includeInactive)
        {
            query = query.Where(s => s.IsActive);
        }

        return await query
            .OrderBy(s => s.Name)
            .Select(s => new SectionDto(s.SectionId, s.DepartmentId, s.Name, s.IsActive))
            .ToListAsync();
    }

    public async Task<List<PositionDto>> GetPositionsAsync(bool includeInactive = false)
    {
        var query = _context.Positions.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(p => p.IsActive);
        }

        return await query
            .OrderBy(p => p.NLevel)
            .ThenBy(p => p.Name)
            .Select(p => new PositionDto(p.PositionId, p.Name, p.NLevel, p.IsActive))
            .ToListAsync();
    }
}
