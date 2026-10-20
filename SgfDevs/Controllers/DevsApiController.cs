using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.Dev;
using Examine;
using SgfDevs.Dev;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Tag = Umbraco.Cms.Web.Common.PublishedModels.Tag;
using Umbraco.Extensions;

namespace SGFDevs.Controllers;

[ApiController]
public class DevsApiController : Controller
{
    private readonly DirectoryHelper _directoryHelper;
    private readonly IMemberService _memberService;
    private readonly IExamineManager _examineManager;
    private readonly MemberConverter _memberConverter;
    private readonly MemberTagDisplayService _memberTagDisplayService;

    public DevsApiController(
        DirectoryHelper directoryHelper,
        IMemberService memberService,
        IExamineManager examineManager,
        MemberConverter memberConverter,
        MemberTagDisplayService memberTagDisplayService)
    {
        _directoryHelper = directoryHelper;
        _memberService = memberService;
        _examineManager = examineManager;
        _memberConverter = memberConverter;
        _memberTagDisplayService = memberTagDisplayService;
    }

    [HttpGet("api/tags/skills", Name = "Directory_GetSkillNames")]
    [Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<string>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetAllSkills()
    {
        var skills = _directoryHelper.GetSkills() ?? [];
        return Ok(skills.Select(GetTagDisplayName).ToList());
    }

    [HttpGet("api/directory/filters/skills", Name = "Directory_GetSkillFilters")]
    [Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<PublicSkillFilterDto>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<PublicSkillFilterDto>> GetSkillsFilters()
    {
        var skills = (_directoryHelper.GetSkills() ?? [])
            .Select(x => new PublicSkillFilterDto
            {
                Name = GetTagDisplayName(x),
                Id = x.Id,
                Key = x.Key,
                IsActive = false
            })
            .ToList();

        return Ok(skills);
    }

    [HttpGet("api/directory/search", Name = "Directory_Search")]
    [ProducesResponseType<IReadOnlyList<PublicDirectoryMemberDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    public ActionResult<IReadOnlyList<PublicDirectoryMemberDto>> GetSearch(
        [FromQuery] string skills,
        [FromQuery] int? skip,
        [FromQuery] int? take)
    {
        if (!DirectorySearchQuery.TryCreate(skills, skip, take, out var searchQuery, out var error))
        {
            return BadRequest(new ProblemDetails
            {
                Title = "Invalid directory search query.",
                Detail = error,
                Status = StatusCodes.Status400BadRequest
            });
        }

        if (searchQuery.SkillTerms.Count == 0)
        {
            var allMembers = searchQuery.ApplyTo(_directoryHelper.GetAllMembers()
                    .Select(BuildDirectoryResult))
                .ToList();

            return Ok(allMembers);
        }

        if (!_examineManager.TryGetIndex(Constants.UmbracoIndexes.MembersIndexName, out var index))
        {
            return Ok(new List<PublicDirectoryMemberDto>());
        }

        var searcher = index.Searcher;
        var criteria = searcher.CreateQuery("member");
        var query = criteria.GroupedOr(["nodeName", "skillKeys", "skillsTags", "skills", "skillIds"], searchQuery.SkillTerms.ToArray());
        var results = query.Execute();

        if (!results.Any())
        {
            return Ok(new List<PublicDirectoryMemberDto>());
        }

        var ids = results.Select(result => int.Parse(result.Id)).ToArray();
        var filteredMembers = searchQuery.ApplyTo(_memberService.GetAllMembers(ids)
                .Select(BuildDirectoryResult)
                .OrderBy(m => m.Name))
            .ToList();

        return Ok(filteredMembers);
    }

    private PublicDirectoryMemberDto BuildDirectoryResult(IMember umbracoMember)
    {
        var member = _memberConverter.FromMember(umbracoMember);
        var url = "/member/" + member.Username;
        var location = member.City + ", " + member.State;
        var image = "/images/pipey.jpg";

        if(member.ProfileImage != null)
        {
            image = member.ProfileImage.GetCropUrl(width: 500);
        }

        return new PublicDirectoryMemberDto
        {
            Name = member.Name,
            Location = location,
            Image = image,
            Url = url,
            Tags = _memberTagDisplayService.GetDisplayMemberTags(member)
        };
    }

    private static string GetTagDisplayName(Tag tag) =>
        string.IsNullOrEmpty(tag.DisplayName) ? tag.Name : tag.DisplayName;
}
