#nullable enable
using System.Reflection;
using System.Text.Json;
using SgfDevs.Dev;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.PropertyEditors.DeliveryApi;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Web.Common.PublishedModels;
using Xunit;

namespace SgfDevs.Tests;

public class CompanyDeliveryTests
{
    [Fact]
    public void CompanyPickersProjectPublishedPublicSkillsOnlyEvenWhenExpanded()
    {
        var publicKey = Guid.NewGuid();
        var privateKey = Guid.NewGuid();
        var fallback = Proxy<IPublishedValueFallback>((_, _) => null);
        Tag MakeTag(Guid key, string path) => new TestTag(Proxy<IPublishedContent>((m, _) => m.Name switch
        {
            "get_Key" => key, "get_Name" => "Rust", "get_Path" => path, _ => null
        }), fallback);
        var cache = Proxy<IPublishedContentCache>((m, a) =>
        {
            Assert.Equal("GetById", m.Name);
            Assert.False((bool)a![0]!);
            return (Guid)a[1]! == publicKey ? MakeTag(publicKey, "-1,10,20") : MakeTag(privateKey, "-1,10,30");
        });
        var protection = Proxy<IPublicContentProtectionLookup>((_, a) => (string)a![0]! == "-1,10,30");
        var converter = new CompanyTagDeliveryValueConverter(null!, null!, null!, null!, cache, null!, null!, protection);
        IPublishedPropertyType Property(string owner, string alias) => Proxy<IPublishedPropertyType>((m, _) => m.Name switch
        {
            "get_ContentType" => Proxy<IPublishedContentType>((_, _) => owner), "get_Alias" => alias,
            "get_EditorAlias" => Constants.PropertyEditors.Aliases.MultiNodeTreePicker, _ => null
        });
        var property = Property("company", "skillTags");
        Assert.True(converter.IsConverter(property));
        Assert.False(converter.IsConverter(Property("group", "skillTags")));
        Assert.False(converter.IsConverter(Property("member", "skillTags")));
        IDeliveryApiPropertyValueConverter delivery = converter;
        Assert.Equal(PropertyCacheLevel.None, delivery.GetDeliveryApiPropertyCacheLevel(property));
        var udis = new Udi[] { new GuidUdi(Constants.UdiEntityType.Document, publicKey),
            new GuidUdi(Constants.UdiEntityType.Document, privateKey), new GuidUdi(Constants.UdiEntityType.Member, Guid.NewGuid()) };
        foreach (var expanding in new[] { false, true })
        {
            var values = Assert.IsType<CompanySkillValue[]>(delivery.ConvertIntermediateToDeliveryApiObject(null!, property,
                PropertyCacheLevel.None, udis, false, expanding));
            var skill = Assert.Single(values);
            Assert.Equal("Rust language", skill.Name);
            Assert.Equal(publicKey.ToString(), skill.DirectoryFilterValue);
            var json = JsonSerializer.Serialize(skill, JsonSerializerOptions.Web);
            Assert.DoesNotContain(privateKey.ToString(), json);
            using var doc = JsonDocument.Parse(json);
            Assert.Equal(["directoryFilterValue", "name"], doc.RootElement.EnumerateObject().Select(p => p.Name).Order());
        }
        Assert.Empty(Assert.IsType<CompanySkillValue[]>(delivery.ConvertIntermediateToDeliveryApiObject(null!, property,
            PropertyCacheLevel.None, udis, true, true)));
        Assert.Empty(Assert.IsType<CompanySkillValue[]>(delivery.ConvertIntermediateToDeliveryApiObject(null!, Property("company", "companyTags"),
            PropertyCacheLevel.None, udis, false, true)));
    }

    private class TestTag(IPublishedContent content, IPublishedValueFallback fallback) : Tag(content, fallback)
    {
        public override string DisplayName => "Rust language";
    }
    public class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var value = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)value).Handler = handler;
        return value;
    }
}
