#nullable enable
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SGFDevs.Controllers;
using SGFDevs.ViewModels;
using SgfDevs.Dev;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.IO;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PropertyEditors;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Extensions;
using Xunit;

namespace SgfDevs.Tests;

public class MemberAvatarTests
{
    [Fact]
    public async Task CurrentMemberOnlyAndNoCallerSelections()
    {
        var f = new Fixture { Authenticated = false };
        Assert.IsType<UnauthorizedResult>((await f.Upload(await ImageBytes())).Result);
        f.Authenticated = true; f.Current = false;
        Assert.IsType<UnauthorizedResult>((await f.Upload(await ImageBytes())).Result);
        f.Current = true;
        Assert.IsType<BadRequestResult>((await f.Upload(await ImageBytes(), extra: true)).Result);
        Assert.Empty(f.Writes);
        Assert.Equal("old avatar", f.Member.GetValue<string>("profileImage"));
    }

    [Fact]
    public async Task RejectsMalformedOversizedAndNonImagesWithoutWrites()
    {
        var f = new Fixture();
        foreach (var bytes in new[] { Array.Empty<byte>(), Encoding.UTF8.GetBytes("<svg/>"), new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } })
            Assert.IsType<BadRequestResult>((await f.Upload(bytes)).Result);
        Assert.Equal(413, Assert.IsType<StatusCodeResult>((await f.Upload(new byte[8 * 1024 * 1024 + 1])).Result).StatusCode);
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData(8193, 1, false)]
    [InlineData(4001, 4000, false)]
    [InlineData(20, 20, true)]
    public async Task RejectsDimensionsPixelsAndAnimatedPngBeforeWriting(int width, int height, bool animate)
    {
        var f = new Fixture();
        Assert.IsType<BadRequestResult>((await f.Upload(await ImageBytes(width, height, animate: animate))).Result);
        Assert.Empty(f.Writes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReencodesSquareWithoutMetadataAndAssignsOnlyNewImage(bool jpeg)
    {
        var f = new Fixture();
        var result = Assert.IsType<MemberAvatarResult>(Assert.IsType<OkObjectResult>((await f.Upload(await ImageBytes(800, 600, jpeg))).Result).Value);
        Assert.Equal(new[] { "file", "media", "member", "complete" }, f.Writes);
        Assert.Equal("/media/synthetic/" + f.Filename + "?width=200", result.ProfileImageUrl);
        Assert.EndsWith(jpeg ? ".jpg" : ".png", f.Filename);
        Assert.DoesNotContain("caller", f.Filename);
        using var image = Image.Load(f.Stored!);
        Assert.Equal(512, image.Width); Assert.Equal(512, image.Height); Assert.Single(image.Frames);
        Assert.Null(image.Metadata.ExifProfile);
        Assert.Null(image.Metadata.IccProfile);
        var selection = JsonDocument.Parse(f.Member.GetValue<string>("profileImage")!).RootElement;
        Assert.Equal(f.NewMedia.Key, selection[0].GetProperty("mediaKey").GetGuid());
        Assert.Single(selection.EnumerateArray());
        Assert.Equal("unchanged", f.Member.GetValue<string>("aboutText"));
    }

    [Fact]
    public async Task MissingFolderSchemaOrStorageAndCancelledSaveFailSafely()
    {
        foreach (var mode in new[] { "folder", "schema", "storage", "cancel" })
        {
            var f = new Fixture { Failure = mode };
            Assert.Equal(503, Assert.IsType<StatusCodeResult>((await f.Upload(await ImageBytes())).Result).StatusCode);
            Assert.DoesNotContain("complete", f.Writes);
            if (mode != "cancel") Assert.Equal("old avatar", f.Member.GetValue<string>("profileImage"));
        }
    }

    private static async Task<byte[]> ImageBytes(int width = 32, int height = 16, bool jpeg = false, bool animate = false)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(40, 80, 120));
        image.Metadata.ExifProfile = new ExifProfile();
        image.Metadata.ExifProfile.SetValue(ExifTag.Artist, "strip this metadata");
        if (animate) image.Frames.AddFrame(image.Frames.RootFrame);
        using var stream = new MemoryStream();
        if (jpeg) await image.SaveAsJpegAsync(stream); else await image.SaveAsPngAsync(stream);
        return stream.ToArray();
    }

    private sealed class Fixture
    {
        public bool Authenticated = true, Current = true;
        public string Failure = "", Filename = "";
        public byte[]? Stored;
        public Member Member;
        public Media NewMedia;
        public List<string> Writes = [];
        private readonly MemberAvatarController controller;
        public Fixture()
        {
            var helper = new DefaultShortStringHelper(new DefaultShortStringHelperConfig());
            var type = new MemberType(helper, -1) { Alias = "Member" };
            type.AddPropertyType(new PropertyType(helper, "Umbraco.MediaPicker3", ValueStorageType.Ntext, "profileImage"));
            type.AddPropertyType(new PropertyType(helper, "Umbraco.TextBox", ValueStorageType.Nvarchar, "aboutText"));
            Member = new Member("Synthetic", "synthetic@example.test", "Synthetic", type);
            Member.SetValue("profileImage", "old avatar"); Member.SetValue("aboutText", "unchanged");
            var folderType = new MediaType(helper, -1) { Alias = "Folder" };
            var folder = new Media("Members", -1, folderType) { Id = 321 };
            var imageType = new MediaType(helper, -1) { Alias = "Image" };
            imageType.AddPropertyType(new PropertyType(helper, "Umbraco.ImageCropper", ValueStorageType.Ntext, "umbracoFile"));
            NewMedia = new Media("new", folder.Id, imageType);
            var media = Proxy<IMediaService>((method, args) => method.Name switch
            {
                "GetRootMedia" => Failure == "folder" ? Array.Empty<IMedia>() : new IMedia[] { folder },
                "CreateMedia" => Create(args!),
                "Save" => SaveMedia(args!),
                _ => throw new Exception("Unexpected media operation " + method.Name)
            });
            var members = Proxy<IMemberService>((method, args) => method.Name switch
            {
                "GetById" => Read(args!),
                "Save" => SaveMember(args!),
                _ => throw new Exception(method.Name)
            });
            var scope = Proxy<ICoreScope>((method, _) =>
            {
                if (method.Name == "Complete") { Writes.Add("complete"); return true; }
                if (method.Name == "Dispose") return null;
                throw new Exception(method.Name);
            });
            var scopes = Proxy<ICoreScopeProvider>((_, _) => scope);
            var fs = Proxy<IFileSystem>((method, args) => method.Name == "GetUrl" ? "/media/" + args![0] : throw new Exception(method.Name));
            var files = new MediaFileManager(fs, null!, NullLogger<MediaFileManager>.Instance, helper, new ServiceCollection().BuildServiceProvider(), new Lazy<ICoreScopeProvider>(() => scopes));
            var generator = Proxy<IMediaUrlGenerator>((method, args) =>
            {
                Assert.Equal("TryGetMediaPath", method.Name); args![2] = "synthetic/" + Filename; return true;
            });
            var generators = new MediaUrlGeneratorCollection(() => new[] { generator });
            var json = Proxy<IJsonSerializer>((_, args) => JsonSerializer.Serialize(args![0]));
            var dataType = Proxy<IDataType>((_, _) => null);
            var dataTypes = Proxy<IDataTypeService>((_, _) => Task.FromResult<IDataType?>(Failure == "schema" ? null : dataType));
            var valueEditor = Proxy<IDataValueEditor>((_, _) => Array.Empty<ValidationResult>());
            var editors = new PropertyEditorCollection(new DataEditorCollection(() => new[] {
                Proxy<IDataEditor>((method, _) => method.Name == "get_Alias" ? "Umbraco.MediaPicker3" : valueEditor) }));
            var service = new FakeFiles(this, members, media, files, generators, helper, scopes, json, dataTypes, editors);
            var identity = new MemberIdentityUser { Key = Member.Key, UserName = "Synthetic" };
            var manager = Proxy<IMemberManager>((_, _) => Task.FromResult(Current ? identity : null));
            var auth = Proxy<IAuthenticationService>((_, args) =>
            {
                Assert.Equal(IdentityConstants.ApplicationScheme, args![1]);
                return Task.FromResult(Authenticated ? AuthenticateResult.Success(new AuthenticationTicket(
                    new ClaimsPrincipal(new ClaimsIdentity(IdentityConstants.ApplicationScheme)), IdentityConstants.ApplicationScheme)) : AuthenticateResult.NoResult());
            });
            controller = new(manager, service, NullLogger<MemberAvatarController>.Instance)
            { ControllerContext = new() { HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(auth).BuildServiceProvider() } } };
        }
        private IMedia Create(object?[] args)
        {
            Assert.Equal(321, args[1]); Assert.Equal("Image", args[2]); return NewMedia;
        }
        private IMember Read(object?[] args) { Assert.Equal(Member.Key, args[0]); return Member; }
        private Attempt<OperationResult?> SaveMedia(object?[] args)
        { Assert.Same(NewMedia, args[0]); Writes.Add("media"); return Attempt.Succeed<OperationResult?>(null); }
        private Attempt<OperationResult?> SaveMember(object?[] args)
        { Assert.Same(Member, args[0]); Writes.Add("member"); return Failure == "cancel" ? Attempt.Fail<OperationResult?>(null) : Attempt.Succeed<OperationResult?>(null); }
        public Task<ActionResult<MemberAvatarResult>> Upload(byte[] bytes, bool extra = false)
        {
            var file = new FormFile(new MemoryStream(bytes), 0, bytes.Length, "file", "caller.svg");
            controller.Request.Form = new FormCollection(extra ? new() { ["memberId"] = "foreign" } : new(), new FormFileCollection { file });
            return controller.Upload(file);
        }
        private sealed class FakeFiles(Fixture fixture, IMemberService members, IMediaService media, MediaFileManager files,
            MediaUrlGeneratorCollection generators, IShortStringHelper strings, ICoreScopeProvider scopes, IJsonSerializer json,
            IDataTypeService dataTypes, PropertyEditorCollection editors)
            : MemberAvatarService(members, media, files, generators, strings, null!, scopes, json, dataTypes, editors)
        {
            protected override void WriteFile(IMedia media, string filename, Stream content)
            {
                if (fixture.Failure == "storage") throw new IOException("synthetic storage failure");
                fixture.Filename = filename;
                using var memory = new MemoryStream(); content.CopyTo(memory); fixture.Stored = memory.ToArray();
                fixture.Writes.Add("file"); media.SetValue("umbracoFile", "synthetic/" + filename);
            }
        }
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, MemberLoginTests.InterfaceProxy>();
        ((MemberLoginTests.InterfaceProxy)(object)proxy).Handler = handler; return proxy;
    }
}
