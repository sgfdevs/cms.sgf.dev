#nullable enable

using System;
using System.Linq;
using SGFDevs.ViewModels;
using Umbraco.Cms.Web.Common;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Extensions;

namespace SgfDevs.Dev;

// Content interest groups are documents, never Umbraco member security groups.
public class MemberProfileChoices(UmbracoHelper helper)
{
    public virtual (MemberProfileChoice[] Skills, MemberProfileChoice[] Groups)? Get()
    {
        var roots = helper.ContentAtRoot().ToArray();
        var tags = roots.FirstOrDefault(x => x.Name == "Tags");
        var home = roots.FirstOrDefault(x => x.Name == "Home");
        var skills = tags?.Children().FirstOrDefault(x => x.ContentType.Alias == "tagGroup" &&
            string.Equals(x.Name, "skills", StringComparison.OrdinalIgnoreCase));
        if (skills is null || home is null) return null;
        return (
            skills.Children().Where(x => x.ContentType.Alias == "tag").Select(x =>
                new MemberProfileChoice(x.Key.ToString(), x.Value<string>("displayName") is { Length: > 0 } name ? name : x.Name)).ToArray(),
            home.Descendants().Where(x => x.ContentType.Alias == "group").Select(x =>
                new MemberProfileChoice(x.Key.ToString(), x.Name)).ToArray());
    }
}
