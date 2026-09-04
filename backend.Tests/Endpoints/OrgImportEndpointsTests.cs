using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClosedXML.Excel;
using FluentAssertions;
using MEval.Api.DTOs;
using MEval.Api.Tests.Infrastructure;
using Xunit;

namespace MEval.Api.Tests.Endpoints;

public class OrgImportEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public OrgImportEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string email, string password, string ip)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", ip);

        var loginResponse = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(email, password));
        loginResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginBody = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginBody!.AccessToken);
        return client;
    }

    private byte[] CreateValidWorkbookBytes()
    {
        using var wb = new XLWorkbook();

        var wsC = wb.Worksheets.Add("Companies");
        wsC.Cell(1, 1).Value = "CompanyId";
        wsC.Cell(1, 2).Value = "Name";
        wsC.Cell(2, 1).Value = 100;
        wsC.Cell(2, 2).Value = "Endpoint Test Company";

        var wsD = wb.Worksheets.Add("Departments");
        wsD.Cell(1, 1).Value = "DepartmentId";
        wsD.Cell(1, 2).Value = "CompanyId";
        wsD.Cell(1, 3).Value = "Name";
        wsD.Cell(2, 1).Value = 200;
        wsD.Cell(2, 2).Value = 100;
        wsD.Cell(2, 3).Value = "Endpoint Test Dept";

        var wsS = wb.Worksheets.Add("Sections");
        wsS.Cell(1, 1).Value = "SectionId";
        wsS.Cell(1, 2).Value = "DepartmentId";
        wsS.Cell(1, 3).Value = "Name";
        wsS.Cell(2, 1).Value = 300;
        wsS.Cell(2, 2).Value = 200;
        wsS.Cell(2, 3).Value = "Endpoint Test Section";

        var wsP = wb.Worksheets.Add("Positions");
        wsP.Cell(1, 1).Value = "PositionId";
        wsP.Cell(1, 2).Value = "Name";
        wsP.Cell(1, 3).Value = "NLevel";
        wsP.Cell(2, 1).Value = 400;
        wsP.Cell(2, 2).Value = "Endpoint CEO";
        wsP.Cell(2, 3).Value = 1;

        var wsE = wb.Worksheets.Add("Employees");
        string[] headers =
        {
            "EmployeeId", "EmployeeNumber", "FullName", "Email",
            "CompanyId", "CompanyName", "DepartmentId", "DepartmentName",
            "SectionId", "SectionName", "PositionId", "PositionName", "NLevel",
            "ManagerEmployeeId", "EmploymentStatus", "HireDate", "ResignationDate"
        };
        for (int i = 0; i < headers.Length; i++)
        {
            wsE.Cell(1, i + 1).Value = headers[i];
        }

        wsE.Cell(2, 1).Value = 8800;
        wsE.Cell(2, 2).Value = "EMP-8800";
        wsE.Cell(2, 3).Value = "Endpoint Executive";
        wsE.Cell(2, 4).Value = "endpoint.exec@meval.local";
        wsE.Cell(2, 5).Value = 100;
        wsE.Cell(2, 6).Value = "Endpoint Test Company";
        wsE.Cell(2, 7).Value = 200;
        wsE.Cell(2, 8).Value = "Endpoint Test Dept";
        wsE.Cell(2, 9).Value = 300;
        wsE.Cell(2, 10).Value = "Endpoint Test Section";
        wsE.Cell(2, 11).Value = 400;
        wsE.Cell(2, 12).Value = "Endpoint CEO";
        wsE.Cell(2, 13).Value = 1;
        wsE.Cell(2, 14).Value = "";
        wsE.Cell(2, 15).Value = 1;
        wsE.Cell(2, 16).Value = "2022-01-01";
        wsE.Cell(2, 17).Value = "";

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task OrgImportEndpoints_WithoutPermission_ShouldReturn403Forbidden()
    {
        var normalClient = await CreateAuthenticatedClientAsync(_factory.NormalUserEmail, _factory.TestUserPassword, "10.0.3.1");

        var templateResponse = await normalClient.GetAsync("/api/v1/org/imports/template");
        templateResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(new byte[] { 1, 2, 3 }), "file", "test.xlsx");

        var dryRunResponse = await normalClient.PostAsync("/api/v1/org/imports/dry-run", content);
        dryRunResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task DownloadTemplate_ShouldReturnExcelFile()
    {
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.3.2");

        var response = await adminClient.GetAsync("/api/v1/org/imports/template");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
    }

    [Fact]
    public async Task DryRunAndExecute_WithValidWorkbook_ShouldSucceed()
    {
        var adminClient = await CreateAuthenticatedClientAsync(_factory.AdminUserEmail, _factory.TestUserPassword, "10.0.3.3");
        var bytes = CreateValidWorkbookBytes();

        // 1. Dry Run
        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(fileContent, "file", "org_import_test.xlsx");

            var dryRunResponse = await adminClient.PostAsync("/api/v1/org/imports/dry-run", content);
            dryRunResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var result = await dryRunResponse.Content.ReadFromJsonAsync<OrgImportDryRunResultDto>();
            result.Should().NotBeNull();
            result!.IsValid.Should().BeTrue();
        }

        // 2. Execute
        using (var content = new MultipartFormDataContent())
        {
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
            content.Add(fileContent, "file", "org_import_test.xlsx");

            var executeResponse = await adminClient.PostAsync("/api/v1/org/imports/execute", content);
            executeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var response = await executeResponse.Content.ReadFromJsonAsync<OrgImportExecuteResponse>();
            response.Should().NotBeNull();
            response!.Success.Should().BeTrue();
        }
    }
}
