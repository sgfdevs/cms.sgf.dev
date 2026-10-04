#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace SGFDevs.ViewModels;

// Omitted fields are unchanged. Unknown fields, including identity, roles and media, are rejected.
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class MemberProfileEditRequest
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? JobTitle { get; set; }
    public string? AboutText { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public bool? AvailableForHire { get; set; }
    public bool? AvailableForContractWork { get; set; }
    public string? TwitterUrl { get; set; }
    public string? TwitchUrl { get; set; }
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? MeetupUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? YouTubeUrl { get; set; }
    public string[]? Skills { get; set; }
    public string[]? Groups { get; set; }
}

public sealed record MemberProfileChoice(string Key, string Name);
public sealed record MemberProfileEditDto(
    MemberProfileEditRequest Values, string? ProfileImageUrl,
    MemberProfileChoice[] Skills, MemberProfileChoice[] Groups);
public sealed record MemberProfileEditResult(bool Succeeded, Dictionary<string, string[]> Errors);
