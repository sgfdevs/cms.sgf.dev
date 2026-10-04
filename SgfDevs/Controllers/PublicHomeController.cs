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
public class PublicHomeController : ControllerBase
{
    private readonly PublicHomeService _publicHomeService;

    public PublicHomeController(PublicHomeService publicHomeService)
    {
        _publicHomeService = publicHomeService;
    }

    [HttpGet("api/v1/public/home", Name = "PublicHome_Get")]
    [Produces("application/json")]
    [ProducesResponseType<PublicHomeDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status500InternalServerError, "application/problem+json")]
    public async Task<ActionResult<PublicHomeDto>> Get()
    {
        var result = await _publicHomeService.GetHomeAsync();

        return result.Status switch
        {
            PublicHomeResultStatus.Ok => Ok(result.Home),
            PublicHomeResultStatus.NotFound => NotFound(new ProblemDetails
            {
                Title = "Home content not found.",
                Status = StatusCodes.Status404NotFound
            }),
            _ => Problem(
                title: "Home content is not uniquely configured.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
