#nullable enable
using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicGroupDto
{
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? AboutHtml { get; set; }
    public string? ImageUrl { get; set; }
    public string? Location { get; set; }
    public string? EstablishedText { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? TwitterUrl { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? FacebookUrl { get; set; }
    public string? InstagramUrl { get; set; }
    public string? YouTubeUrl { get; set; }
    public IReadOnlyList<PublicGroupSkillDto> Skills { get; set; } = [];
    public IReadOnlyList<PublicGroupLeaderDto> Leaders { get; set; } = [];
    public IReadOnlyList<PublicGroupPresentationDto> UpcomingPresentations { get; set; } = [];
}

public class PublicGroupSkillDto
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
}

public class PublicGroupLeaderDto
{
    public string Name { get; set; } = string.Empty;
    public string ListLabel { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string ProfilePath { get; set; } = string.Empty;
    public IReadOnlyList<string> Tags { get; set; } = [];
}

public class PublicGroupPresentationDto
{
    public string Title { get; set; } = string.Empty;
    public string EventName { get; set; } = string.Empty;
    public string StartsAtLocal { get; set; } = string.Empty;
    public IReadOnlyList<PublicHomePresenterDto> Presenters { get; set; } = [];
}
