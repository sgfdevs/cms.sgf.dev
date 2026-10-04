#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.ViewModels;
using SgfDevs.Dev;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
public class PublicMemberController : ControllerBase
{
    private readonly PublicMemberService _publicMemberService;

    public PublicMemberController(PublicMemberService publicMemberService)
    {
        _publicMemberService = publicMemberService;
    }

    [HttpGet("api/v1/public/members/{username}", Name = "PublicMember_Get")]
    [ProducesResponseType<PublicMemberProfileDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublicMemberProfileDto>> Get(string username)
    {
        var member = await _publicMemberService.GetAsync(username);
        return member is null
            ? NotFound(new ProblemDetails { Title = "Member profile not found.", Status = StatusCodes.Status404NotFound })
            : Ok(member);
    }
}
