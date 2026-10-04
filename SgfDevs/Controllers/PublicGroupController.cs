#nullable enable
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.ViewModels;
using SgfDevs.Dev;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
public class PublicGroupController(PublicGroupService groups) : ControllerBase
{
    [HttpGet("api/v1/public/groups", Name = "PublicGroups_List")]
    [ProducesResponseType<IReadOnlyList<PublicGroupDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<PublicGroupDto>>> List()
    {
        var result = await groups.ListAsync();
        return result is null ? Missing() : Ok(result);
    }

    [HttpGet("api/v1/public/groups/{slug}", Name = "PublicGroups_Get")]
    [ProducesResponseType<PublicGroupDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublicGroupDto>> Get(string? slug)
    {
        var result = await groups.GetAsync(slug);
        return result is null ? Missing() : Ok(result);
    }

    private NotFoundObjectResult Missing() => NotFound(new ProblemDetails
    {
        Title = "Group content not found.", Status = StatusCodes.Status404NotFound
    });
}
