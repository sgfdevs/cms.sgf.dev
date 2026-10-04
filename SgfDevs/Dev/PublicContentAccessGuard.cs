#nullable enable

using System.Security.Claims;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;

namespace SgfDevs.Dev;

public interface IPublicContentProtectionLookup
{
    bool IsProtectedPath(string contentPath);
}

public class PublicContentProtectionLookup : IPublicContentProtectionLookup
{
    private readonly IPublicAccessService _publicAccessService;

    public PublicContentProtectionLookup(IPublicAccessService publicAccessService)
    {
        _publicAccessService = publicAccessService;
    }

    public bool IsProtectedPath(string contentPath) => _publicAccessService.IsProtected(contentPath).Success;
}

public class PublicContentAccessGuard
{
    private static readonly ClaimsPrincipal AnonymousPrincipal = new(new ClaimsIdentity());
    private readonly IPublicAccessChecker _publicAccessChecker;
    private readonly IPublicContentProtectionLookup _protectionLookup;

    public PublicContentAccessGuard(
        IPublicAccessChecker publicAccessChecker,
        IPublicContentProtectionLookup protectionLookup)
    {
        _publicAccessChecker = publicAccessChecker;
        _protectionLookup = protectionLookup;
    }

    public Task<bool> AllowsAnonymousAsync(IPublishedContent content) => AllowsAnonymousContentAsync(content.Id, content.Path);

    public async Task<bool> AllowsAnonymousContentAsync(int contentId, string contentPath)
    {
        if (!_protectionLookup.IsProtectedPath(contentPath))
        {
            return true;
        }

        var status = await _publicAccessChecker.HasMemberAccessToContentAsync(contentId, AnonymousPrincipal);
        return status == PublicAccessStatus.AccessAccepted;
    }
}
