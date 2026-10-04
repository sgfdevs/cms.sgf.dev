#nullable enable

using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicMemberProfileDto
{
    public required string Username { get; init; }
    public required string Name { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
    public string? JobTitle { get; init; }
    public required string ProfileImageUrl { get; init; }
    public IReadOnlyList<string> Tags { get; init; } = [];
    public string? City { get; init; }
    public string? State { get; init; }
    public string? JoinMonthLabel { get; init; }

    /// <summary>User-authored biography HTML. Clients must sanitize before HTML rendering.</summary>
    public string? AboutHtml { get; init; }
    public IReadOnlyList<PublicMemberSkillDto> Skills { get; init; } = [];
    public string? WebsiteUrl { get; init; }
    public string? WebsiteLabel { get; init; }
    public string? TwitterUrl { get; init; }
    public string? LinkedInUrl { get; init; }
    public string? FacebookUrl { get; init; }
    public string? InstagramUrl { get; init; }
    public string? YouTubeUrl { get; init; }
    public bool AvailableForHire { get; init; }
    public bool AvailableForContractWork { get; init; }
}

public class PublicMemberSkillDto
{
    public required string Name { get; init; }

    /// <summary>String term accepted by the directory search skills query, not a member identifier.</summary>
    public required string DirectoryFilterValue { get; init; }
}
