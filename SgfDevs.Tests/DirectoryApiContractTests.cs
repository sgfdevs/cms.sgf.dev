using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using Xunit;

namespace SgfDevs.Tests;

public class DirectoryApiContractTests
{
    [Fact]
    public void PublicDirectoryMemberDto_SerializesOnlyPublicCardFields()
    {
        var dto = new PublicDirectoryMemberDto
        {
            Name = "Jane Developer",
            Location = "Springfield, MO",
            Image = "/images/pipey.jpg",
            Url = "/member/janedev",
            Tags = ["2024 Supporting Member"]
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();

        Assert.Equal(["image", "location", "name", "tags", "url"], properties);
        Assert.DoesNotContain(properties, property => property.Contains("email", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("password", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("token", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(properties, property => property.Contains("account", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PublicSkillFilterDto_KeepsLegacyDirectoryConsumerShape()
    {
        var dto = new PublicSkillFilterDto
        {
            Name = "C#",
            Id = 1234,
            Key = Guid.Parse("11111111-2222-3333-4444-555555555555"),
            IsActive = false
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, JsonSerializerOptions.Web));
        var properties = document.RootElement.EnumerateObject().Select(property => property.Name).Order().ToArray();

        Assert.Equal(["id", "isActive", "key", "name"], properties);
    }

    [Theory]
    [InlineData(null, null, null, 0, null, 0)]
    [InlineData("42, 42, CSharp", 5, 25, 5, 25, 2)]
    [InlineData("  ", 0, 1, 0, 1, 0)]
    public void DirectorySearchQuery_NormalizesCompatibleQueryValues(
        string? skills,
        int? skip,
        int? take,
        int expectedSkip,
        int? expectedTake,
        int expectedSkillTerms)
    {
        var valid = DirectorySearchQuery.TryCreate(skills, skip, take, out var query, out var error);

        Assert.True(valid, error);
        Assert.NotNull(query);
        Assert.Equal(expectedSkip, query.Skip);
        Assert.Equal(expectedTake, query.Take);
        Assert.Equal(expectedSkillTerms, query.SkillTerms.Count);
    }

    [Fact]
    public void DirectorySearchQuery_DoesNotPageUnlessRequested()
    {
        var valid = DirectorySearchQuery.TryCreate(null, null, null, out var query, out var error);

        Assert.True(valid, error);
        Assert.NotNull(query);
        Assert.Equal(Enumerable.Range(1, 600), query.ApplyTo(Enumerable.Range(1, 600)));
    }

    [Fact]
    public void DirectorySearchQuery_AppliesRequestedPaging()
    {
        var valid = DirectorySearchQuery.TryCreate(null, 10, 5, out var query, out var error);

        Assert.True(valid, error);
        Assert.NotNull(query);
        Assert.Equal([11, 12, 13, 14, 15], query.ApplyTo(Enumerable.Range(1, 600)));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(0, 0)]
    [InlineData(0, DirectorySearchQuery.MaxTake + 1)]
    public void DirectorySearchQuery_RejectsInvalidPaging(int skip, int take)
    {
        Assert.False(DirectorySearchQuery.TryCreate(null, skip, take, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void DirectorySearchQuery_RejectsOverLargeSkillFilters()
    {
        var tooManySkills = string.Join(',', Enumerable.Range(1, DirectorySearchQuery.MaxSkillTerms + 1));

        Assert.False(DirectorySearchQuery.TryCreate(tooManySkills, null, null, out _, out var error));
        Assert.Contains(DirectorySearchQuery.MaxSkillTerms.ToString(), error);
    }

    [Theory]
    [InlineData(nameof(DevsApiController.GetAllSkills), "api/tags/skills", "Directory_GetSkillNames", typeof(IReadOnlyList<string>))]
    [InlineData(nameof(DevsApiController.GetSkillsFilters), "api/directory/filters/skills", "Directory_GetSkillFilters", typeof(IReadOnlyList<PublicSkillFilterDto>))]
    [InlineData(nameof(DevsApiController.GetSearch), "api/directory/search", "Directory_Search", typeof(IReadOnlyList<PublicDirectoryMemberDto>))]
    public void DirectoryEndpoints_HaveStableRoutesOperationNamesAndResponseTypes(
        string methodName,
        string routeTemplate,
        string operationName,
        Type responseType)
    {
        var method = typeof(DevsApiController).GetMethod(methodName)!;
        var route = method.GetCustomAttribute<HttpGetAttribute>()!;
        var okResponse = method.GetCustomAttributes<ProducesResponseTypeAttribute>()
            .Single(attribute => attribute.StatusCode == 200);

        Assert.Equal(routeTemplate, route.Template);
        Assert.Equal(operationName, route.Name);
        Assert.Equal(responseType, okResponse.Type);
    }

    [Fact]
    public void ProfileImageEndpoint_RemainsMemberProtected()
    {
        var method = typeof(DevsApiController).GetMethod(nameof(DevsApiController.UploadProfileImage))!;

        Assert.Contains(method.GetCustomAttributes(), attribute =>
            attribute.GetType().Name == "UmbracoMemberAuthorizeAttribute");
    }
}
