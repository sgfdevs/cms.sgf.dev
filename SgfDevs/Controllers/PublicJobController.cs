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
public class PublicJobController(PublicJobService jobs) : ControllerBase
{
    [HttpGet("api/v1/public/jobs", Name = "PublicJobs_List")]
    [ProducesResponseType<IReadOnlyList<PublicJobDto>>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<IReadOnlyList<PublicJobDto>>> List()
    {
        var result = await jobs.ListAsync();
        return result is null ? Missing() : Ok(result);
    }

    [HttpGet("api/v1/public/jobs/{company}/{job}", Name = "PublicJobs_Get")]
    [ProducesResponseType<PublicJobDto>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<ActionResult<PublicJobDto>> Get(string? company, string? job)
    {
        var result = await jobs.GetAsync(company, job);
        return result is null ? Missing() : Ok(result);
    }

    private NotFoundObjectResult Missing() => NotFound(new ProblemDetails
    {
        Title = "Job content not found.", Status = StatusCodes.Status404NotFound
    });
}
