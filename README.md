# Springfield Devs Website

![](https://pbs.twimg.com/profile_banners/2869149607/1567717351/1500x500)

## Prerequisites
- [.NET SDK 10.x](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Node.js 18.x](https://nodejs.org/en/download/)

## Local Installation Instructions

There are a couple of ways to run this project depending on if you have a .NET IDE installed or just the CLI tools

### Environment Specific Steps

Local media uses SeaweedFS. With Docker Compose installed, run from the repo root:

```sh
docker compose up -d
```

The local bucket is created automatically at `http://localhost:8333`.
The development launch profiles supply local-only credentials.
Media persists across restarts; `docker compose down --volumes` deletes it.

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

### Umbraco In-browser Steps
- Once the site has been launched you should see an Umbraco screen to create a new account
- Fill this out and wait a few seconds for Umbraco to install
- Once you're redirected to the Admin, click on the Settings tab
- Navigation to "uSync" under "Synchronization" in the left panel
- Under the "Everything" card click the green "Import" button
- Once this is finished navigate to the site's root url and you should see a functioning site

## Public Content Delivery API

This project uses Umbraco CMS 18.2.0. The Delivery API exposes only published,
unprotected nodes with these document type aliases: `home`, `events`, `event`,
`companies`, `groups`, and `jobs`. These are public listing pages and event dates,
not the member directory or full event presentations. The existing Razor pages
continue to render unchanged.

The non-empty `Umbraco:CMS:DeliveryApi:AllowedContentTypeAliases` list excludes
every other type, including future document types. Do not empty it. Umbraco's
allowlist takes precedence over its denylist. Generic `page` nodes stay excluded
because that type also covers Member and Search Results pages. Company, group,
job, presentation, leadership, account, authentication, tag, and element types
stay excluded pending a property and reference review. The allowed types have no
member pickers, block lists, media pickers, content references, or compositions.

Media API access and both Delivery API member authorization flows are explicitly
disabled. Umbraco excludes protected content when member authorization is disabled.
The Delivery API key is empty, so preview requests cannot authorize access to
draft content. Do not supply `Umbraco__CMS__DeliveryApi__ApiKey` or enable member
authorization for this public-only API. Client-side `fields` filtering is not an
access control.

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

## Building CSS
- Navigate to the SgfDevs project folder `cd SgfDevs`
- `npm install`
- `npm run build` or to watch for changes `npm run css`
