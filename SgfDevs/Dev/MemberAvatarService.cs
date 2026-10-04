#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Umbraco.Cms.Core.Models.Validation;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;

namespace SgfDevs.Dev;

public class MemberAvatarService(
    IMemberService members, IMediaService mediaService, MediaFileManager files,
    MediaUrlGeneratorCollection generators, IShortStringHelper strings,
    IContentTypeBaseServiceProvider contentTypes, ICoreScopeProvider scopes, IJsonSerializer json,
    IDataTypeService dataTypes, PropertyEditorCollection editors)
{
    public async Task<string?> SaveAsync(Guid memberKey, Stream content, string extension)
    {
        using var scope = scopes.CreateCoreScope();
        var member = members.GetById(memberKey);
        var property = member?.Properties.FirstOrDefault(x => x.Alias == "profileImage");
        var folder = mediaService.GetRootMedia().FirstOrDefault(x =>
            x.Name.InvariantEquals("Members") && x.ContentType.Alias == Constants.Conventions.MediaTypes.Folder && !x.Trashed);
        if (member?.ContentType.Alias != "Member" || property?.PropertyType.PropertyEditorAlias != "Umbraco.MediaPicker3" || folder is null)
            return null;
        var dataType = await dataTypes.GetAsync(property.PropertyType.DataTypeKey);
        if (dataType is null || !editors.TryGet("Umbraco.MediaPicker3", out var editor)) return null;
        var validator = editor.GetValueEditor(dataType.ConfigurationObject);

        // Never look up, reuse or delete the member's previous image or a caller-selected media item.
        var media = mediaService.CreateMedia("Avatar " + Guid.NewGuid().ToString("N"), folder.Id, Constants.Conventions.MediaTypes.Image);
        if (!media.Properties.Any(x => x.Alias == Constants.Conventions.Media.File && x.PropertyType.PropertyEditorAlias == "Umbraco.ImageCropper"))
            return null;
        WriteFile(media, Guid.NewGuid().ToString("N") + extension, content);
        if (!mediaService.Save(media).Success || !media.TryGetMediaPath(Constants.Conventions.Media.File, generators, out var path))
            return null;
        var url = PublicHomeBuilder.GetSafePathOrHttpUrl(files.FileSystem.GetUrl(path));
        if (url is null || url.Contains('?')) return null;
        // MediaPicker3 stores an array of key/mediaKey/crops/focalPoint, not legacy UDI strings.
        // Its installed MediaWithCropsDto is internal. Serialize the same shape with Umbraco.
        var value = json.Serialize(new[] { new { key = Guid.NewGuid(), mediaKey = media.Key, crops = Array.Empty<object>(), focalPoint = (object?)null } });
        if (validator.Validate(value, property.PropertyType.Mandatory, property.PropertyType.ValidationRegExp,
            PropertyValidationContext.Empty()).Any()) return null;
        member.SetValue("profileImage", value);
        if (!members.Save(member).Success || members.GetById(memberKey)?.GetValue<string>("profileImage") != value)
            return null;
        scope.Complete();
        return url + "?width=200";
    }

    protected virtual void WriteFile(IMedia media, string filename, Stream content) => media.SetValue(
        files, generators, strings, contentTypes, Constants.Conventions.Media.File, filename, content);
}
