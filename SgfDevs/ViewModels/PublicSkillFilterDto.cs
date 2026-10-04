#nullable enable

using System;

namespace SGFDevs.ViewModels;

public class PublicSkillFilterDto
{
    public required string Name { get; set; }
    public int Id { get; set; }
    public Guid Key { get; set; }
    public bool IsActive { get; set; }
}
