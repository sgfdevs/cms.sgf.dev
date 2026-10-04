#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using SixLabors.ImageSharp;
using Umbraco.Cms.Core.Security;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class MemberAvatarController(
    IMemberManager manager, MemberAvatarService avatars, ILogger<MemberAvatarController> logger) : ControllerBase
{
    [HttpPost("api/v1/member/avatar", Name = "Member_AvatarUpload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(MemberAvatarImage.RequestBytes)]
    [RequestFormLimits(MultipartBodyLengthLimit = MemberAvatarImage.MaxBytes, ValueCountLimit = 0)]
    [ProducesResponseType<MemberAvatarResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberAvatarResult>> Upload([FromForm] MemberAvatarRequest request)
    {
        var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        HttpContext.User = auth.Succeeded && auth.Principal is not null ? auth.Principal : new();
        var member = auth.Succeeded ? await manager.GetCurrentMemberAsync() : null;
        if (member is null) return Unauthorized();
        var form = await Request.ReadFormAsync(HttpContext.RequestAborted);
        if (Request.Query.Count != 0 || form.Count != 0 || form.Files.Count != 1 || form.Files[0].Name != "file" || request.File is null || request.File.Length == 0)
            return BadRequest();
        if (request.File.Length > MemberAvatarImage.MaxBytes) return StatusCode(413);
        try
        {
            using var source = request.File.OpenReadStream();
            var prepared = await MemberAvatarImage.PrepareAsync(source, HttpContext.RequestAborted);
            using var content = prepared.Content;
            var url = await avatars.SaveAsync(member.Key, content, prepared.Extension);
            return url is null ? StatusCode(503) : Ok(new MemberAvatarResult(url));
        }
        catch (UnknownImageFormatException) { return BadRequest(); }
        catch (InvalidImageContentException) { return BadRequest(); }
        catch (NotSupportedException) { return BadRequest(); }
        catch (Exception)
        {
            logger.LogWarning("Member avatar could not be uploaded.");
            return StatusCode(503);
        }
    }
}
