using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.Dev;
using Examine;
using SgfDevs.Dev;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Web.Common;
using Umbraco.Cms.Web.Common.Filters;
using Tag = Umbraco.Cms.Web.Common.PublishedModels.Tag;
using Umbraco.Extensions;

namespace SGFDevs.Controllers;

[ApiController]
public class DevsApiController : Controller
{
    private DirectoryHelper _directoryHelper;
    private IMemberService _memberService;
    private IExamineManager _examineManager;
    private UmbracoHelper _helper;
    private IMemberManager _memberManager;
    private IMediaService _mediaService;
    private MediaFileManager _mediaFileManager;
    private IShortStringHelper _shortStringHelper;
    private IContentTypeBaseServiceProvider _contentTypeBaseServiceProvider;
    private MediaUrlGeneratorCollection _mediaUrlGeneratorCollection;
    private NewsletterHelper _newsletterHelper;
    private readonly MemberConverter _memberConverter;
    private readonly MemberTagDisplayService _memberTagDisplayService;


    public DevsApiController(
        DirectoryHelper directoryHelper,
        IMemberService memberService,
        IExamineManager examineManager,
        UmbracoHelper helper,
        IMemberManager memberManager,
        IMediaService mediaService,
        MediaFileManager mediaFileManager,
        IShortStringHelper shortStringHelper,
        IContentTypeBaseServiceProvider contentTypeBaseServiceProvider,
        MediaUrlGeneratorCollection mediaUrlGeneratorCollection,
        NewsletterHelper newsletterHelper,
        MemberConverter memberConverter,
        MemberTagDisplayService memberTagDisplayService
    )
    {
        _directoryHelper = directoryHelper;
        _memberService = memberService;
        _examineManager = examineManager;
        _helper = helper;
        _memberManager = memberManager;
        _mediaService = mediaService;
        _mediaFileManager = mediaFileManager;
        _shortStringHelper = shortStringHelper;
        _contentTypeBaseServiceProvider = contentTypeBaseServiceProvider;
        _mediaUrlGeneratorCollection = mediaUrlGeneratorCollection;
        _newsletterHelper = newsletterHelper;
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
    [Produces("application/json")]
    [ProducesResponseType<IReadOnlyList<PublicDirectoryMemberDto>>(StatusCodes.Status200OK)]
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

    [Route("api/profile/image-process")]
    [UmbracoMemberAuthorize]
    public async Task<IActionResult> UploadProfileImage()
    {
        var currentMember = await _memberManager.GetCurrentMemberAsync();
        if (currentMember == null)
        {
            return Forbid();
        }
        var member = _memberConverter.FromContent(_memberManager.AsPublishedMember(currentMember));

        //var request = HttpContext.Current.Request;
        //var request = HttpContext.Request;
        var request = Request;

        if (request.Form.Files.Count > 0)
        {
            var file = request.Form.Files[0];

            if(file.Length > 0)
            {
                var filesExtension = System.IO.Path.GetExtension(file.FileName);
                var newFileName = member.Username + filesExtension;
                var membersMediaFolder = _mediaService.GetRootMedia().FirstOrDefault(x => x.Name.InvariantEquals("Members"));

                // Need to explore and see if mediaService.SetMediaFileContent will work to
                // update an item if it already exists instead of always creating a new one.
                // For now, just create dupes!
                // - Myke
                var media = _mediaService.CreateMedia(member.Username, membersMediaFolder, Constants.Conventions.MediaTypes.Image);

                media.SetValue(
                    _mediaFileManager,
                    _mediaUrlGeneratorCollection,
                    _shortStringHelper,
                    _contentTypeBaseServiceProvider,
                    Constants.Conventions.Media.File,
                    newFileName,
                    file.OpenReadStream()
                );
                _mediaService.Save(media);

                var imageUdi = new GuidUdi("media", media.Key).ToString();

                return Ok(imageUdi);
            }
        }

        return BadRequest();
    }

    [HttpPost]
    [Route("api/newsletter/signup")]
    public async Task<IActionResult> NewsletterSignUp([FromForm] string email, [FromForm(Name = "name")] string honeypot)
    {
        if (string.IsNullOrEmpty(honeypot))
        {
            await _newsletterHelper.Subscribe(email);
        }

        return Ok();
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
