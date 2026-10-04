#nullable enable

using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using SgfDevs.Dev;

namespace SGFDevs.Controllers;

// MemberBridge protects this anonymous server-only endpoint, without a member session.
[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class NewsletterController(NewsletterHelper newsletter) : ControllerBase
{
    [HttpPost("api/v1/member/newsletter", Name = "Newsletter_Signup")]
    [RequestSizeLimit(4096)]
    [ProducesResponseType<NewsletterSignupResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<NewsletterSignupResult>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<NewsletterSignupResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<NewsletterSignupResult>> Signup(
        NewsletterSignupRequest request, CancellationToken cancellationToken = default)
    {
        // Whitespace is filled too. Never trim the legacy empty-only honeypot.
        if (!string.IsNullOrEmpty(request.Name))
            return BadRequest(new NewsletterSignupResult(false, "Unable to sign up. Please try again."));

        var email = request.Email?.Trim() ?? "";
        if (email.Length > 254 || !new EmailAddressAttribute().IsValid(email) ||
            !MailAddress.TryCreate(email, out var address) || address.Address != email)
            return BadRequest(new NewsletterSignupResult(false, "Please enter a valid email address."));

        if (!newsletter.IsAvailable || !await newsletter.Subscribe(email, cancellationToken))
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new NewsletterSignupResult(false, "Newsletter signup is unavailable right now. Please try again."));
        return Ok(new NewsletterSignupResult(true));
    }
}

public sealed record NewsletterSignupRequest(string? Email, string? Name);
public sealed record NewsletterSignupResult(bool Accepted, string? Error = null);
