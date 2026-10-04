#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
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
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Models.Validation;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Security;
using Umbraco.Extensions;

namespace SGFDevs.Controllers;

[ApiController]
[AllowAnonymous]
[DisableCors]
public sealed class MemberProfileController(
    IMemberManager manager, IMemberSignInManager signIn, IMemberService members,
    MemberProfileChoices choices, ICoreScopeProvider scopes, PropertyEditorCollection editors,
    IDataTypeService dataTypes, ILogger<MemberProfileController> logger) : ControllerBase
{
    [HttpGet("api/v1/member/profile", Name = "Member_Profile")]
    [ProducesResponseType<MemberProfileEditDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberProfileEditDto>> Profile()
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();
        try
        {
            var member = members.GetById(user.Key);
            var options = choices.Get();
            if (member is null || !HasSchema(member) || options is null) return StatusCode(503);
            var image = manager.AsPublishedMember(user)?.Value<MediaWithCrops>("profileImage")?.GetCropUrl(width: 200);
            return Ok(new MemberProfileEditDto(Read(member), PublicHomeBuilder.GetSafePathOrHttpUrl(image),
                options.Value.Skills, options.Value.Groups));
        }
        catch
        {
            logger.LogWarning("Member profile could not be loaded.");
            return StatusCode(503);
        }
    }

    [HttpPost("api/v1/member/profile", Name = "Member_ProfileUpdate")]
    [RequestSizeLimit(262144)]
    [ProducesResponseType<MemberProfileEditResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<MemberProfileEditResult>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MemberProfileEditResult>> Update(MemberProfileEditRequest request)
    {
        var user = await CurrentAsync();
        if (user is null) return Unauthorized();
        var errors = Validate(request);
        if (errors.Count > 0) return Ok(new MemberProfileEditResult(false, errors));
        try
        {
            // The same ambient CMS scope covers Identity's writes and the profile properties.
            using (var scope = scopes.CreateCoreScope())
            {
                var member = members.GetById(user.Key);
                var options = choices.Get();
                if (member is null || !HasSchema(member) || options is null) return Unavailable();
                var skills = Selection(request.Skills, options.Value.Skills, "skills", errors);
                var groups = Selection(request.Groups, options.Value.Groups, "groups", errors);
                var email = request.Email?.Trim();
                if (email is not null)
                {
                    var duplicate = await manager.FindByEmailAsync(email);
                    if (duplicate is not null && duplicate.Key != user.Key)
                        errors["email"] = ["A member with that email already exists."];
                }
                var first = request.FirstName?.Trim() ?? member.GetValue<string>("firstName");
                var last = request.LastName?.Trim() ?? member.GetValue<string>("lastName");
                var name = string.Join(" ", new[] { first, last }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (name.Length > 255) errors["firstName"] = ["Your full name must be at most 255 characters."];
                // Use the deployed property's editor, mandatory/regex rules and datatype configuration.
                foreach (var field in Fields(request).Where(x => x.Value is not null))
                {
                    var property = member.Properties.First(x => x.Alias == field.Alias).PropertyType;
                    var dataType = await dataTypes.GetAsync(property.DataTypeKey);
                    if (dataType is null || !editors.TryGet(property.PropertyEditorAlias, out var editor)) return Unavailable();
                    if (editor.GetValueEditor(dataType.ConfigurationObject)
                        .Validate(field.Value, property.Mandatory, property.ValidationRegExp, PropertyValidationContext.Empty()).Any())
                        errors[field.Alias] = ["This value does not meet the field requirements."];
                }
                if (errors.Count > 0) return Ok(new MemberProfileEditResult(false, errors));

                // IMemberManager.UpdateAsync validates and normalizes the new email.
                if (email is not null && email != user.Email)
                {
                    user.Email = email;
                    user.EmailConfirmed = false;
                }
                if (request.FirstName is not null || request.LastName is not null) user.Name = name;
                if (email is not null || request.FirstName is not null || request.LastName is not null)
                {
                    var result = await manager.UpdateAsync(user);
                    if (!result.Succeeded) return IdentityFailure(result);
                }
                // Reload after Identity saves. Do not overwrite normalized email or security stamp.
                member = members.GetById(user.Key);
                if (member is null) return Unavailable();
                if (request.FirstName is not null) member.SetValue("firstName", request.FirstName.Trim());
                if (request.LastName is not null) member.SetValue("lastName", request.LastName.Trim());
                if (request.JobTitle is not null) member.SetValue("jobTitle", request.JobTitle.Trim());
                if (request.AboutText is not null) member.SetValue("aboutText", request.AboutText);
                if (request.City is not null) member.SetValue("city", request.City.Trim());
                if (request.State is not null) member.SetValue("state", request.State.Trim());
                if (request.TwitterUrl is not null) member.SetValue("twitterUrl", request.TwitterUrl.Trim());
                if (request.TwitchUrl is not null) member.SetValue("twitchUrl", request.TwitchUrl.Trim());
                if (request.FacebookUrl is not null) member.SetValue("facebookUrl", request.FacebookUrl.Trim());
                if (request.InstagramUrl is not null) member.SetValue("instagramUrl", request.InstagramUrl.Trim());
                if (request.LinkedInUrl is not null) member.SetValue("linkedInUrl", request.LinkedInUrl.Trim());
                if (request.MeetupUrl is not null) member.SetValue("meetupUrl", request.MeetupUrl.Trim());
                if (request.WebsiteUrl is not null) member.SetValue("websiteUrl", request.WebsiteUrl.Trim());
                if (request.YouTubeUrl is not null) member.SetValue("youTubeUrl", request.YouTubeUrl.Trim());
                if (request.AvailableForHire is not null) member.SetValue("availableForHire", request.AvailableForHire.Value);
                if (request.AvailableForContractWork is not null) member.SetValue("availableForContractWork", request.AvailableForContractWork.Value);
                if (skills is not null) member.SetValue("skillsTags", skills);
                if (groups is not null) member.SetValue("groups", groups);
                if (!members.Save(member).Success) return Unavailable();
                var saved = members.GetById(user.Key);
                if (saved is null || saved.Email != (email ?? member.Email) ||
                    ((request.FirstName is not null || request.LastName is not null) && saved.Name != name) ||
                    Fields(request).Any(x => x.Value is not null && saved.GetValue<string>(x.Alias) != x.Value) ||
                    (request.AvailableForHire is not null && saved.GetValue<bool>("availableForHire") != request.AvailableForHire) ||
                    (request.AvailableForContractWork is not null && saved.GetValue<bool>("availableForContractWork") != request.AvailableForContractWork) ||
                    (skills is not null && saved.GetValue<string>("skillsTags") != skills) ||
                    (groups is not null && saved.GetValue<string>("groups") != groups))
                    return Unavailable();
                scope.Complete();
            }
            // Reissue the existing member cookie with updated claims and its persistence choice.
            await signIn.SignInAsync(user, isPersistent: _isPersistent);
            return Ok(new MemberProfileEditResult(true, new()));
        }
        catch
        {
            logger.LogWarning("Member profile could not be saved.");
            return Unavailable();
        }
    }

    private bool _isPersistent;

    private async Task<MemberIdentityUser?> CurrentAsync()
    {
        var auth = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        _isPersistent = auth.Properties?.IsPersistent ?? false;
        HttpContext.User = auth.Succeeded && auth.Principal is not null ? auth.Principal : new();
        return auth.Succeeded ? await manager.GetCurrentMemberAsync() : null;
    }

    private ActionResult<MemberProfileEditResult> IdentityFailure(IdentityResult result) =>
        result.Errors.Any(x => x.Code == "DuplicateEmail")
            ? Ok(new MemberProfileEditResult(false, new() { ["email"] = ["A member with that email already exists."] }))
            : Unavailable();

    private ActionResult<MemberProfileEditResult> Unavailable() =>
        StatusCode(503, new MemberProfileEditResult(false, new() { [""] = ["Profile editing is unavailable right now."] }));

    private static bool HasSchema(IMember member) =>
        member.ContentType.Alias == "Member" &&
        Fields(new()).All(x => member.Properties.Any(p => p.Alias == x.Alias)) &&
        new[] { "skillsTags", "groups", "availableForHire", "availableForContractWork" }.All(a => member.Properties.Any(p => p.Alias == a)) &&
        Fields(new()).All(x => member.Properties.First(p => p.Alias == x.Alias).PropertyType.ValueStorageType ==
            (x.Alias == "aboutText" ? ValueStorageType.Ntext : ValueStorageType.Nvarchar));

    internal static Dictionary<string, string[]> Validate(MemberProfileEditRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Email is not null && (request.Email.Length > 1000 ||
            !new EmailAddressAttribute().IsValid(request.Email.Trim()) || string.IsNullOrWhiteSpace(request.Email)))
            errors["email"] = ["Enter a valid email address of at most 1000 characters."];
        foreach (var field in Fields(request))
        {
            // TextBox uses cmsPropertyData nvarchar(512); Markdown uses ntext.
            // Markdown has no configured maximum. This request cap bounds server/form work.
            if (field.Value is not null && field.Value.Length > (field.Alias == "aboutText" ? 100000 : 512))
                errors[field.Alias] = ["This value is too long."];
        }
        request.TwitterUrl = NormalizeUrl(request.TwitterUrl, "twitterUrl", false, errors);
        request.TwitchUrl = NormalizeUrl(request.TwitchUrl, "twitchUrl", false, errors);
        request.FacebookUrl = NormalizeUrl(request.FacebookUrl, "facebookUrl", false, errors);
        request.InstagramUrl = NormalizeUrl(request.InstagramUrl, "instagramUrl", false, errors);
        request.LinkedInUrl = NormalizeUrl(request.LinkedInUrl, "linkedInUrl", false, errors);
        request.MeetupUrl = NormalizeUrl(request.MeetupUrl, "meetupUrl", false, errors);
        request.WebsiteUrl = NormalizeUrl(request.WebsiteUrl, "websiteUrl", true, errors);
        request.YouTubeUrl = NormalizeUrl(request.YouTubeUrl, "youTubeUrl", false, errors);
        return errors;
    }

    private static string? NormalizeUrl(string? value, string field, bool bare, Dictionary<string, string[]> errors)
    {
        if (value is null) return null;
        value = value.Trim();
        if (value.Length == 0) return "";
        if (value.Length > 512) return value;
        var normalized = PublicMemberService.GetSafeHttpUrl(value, bare);
        if (normalized is null || normalized.Length > 512) errors[field] = ["Enter a safe HTTP or HTTPS URL."];
        return normalized ?? value;
    }

    private static string? Selection(string[]? keys, MemberProfileChoice[] available, string field, Dictionary<string, string[]> errors)
    {
        if (keys is null) return null;
        var allowed = available.Select(x => Guid.Parse(x.Key)).ToHashSet();
        var selected = new List<Guid>();
        if (keys.Length > 100) errors[field] = ["Choose at most 100 items."];
        else foreach (var key in keys)
        {
            if (!Guid.TryParse(key, out var guid) || !allowed.Contains(guid))
                errors[field] = ["Choose only listed items."];
            else if (!selected.Contains(guid)) selected.Add(guid);
        }
        return string.Join(",", selected.Select(x => Udi.Create(Constants.UdiEntityType.Document, x).ToString()));
    }

    private static string[] ReadKeys(string? value) => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
        .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) && uri.Scheme == "umb" && uri.Host == "document" &&
            Guid.TryParse(uri.AbsolutePath.Trim('/'), out var key) ? key.ToString() : null)
        .Where(x => x is not null).Cast<string>().ToArray();

    private static MemberProfileEditRequest Read(IMember member) => new()
    {
        Email = member.Email,
        FirstName = member.GetValue<string>("firstName") ?? "",
        LastName = member.GetValue<string>("lastName") ?? "",
        JobTitle = member.GetValue<string>("jobTitle") ?? "",
        AboutText = member.GetValue<string>("aboutText") ?? "",
        City = member.GetValue<string>("city") ?? "",
        State = member.GetValue<string>("state") ?? "",
        TwitterUrl = member.GetValue<string>("twitterUrl") ?? "",
        TwitchUrl = member.GetValue<string>("twitchUrl") ?? "",
        FacebookUrl = member.GetValue<string>("facebookUrl") ?? "",
        InstagramUrl = member.GetValue<string>("instagramUrl") ?? "",
        LinkedInUrl = member.GetValue<string>("linkedInUrl") ?? "",
        MeetupUrl = member.GetValue<string>("meetupUrl") ?? "",
        WebsiteUrl = member.GetValue<string>("websiteUrl") ?? "",
        YouTubeUrl = member.GetValue<string>("youTubeUrl") ?? "",
        AvailableForHire = member.GetValue<bool>("availableForHire"),
        AvailableForContractWork = member.GetValue<bool>("availableForContractWork"),
        Skills = ReadKeys(member.GetValue<string>("skillsTags")),
        Groups = ReadKeys(member.GetValue<string>("groups"))
    };

    private static (string Alias, string? Value)[] Fields(MemberProfileEditRequest request) =>
    [
        ("firstName", request.FirstName?.Trim()),
        ("lastName", request.LastName?.Trim()),
        ("jobTitle", request.JobTitle?.Trim()),
        ("aboutText", request.AboutText),
        ("city", request.City?.Trim()),
        ("state", request.State?.Trim()),
        ("twitterUrl", request.TwitterUrl?.Trim()),
        ("twitchUrl", request.TwitchUrl?.Trim()),
        ("facebookUrl", request.FacebookUrl?.Trim()),
        ("instagramUrl", request.InstagramUrl?.Trim()),
        ("linkedInUrl", request.LinkedInUrl?.Trim()),
        ("meetupUrl", request.MeetupUrl?.Trim()),
        ("websiteUrl", request.WebsiteUrl?.Trim()),
        ("youTubeUrl", request.YouTubeUrl?.Trim()),
    ];
}
