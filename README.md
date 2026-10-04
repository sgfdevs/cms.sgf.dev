# Springfield Devs CMS

![](https://pbs.twimg.com/profile_banners/2869149607/1567717351/1500x500)

## Prerequisites
- [.NET SDK 10.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Node.js 18.x](https://nodejs.org/en/download/)

## Local installation

There are a couple of ways to run this project depending on if you have a .NET IDE installed or just the CLI tools

### Local media

Local media uses SeaweedFS. With Docker Compose installed, run from the repo root:

```sh
docker compose up -d seaweedfs
```

The local bucket is created automatically at `http://localhost:8333`.
The development launch profiles supply local-only credentials.
Media persists across restarts; `docker compose down --volumes` deletes it.

### Optional local email capture

[Mailpit](https://mailpit.axllent.org/docs/install/docker/) uses the official
`axllent/mailpit:v1.31.4` stable image, behind the optional `mailpit` Compose profile.
SMTP and the inbox UI bind only to `127.0.0.1:1025` and `127.0.0.1:8025`.
No relay, authentication, or persistent mail volume is configured. Use fictional
local addresses only. Captured messages can contain private reset tokens.

From the repo root, confirm that `desktop-linux` points to your local Unix socket,
not a remote Docker host. This workstation uses
`unix:///home/levi/.docker/desktop/docker.sock`. Stop if the context is not local.
Then start only Mailpit, without starting or recreating SeaweedFS:

```sh
docker context inspect desktop-linux --format '{{.Endpoints.docker.Host}}'
docker --context desktop-linux compose --profile mailpit up -d --no-deps mailpit
```

For the CMS running on the host in ordinary local Development, supply native
Umbraco 18.2 SMTP settings in its launch shell. These are session-only overrides,
not changes to tracked appsettings or shared credentials:

```sh
export ASPNETCORE_ENVIRONMENT=Development
export DOTNET_ENVIRONMENT=Development
export Umbraco__CMS__Global__Smtp__Host=127.0.0.1
export Umbraco__CMS__Global__Smtp__Port=1025
export Umbraco__CMS__Global__Smtp__From=noreply@sgf.dev.invalid
export Umbraco__CMS__Global__Smtp__DeliveryMethod=Network
export Umbraco__CMS__Global__Smtp__SecureSocketOptions=None
export Umbraco__CMS__Global__Smtp__Username=
export Umbraco__CMS__Global__Smtp__Password=
export SGFDevs__MemberBridge__FrontendOrigin=http://127.0.0.1:3000
```

Replace port `3000` with your local frontend port. The existing
`SGFDevs:MemberBridge:FrontendOrigin` selects the `/reset-password` link origin,
not the CMS host. Launch the CMS using your existing ordinary Development setup;
open the inbox at `http://127.0.0.1:8025`. `SecureSocketOptions=None` means no TLS.
Do not use these overrides with `scripts/bootstrap-local-cms.py` or sealed
install-only profiles. Sealed bootstrap forbids SMTP; its guards stay unchanged.
This capture setup is local-only, not a deployment or Production configuration.

The bounded native auth/reset proof used cached Mailpit v1.29.4, not the tracked
v1.31.4 image. It verified native SMTP delivery and a real password reset. Runtime
compatibility with v1.31.4 was not established by that proof.

### Disposable local bootstrap

For a fresh local install that does not use user secrets or the old launch profile, run this from the repo root:

```sh
python3 scripts/bootstrap-local-cms.py
```

The script defaults the CMS to `http://127.0.0.1:5099` when no environment is set. If `ASPNETCORE_ENVIRONMENT` or `DOTNET_ENVIRONMENT` is set to anything other than `Development`, it refuses to run. It starts only the `seaweedfs` compose service with `docker compose up -d seaweedfs`, then runs the CMS in the foreground with `dotnet run --no-launch-profile`. Stop the CMS with Ctrl+C.

The script writes a private gitignored file at `SgfDevs/umbraco/Data/local-bootstrap/local-bootstrap.appsettings.json`. That file contains the generated local Umbraco admin password and local imaging HMAC secret. The script prints the path, not the values. File permissions are locked to the current user. Do not copy this file into commits, images, chat logs, or shared secret stores.

The disposable database path is `SgfDevs/umbraco/Data/local-bootstrap/local-bootstrap.sqlite`. The script never deletes or wipes databases. If that database already exists without the private bootstrap config that proves script ownership, it fails closed. Reruns reuse the same private config and database only after checking that the config still targets the loopback SeaweedFS endpoint and the local SQLite database.

This install layer does not import schema or content. Its guarded local uSync settings keep `ImportOnFirstBoot=false`, `ImportAtStartup=None`, `ExportAtStartup=None`, and `ExportOnSave=None`. The tracked real `uSync/v18/Content` and `uSync/v18/Media` folders are not imported by this command. Imports remain paused. Do not activate schema import or import real content/media. Fictional SGF seed data and the required legacy properties are still missing from the builtin-only runtime. Do not expect visual site parity from this bootstrap alone.

The split frontend already uses `CMS_INTERNAL_ORIGIN`. Use an ordinary Development runtime for frontend integration, not the sealed install-only profile. See the server-only settings below and the frontend README for the separate Kit commands. This bootstrap does not enable production OpenAPI schemas or fetch production data.

- Create an `Umbraco.sqlite.db` file in the `./SgfDevs/umbraco/Data` directory 
  - Mac OS/Linux `mkdir -p ./SgfDevs/umbraco/Data && touch ./SgfDevs/umbraco/Data/Umbraco.sqlite.db`
  - Windows `New-Item -ItemType Directory -Force -Path .\SgfDevs\umbraco\Data; New-Item -ItemType File -Force -Path .\SgfDevs\umbraco\Data\Umbraco.sqlite.db`

#### CLI Tools
- Navigate to the SgfDevs project folder `cd SgfDevs`
- Use the `dotnet user-secrets` command to set your connection string
  - `dotnet user-secrets set "ConnectionStrings:umbracoDbDSN" "Data Source=|DataDirectory|/Umbraco.sqlite.db;Cache=Shared;Foreign Keys=True;Pooling=True;Default Timeout=60"`
- `dotnet run --launch-profile Umbraco.Web.UI`
- Open the URL that's printed in the console in your browser

#### .NET IDE e.g. Rider or Visual Studio (Windows)
- Update your .NET User Secrets with a connection string
  - Most IDEs have a shortcut to navigate to this file
  - Reference `appsettings.json` for an example of the `ConnectionStrings` object
    - Set `umbracoDbDSN` to something like `Data Source=|DataDirectory|/Umbraco.sqlite.db;Cache=Shared;Foreign Keys=True;Pooling=True;Default Timeout=60`
  - You can also fall back to the CLI tools instructions
- Your IDE will likely have some kind of run option, run this and it should launch your browser

### Historical installation limits

The old backoffice "Everything" import instructions are retired. Imports remain
paused. A fresh native Umbraco install is not a functioning SGF site and does not
supply SGF member properties, document types or content. Do not import or activate
schema, content or media based on this README.

## Split frontend and native member bridge

The SvelteKit frontend in `sgf.dev` already has SSR/API and membership integration.
Use two ordinary local Development processes. In the CMS shell, supply the
existing private settings for an owned installation and these matching values:

- `SGFDevs__MemberBridge__Secret` is a private shared secret. It must match
  frontend `CMS_MEMBER_BRIDGE_SECRET`. Missing or mismatched secrets fail closed.
- `SGFDevs__MemberBridge__FrontendOrigin=http://127.0.0.1:3000` selects the Kit
  origin for reset links. It is not a CORS allowlist.
- `Umbraco__CMS__DeliveryApi__ApiKey` is a private native Delivery key. Supply the
  same value to frontend `CMS_DELIVERY_API_KEY`; content-page loads require it
  even while native `PublicAccess=true`. Do not put it in browser code or Git.

For that existing installation, the ordinary CMS command is:

```sh
ASPNETCORE_ENVIRONMENT=Development DOTNET_ENVIRONMENT=Development \
  dotnet run --project SgfDevs --no-launch-profile --urls http://127.0.0.1:5099
```

Run Kit separately using the frontend README. Set frontend `CMS_INTERNAL_ORIGIN`
to `http://127.0.0.1:5099`, plus its matching private keys. Vite loads ignored `.env`
files; the built Node frontend needs process-environment injection. Set
`BODY_SIZE_LIMIT=9M` on that Node server for the existing avatar upload path.
These settings do not change sealed-bootstrap guards or tracked security defaults.

`/api/v1/member/*` is intentionally server-only. Kit relays the native member
cookie and sends `X-SGF-Member-Bridge` from its server. The bridge rejects requests
with an `Origin` header. Native CORS middleware handles `[DisableCors]` endpoint
metadata; it does not authorize browser CMS access. Do not add browser CMS calls,
a permissive CORS policy or a frontend-origin allowlist as a workaround.

A bounded native-CMS journey passed real login, session, logout, SMTP delivery and
password reset. It used a synthetic member created through the native member
manager in a builtin-member-only database. That runtime lacks the legacy SGF
properties needed for registration, profile editing and avatars. Those operations
are not working end-to-end there; a successful reset does not prove them. Imports
remain paused, and no schema, database or media activation is part of this layer.

## Public content Delivery API

This project uses Umbraco CMS 18.2.0. The Delivery API exposes only published,
unprotected nodes with these document type aliases: `home`, `events`, `event`,
`companies`, `company`, `groups`, `jobs`, and `page`. The existing Razor pages
continue to render unchanged. Company details use the native Delivery item endpoint.

The non-empty `Umbraco:CMS:DeliveryApi:AllowedContentTypeAliases` list excludes
every other type, including future document types. Do not empty it. Umbraco's
allowlist takes precedence over its denylist. Group, job, presentation, leadership,
account, authentication, tag, and element types stay excluded. The company-only
Delivery converter suppresses unused `companyTags` and maps `skillTags` to public
names and published tag GUID filter values. It rejects protected tag paths,
preview values and non-document pickers, even when expansion is requested.
No raw picked node properties, routes or member identities leave that converter.
Page blocks and meta remain allowed as in the content-pages layer.

Media API access and both Delivery API member authorization flows are explicitly
disabled. Umbraco excludes protected content when member authorization is disabled.
The tracked Delivery API key is empty, so the default configuration cannot
authorize draft previews. For frontend integration, privately configure the native
`Umbraco__CMS__DeliveryApi__ApiKey` and matching frontend `CMS_DELIVERY_API_KEY`
as described above. Public access stays enabled; this key is still required by
the frontend's server clients. A native key can authorize draft previews, so keep
it server-only and do not expose preview access to browsers. Do not enable member
authorization or change the allowlist for this setup. Client-side `fields`
filtering is not an access control.

OpenAPI uses Umbraco 18's built-in document and UI routes:

- Delivery API JSON: `/umbraco/openapi/delivery.json`
- SGF public API JSON: `/umbraco/openapi/sgf-public-v1.json`
- Swagger UI: `/umbraco/openapi`
- Content endpoints: `/umbraco/delivery/api/v2/content` and
  `/umbraco/delivery/api/v2/content/item/{id-or-path}`

The SGF public API document is filtered to the typed public directory endpoints
and excludes newsletter, account, profile image, redirects, and raw Delivery API
schemas. OpenAPI remains unavailable in Production by Umbraco's default. In local
Development, use the origin printed by `dotnet run` with the paths above.
Content-type schema generation stays disabled to avoid publishing private model
properties or unrelated media schemas. No production OpenAPI override is added.

After enabling the API or changing its allowlist, an operator must rebuild
`DeliveryApiContentIndex` in Examine Management before relying on collection
queries. This change does not rebuild an index, install a database, or import
content. Review the actual database's properties and access protection before
enabling these settings in a running environment. Repository schema checks do
not establish what a live database contains.

Official Umbraco 18 references:

- [Content Delivery API](https://docs.umbraco.com/umbraco-cms/18.latest/develop-with-umbraco/headless-and-apis/content-delivery-api)
- [Protected content](https://docs.umbraco.com/umbraco-cms/18.latest/develop-with-umbraco/headless-and-apis/content-delivery-api/protected-content-in-the-delivery-api)
- [Content-type OpenAPI schemas](https://docs.umbraco.com/umbraco-cms/18.latest/develop-with-umbraco/headless-and-apis/content-delivery-api/content-type-schemas-in-openapi)
- [OpenAPI routes and availability](https://docs.umbraco.com/umbraco-cms/18.latest/extend-your-project/server-side-extensions/api-versioning-and-openapi)

## uSync Publisher credentials

Incoming Publisher requests are disabled by default. Tracked configuration must
not contain Publisher credentials. If incoming publishing is needed, supply these
environment variables through external secret storage:

- `uSync__Publisher__Settings__AppId`
- `uSync__Publisher__Settings__AppKey`
- `uSync__Publisher__Settings__IncomingEnabled`, enabled only after access review

Previously committed credentials must be treated as compromised. An operator
must rotate them externally and update any authorized publishing peers. Removing
values from the current file does not remove them from Git history or revoke
credentials in running environments.

## Building legacy CSS
- Navigate to the SgfDevs project folder `cd SgfDevs`
- `npm install`
- `npm run build` or to watch for changes `npm run css`
