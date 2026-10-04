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
public class PublicLeadershipController(PublicLeadershipService leadership) : ControllerBase
{
    [HttpGet("api/v1/public/leadership", Name = "PublicLeadership_Get")]
    [ProducesResponseType<PublicLeadershipDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublicLeadershipDto>> Get()
    {
        var result = await leadership.GetAsync();
        return result is null ? NotFound(new ProblemDetails
        {
            Title = "Leadership content not found.", Status = StatusCodes.Status404NotFound
        }) : Ok(result);
    }
}
