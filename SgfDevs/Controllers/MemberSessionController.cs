#nullable enable

using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Web.Common.Security;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class MemberSessionController(IMemberSignInManager signInManager, IMemberManager memberManager) : ControllerBase
{
    [HttpPost("api/v1/member/login", Name = "Member_Login")]
    [ProducesResponseType<MemberLoginResult>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MemberLoginResult>> Login(MemberLoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 256 ||
            string.IsNullOrEmpty(request.Password) || request.Password.Length > 4096)
            return Ok(new MemberLoginResult(false));

        // Umbraco trims usernames and uses its configured identity normalizer and password hasher.
        var result = await signInManager.PasswordSignInAsync(
            request.Username, request.Password, request.RememberMe, lockoutOnFailure: true);
        return Ok(new MemberLoginResult(result.Succeeded));
    }

    [HttpGet("api/v1/member/session", Name = "Member_Session")]
    [ProducesResponseType<MemberSessionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<MemberSessionDto>> Session()
    {
        if (!await AuthenticateMemberAsync())
            return Unauthorized();

        var member = await memberManager.GetCurrentMemberAsync();
        return member?.UserName is not { Length: > 0 } username
            ? Unauthorized()
            : Ok(new MemberSessionDto(username, member.Name ?? username));
    }

    [HttpPost("api/v1/member/logout", Name = "Member_Logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout()
    {
        // Idempotent, including expired sessions. Never use the backoffice/default scheme.
        await AuthenticateMemberAsync();
        await signInManager.SignOutAsync();
        return NoContent();
    }

    private async Task<bool> AuthenticateMemberAsync()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        HttpContext.User = result.Succeeded && result.Principal is not null
            ? result.Principal
            : new System.Security.Claims.ClaimsPrincipal();
        return result.Succeeded;
    }
}
