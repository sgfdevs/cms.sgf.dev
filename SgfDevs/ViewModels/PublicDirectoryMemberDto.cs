#nullable enable

using System.Collections.Generic;

namespace SGFDevs.ViewModels;

public class PublicDirectoryMemberDto
{
    public required string Name { get; set; }
    public required string Location { get; set; }
    public required string Image { get; set; }
    public required string Url { get; set; }
    public required IEnumerable<string> Tags { get; set; }
}
