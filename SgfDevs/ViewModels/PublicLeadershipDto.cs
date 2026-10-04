#nullable enable
using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicLeadershipDto
{
    public IReadOnlyList<PublicLeadershipMemberDto> Officers { get; set; } = [];
    public IReadOnlyList<PublicLeadershipMemberDto> BoardOfDirectors { get; set; } = [];
    public IReadOnlyList<PublicLeadershipMemberDto> History { get; set; } = [];
}

public class PublicLeadershipMemberDto
{
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string? OfficerTitle { get; set; }
    public string? OfficerBio { get; set; }
}
