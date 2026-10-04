#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.DeliveryApi;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;
using Umbraco.Cms.Core.PropertyEditors.ValueConverters;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.PublishedModels;

namespace SgfDevs.Dev;

// Keep native model conversion for Razor. Only company Delivery picker values change.
public class CompanyTagDeliveryValueConverter(
    IUmbracoContextAccessor context, IMemberService members, IApiContentBuilder contentBuilder,
    IApiMediaBuilder mediaBuilder, IPublishedContentCache contentCache,
    IPublishedMediaCache mediaCache, IPublishedMemberCache memberCache,
    IPublicContentProtectionLookup protection)
    : MultiNodeTreePickerValueConverter(context, members, contentBuilder, mediaBuilder, contentCache, mediaCache, memberCache),
      IDeliveryApiPropertyValueConverter
{
    public override bool IsConverter(IPublishedPropertyType propertyType) =>
        propertyType.ContentType?.Alias == "company" &&
        propertyType.Alias is "skillTags" or "companyTags" && base.IsConverter(propertyType);

    // Do not retain a projection after a protection change.
    public new PropertyCacheLevel GetDeliveryApiPropertyCacheLevel(IPublishedPropertyType propertyType) => PropertyCacheLevel.None;
    public new PropertyCacheLevel GetDeliveryApiPropertyCacheLevelForExpansion(IPublishedPropertyType propertyType) => PropertyCacheLevel.None;
    public new Type GetDeliveryApiPropertyValueType(IPublishedPropertyType propertyType) => typeof(IEnumerable<CompanySkillValue>);
    public new object ConvertIntermediateToDeliveryApiObject(IPublishedElement owner, IPublishedPropertyType propertyType,
        PropertyCacheLevel referenceCacheLevel, object? inter, bool preview, bool expanding)
    {
        if (preview || propertyType.Alias != "skillTags" || inter is not IEnumerable<Udi> udis) return Array.Empty<CompanySkillValue>();
        // Resolve published documents only. Never expand a picked node's properties or route.
        return udis.OfType<GuidUdi>().Where(udi => udi.EntityType == Constants.UdiEntityType.Document)
            .Select(udi => contentCache.GetById(false, udi.Guid)).OfType<Tag>()
            .Where(tag => !protection.IsProtectedPath(tag.Path))
            .Select(Project).ToArray();
    }

    internal static CompanySkillValue Project(Tag tag) => new(
        string.IsNullOrEmpty(tag.DisplayName) ? tag.Name : tag.DisplayName, tag.Key.ToString());
}

public record CompanySkillValue(string Name, string DirectoryFilterValue);
