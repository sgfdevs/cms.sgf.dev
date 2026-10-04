#nullable enable

using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicHomeDto
{
    public PublicHomeDevNightDto? NextDevNight { get; set; }
    public required PublicHomeDirectoryPreviewDto Directory { get; set; }
    public required IReadOnlyList<PublicHomeSponsorDto> Sponsors { get; set; }
}

public class PublicHomeDevNightDto
{
    public required string Name { get; set; }
    public required string StartsAtLocal { get; set; }
    public required string TimeZone { get; set; }
    public required string DateLabel { get; set; }
    public required string DateTimeAttribute { get; set; }
    public required IReadOnlyList<PublicHomePresentationDto> Presentations { get; set; }
}

public class PublicHomePresentationDto
{
    public required string Title { get; set; }
    public string? MeetupUrl { get; set; }
    public required IReadOnlyList<PublicHomePresenterDto> Presenters { get; set; }
    public PublicHomeGroupDto? Group { get; set; }
}

public class PublicHomePresenterDto
{
    public required string Name { get; set; }
    public required string ImageUrl { get; set; }
    public string? ProfilePath { get; set; }
    public required IReadOnlyList<string> Tags { get; set; }
}

public class PublicHomeGroupDto
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public required bool ShowAttribution { get; set; }
}

public class PublicHomeDirectoryPreviewDto
{
    public required int TotalMembers { get; set; }
    public required IReadOnlyList<PublicDirectoryMemberDto> DailyMembers { get; set; }
}

public class PublicHomeSponsorDto
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? WebsiteLabel { get; set; }
    public required bool IsFoundingSponsor { get; set; }
}
