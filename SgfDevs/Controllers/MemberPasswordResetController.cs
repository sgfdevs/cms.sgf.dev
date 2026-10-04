#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SGFDevs.Models;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Core.Security;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class MemberPasswordResetController(
    IMemberManager memberManager, IEmailSender emailSender, IOptions<GlobalSettings> globalSettings,
    IConfiguration configuration, IHostEnvironment environment,
    ILogger<MemberPasswordResetController> logger) : ControllerBase
{
    private const string InvalidLink = "This reset link is invalid or has expired.";
    private const string Unavailable = "Password reset is unavailable right now.";

    [HttpPost("api/v1/member/forgot-password", Name = "Member_ForgotPassword")]
    [ProducesResponseType<MemberPasswordResetResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<MemberPasswordResetResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberPasswordResetResult>> ForgotPassword(MemberForgotPasswordRequest request)
    {
        var model = new ForgotPasswordModel { Email = request.Email?.Trim() ?? "" };
        if (model.Email.Length > 256) return Failed("email", "This value is too long.");
        var errors = Validate(model);
        if (errors.Count > 0) return Ok(new MemberPasswordResetResult(false, errors));
        try
        {
            // Check deployment-wide readiness before looking up an account.
            var resetUrl = ResetUrl();
            var smtp = globalSettings.Value.Smtp;
            if (resetUrl is null || !emailSender.CanSendRequiredEmail() || string.IsNullOrWhiteSpace(smtp?.From))
                return NotAvailable();

            var member = await memberManager.FindByEmailAsync(model.Email);
            if (member is not null)
            {
                try
                {
                    var token = await memberManager.GeneratePasswordResetTokenAsync(member);
                    var link = QueryHelpers.AddQueryString(resetUrl, new Dictionary<string, string?>
                    {
                        ["memberId"] = member.Id, ["token"] = token
                    });
                    var body = $"<p>Hi {HtmlEncoder.Default.Encode(member.Name ?? "")},</p><p>Someone requested a password reset for your Springfield Devs account.</p><p>If that was you, use the link below to choose a new password:</p><p><a href=\"{HtmlEncoder.Default.Encode(link)}\">Reset your password</a></p><p>If you did not request this, you can ignore this email.</p>";
                    await emailSender.SendAsync(new EmailMessage(smtp.From, member.Email,
                        "Reset your Springfield Devs password", body, true), "PasswordReset", true, smtp.EmailExpiration);
                }
                catch
                {
                    // Sending failure must not change the public response for an existing account.
                    logger.LogWarning("Password reset email could not be sent.");
                }
            }
            return Ok(new MemberPasswordResetResult(true, new()));
        }
        catch
        {
            logger.LogWarning("Password reset is unavailable.");
            return NotAvailable();
        }
    }

    [HttpPost("api/v1/member/reset-password", Name = "Member_ResetPassword")]
    [ProducesResponseType<MemberPasswordResetResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<MemberPasswordResetResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberPasswordResetResult>> ResetPassword(MemberResetPasswordRequest request)
    {
        // Do not trim or decode opaque identity values. QueryHelpers encoded them in the email.
        var model = new ResetPasswordModel
        {
            MemberId = request.MemberId ?? "", Token = request.Token ?? "",
            Password = request.Password ?? "", ConfirmPassword = request.ConfirmPassword ?? ""
        };
        if (string.IsNullOrWhiteSpace(model.MemberId) || model.MemberId.Length > 256 ||
            string.IsNullOrWhiteSpace(model.Token) || model.Token.Length > 8192)
            return Failed("", InvalidLink);
        if (model.Password.Length > 4096 || model.ConfirmPassword.Length > 4096)
            return Failed("password", "This value is too long.");
        var errors = Validate(model);
        if (errors.Count > 0) return Ok(new MemberPasswordResetResult(false, errors));
        try
        {
            var member = await memberManager.FindByIdAsync(model.MemberId);
            if (member is null) return Failed("", InvalidLink);
            var result = await memberManager.ResetPasswordAsync(member, model.Token, model.Password);
            if (result.Succeeded) return Ok(new MemberPasswordResetResult(true, new()));
            // Never forward Identity descriptions, which can include private submitted values.
            if (result.Errors.Any(e => e.Code == "InvalidToken")) return Failed("", InvalidLink);
            if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
                return Failed("password", PasswordValidationRules.ErrorMessage);
            return Failed("", InvalidLink);
        }
        catch
        {
            logger.LogWarning("Password reset could not complete.");
            return NotAvailable();
        }
    }

    private string? ResetUrl()
    {
        var value = configuration["SGFDevs:MemberBridge:FrontendOrigin"];
        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin) ||
            !string.IsNullOrEmpty(origin.UserInfo) || origin.AbsolutePath != "/" ||
            !string.IsNullOrEmpty(origin.Query) || !string.IsNullOrEmpty(origin.Fragment) ||
            string.IsNullOrEmpty(origin.Host) ||
            (origin.Scheme != Uri.UriSchemeHttps && !(environment.IsDevelopment() &&
                origin.Scheme == Uri.UriSchemeHttp && origin.Host is "localhost" or "127.0.0.1" or "[::1]")))
            return null;
        return new Uri(origin, "/reset-password").AbsoluteUri;
    }

    private static Dictionary<string, string[]> Validate(object model)
    {
        var validation = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), validation, true);
        var errors = new Dictionary<string, string[]>();
        foreach (var item in validation)
            foreach (var name in item.MemberNames)
                errors[char.ToLowerInvariant(name[0]) + name[1..]] = [item.ErrorMessage ?? "Invalid value."];
        return errors;
    }

    private ActionResult<MemberPasswordResetResult> Failed(string field, string message) =>
        Ok(new MemberPasswordResetResult(false, new() { [field] = [message] }));

    private ActionResult<MemberPasswordResetResult> NotAvailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable,
            new MemberPasswordResetResult(false, new() { [""] = [Unavailable] }));
}
