#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using SGFDevs.Models;
using SGFDevs.ViewModels;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class MemberRegistrationController(
    IMemberManager memberManager, IMemberSignInManager signInManager,
    IMemberService memberService, IMemberTypeService memberTypeService,
    IMemberGroupService memberGroupService, ILogger<MemberRegistrationController> logger) : ControllerBase
{
    [HttpPost("api/v1/member/register", Name = "Member_Register")]
    [ProducesResponseType<MemberRegistrationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<MemberRegistrationResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberRegistrationResult>> Register(MemberRegistrationRequest request)
    {
        // Reuse the legacy form's actual shared validation, including the exact SGF|sgf challenge.
        var model = new RegisterModel
        {
            FirstName = request.FirstName?.Trim() ?? "", LastName = request.LastName?.Trim() ?? "",
            Email = request.Email?.Trim() ?? "", Username = request.Username?.Trim() ?? "",
            Password = request.Password ?? "", ChallengeQuestion = request.ChallengeQuestion ?? ""
        };
        var values = new Dictionary<string, string>
        {
            ["firstName"] = model.FirstName, ["lastName"] = model.LastName,
            ["email"] = model.Email, ["username"] = model.Username,
            ["password"] = model.Password, ["challengeQuestion"] = model.ChallengeQuestion
        };
        var errors = new Dictionary<string, string[]>();
        foreach (var (field, value) in values)
            if (value.Length > (field == "password" ? 4096 : 256))
                errors[field] = ["This value is too long."];
        // Bound regex work before running the shared validator.
        if (errors.Count > 0) return Ok(new MemberRegistrationResult(false, errors));
        var validation = new List<ValidationResult>();
        Validator.TryValidateObject(model, new ValidationContext(model), validation, validateAllProperties: true);
        foreach (var item in validation)
            foreach (var name in item.MemberNames)
                errors[char.ToLowerInvariant(name[0]) + name[1..]] = [item.ErrorMessage ?? "Invalid value."];
        if (errors.Count > 0) return Ok(new MemberRegistrationResult(false, errors));

        MemberIdentityUser? created = null;
        try
        {
            // These are existing CMS prerequisites. Registration must never invent schema or roles.
            var type = memberTypeService.Get("Member");
            if (type is null || !new[] { "firstName", "lastName", "username" }.All(type.PropertyTypeExists) ||
                memberGroupService.GetByName("SGF Devs") is null)
                return Unavailable();

            if (memberService.GetByEmail(model.Email) is not null)
                return Failed("email", "A member with that email already exists.");
            if (memberService.GetByUsername(model.Username) is not null)
                return Failed("username", "Ope. This username is already taken.");

            var identity = MemberIdentityUser.CreateNew(model.Username, model.Email, "Member", true,
                model.FirstName + " " + model.LastName);
            var result = await memberManager.CreateAsync(identity, model.Password);
            if (!result.Succeeded)
            {
                // Identity descriptions may contain submitted values. Map only known codes.
                if (result.Errors.Any(e => e.Code == "DuplicateEmail"))
                    return Failed("email", "A member with that email already exists.");
                if (result.Errors.Any(e => e.Code == "DuplicateUserName"))
                    return Failed("username", "Ope. This username is already taken.");
                if (result.Errors.Any(e => e.Code.StartsWith("Password", StringComparison.Ordinal)))
                    return Failed("password", PasswordValidationRules.ErrorMessage);
                return Failed("", "Unable to create your account.");
            }
            created = identity;
            var member = memberService.GetById(identity.Key);
            if (member is null) throw new InvalidOperationException();
            member.SetValue("firstName", model.FirstName);
            member.SetValue("lastName", model.LastName);
            member.SetValue("username", model.Username);
            if (!memberService.Save(member).Success) throw new InvalidOperationException();
            var roles = await memberManager.AddToRolesAsync(identity, ["SGF Devs"]);
            if (!roles.Succeeded) throw new InvalidOperationException();
            // Saves can be cancelled by CMS notifications. Do not claim success on partial persistence.
            var saved = memberService.GetById(identity.Key);
            if (saved is null || !saved.IsApproved || saved.ContentType.Alias != "Member" ||
                saved.GetValue<string>("firstName") != model.FirstName ||
                saved.GetValue<string>("lastName") != model.LastName ||
                saved.GetValue<string>("username") != model.Username ||
                !(await memberManager.GetRolesAsync(identity)).Contains("SGF Devs"))
                throw new InvalidOperationException();

            await signInManager.SignInAsync(identity, isPersistent: true);
            return Ok(new MemberRegistrationResult(true, new()));
        }
        catch
        {
            // No exception details: service exceptions can include personal/security values.
            logger.LogWarning("Member registration could not complete.");
            if (created is not null)
            {
                try { await signInManager.SignOutAsync(); } catch { }
                try
                {
                    if (!(await memberManager.DeleteAsync(created)).Succeeded)
                        logger.LogError("Incomplete member registration cleanup failed.");
                }
                catch { logger.LogError("Incomplete member registration cleanup failed."); }
            }
            return Unavailable();
        }
    }

    private ActionResult<MemberRegistrationResult> Failed(string field, string message) =>
        Ok(new MemberRegistrationResult(false, new() { [field] = [message] }));

    private ActionResult<MemberRegistrationResult> Unavailable() =>
        StatusCode(StatusCodes.Status503ServiceUnavailable,
            new MemberRegistrationResult(false, new() { [""] = ["Registration is unavailable right now."] }));
}
