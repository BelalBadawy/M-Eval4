using MEval.Api.DTOs;
using MEval.Api.Security;
using MEval.Api.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MEval.Api.Endpoints;

public static class OrgEndpoints
{
    public static IEndpointRouteBuilder MapOrgEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/org")
            .WithTags("Organization")
            .RequireAuthorization()
            .AddEndpointFilter<FirstLoginGatewayFilter>();

        // 1. Structure Tree (Active-only)
        group.MapGet("/structure", async (IOrgStructureService orgService) =>
        {
            var tree = await orgService.GetOrgStructureTreeAsync();
            return Results.Ok(tree);
        })
        .RequirePermission("org.read")
        .WithName("GetOrgStructure")
        .WithSummary("Retrieve active organization hierarchy tree (Companies -> Departments -> Sections)");

        // 2. Active Companies
        group.MapGet("/companies", async (IOrgStructureService orgService) =>
        {
            var companies = await orgService.GetCompaniesAsync();
            return Results.Ok(companies);
        })
        .RequirePermission("org.read")
        .WithName("GetActiveCompanies")
        .WithSummary("Retrieve list of active companies");

        // 3. Departments in Company
        group.MapGet("/companies/{id:int}/departments", async (int id, IOrgStructureService orgService) =>
        {
            var departments = await orgService.GetDepartmentsByCompanyAsync(id);
            return Results.Ok(departments);
        })
        .RequirePermission("org.read")
        .WithName("GetDepartmentsByCompany")
        .WithSummary("Retrieve active departments within a specific company");

        // 4. Sections in Department
        group.MapGet("/departments/{id:int}/sections", async (int id, IOrgStructureService orgService) =>
        {
            var sections = await orgService.GetSectionsByDepartmentAsync(id);
            return Results.Ok(sections);
        })
        .RequirePermission("org.read")
        .WithName("GetSectionsByDepartment")
        .WithSummary("Retrieve active sections within a specific department");

        // 5. Positions
        group.MapGet("/positions", async (IOrgStructureService orgService) =>
        {
            var positions = await orgService.GetPositionsAsync();
            return Results.Ok(positions);
        })
        .RequirePermission("org.read")
        .WithName("GetPositions")
        .WithSummary("Retrieve list of positions ordered by tier (NLevel)");

        return app;
    }
}
