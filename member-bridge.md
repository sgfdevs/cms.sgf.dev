# Current-member profile editing

Private GET and POST `/api/v1/member/profile` authenticate only the existing Identity.Application member cookie. The edit DTO rejects unknown fields. It never accepts member IDs, usernames, roles, member display tags or avatar inputs. Omitted fields stay unchanged.

The editable fields match the legacy profile form, including Markdown source, all eight website/social links, availability, skills and interest groups. Skills are published tag documents below Tags/skills; interest groups are published group documents below Home, not member security roles. Missing schema or published roots returns unavailable without creating anything.

Umbraco property editors validate supplied text using the deployed datatype configuration, mandatory and regex rules. TextBox storage is limited to 512 characters, full member name to 255 and email to 1000. Markdown has a 100000-character request limit, not a schema limit. Links allow only HTTP(S), with HTTPS normalization for bare websites. Unknown selection GUIDs fail before writes.

Umbraco Identity UpdateAsync validates and normalizes email/name. Profile properties save in the same CMS scope; failed/cancelled saves do not complete it. Existing SignInAsync renews the member cookie with its original persistence choice. Biography remains user-authored Markdown; public frontend rendering keeps its existing sanitizer. Current image is read-only.

# Member password reset

The frontend server posts to `/api/v1/member/forgot-password` and `/api/v1/member/reset-password` through the existing private bridge. Both routes have separate aggregate limits of 20 requests per minute, no queue, and no-store responses. Browser Origin requests remain rejected.

Configure `SGFDevs:MemberBridge:FrontendOrigin`, or environment variable `SGFDevs__MemberBridge__FrontendOrigin`, with the canonical frontend origin, for example `https://sgf.dev`. Paths, credentials, queries and fragments are rejected. HTTP is allowed only for `localhost`, `127.0.0.1` and `[::1]` in Development. Missing or invalid configuration makes forgotten-password unavailable without affecting login or public pages. Links always target `/reset-password`; incoming hosts and CMS content never select the destination.

Mail uses Umbraco `IEmailSender.CanSendRequiredEmail()`, `Umbraco:CMS:Global:Smtp:From` and `Umbraco:CMS:Global:Smtp:EmailExpiration`. Readiness is checked before account lookup. Existing and nonexistent accounts receive the same confirmation. Account-specific send failures keep that response and log only a fixed message. Email HTML encodes the name and link. Identity member IDs and reset tokens appear only in the emailed link and private reset request, never in response DTOs or logs.

Reset reuses `ResetPasswordModel` and its shared password rules. Umbraco generates and verifies the token and updates the password. No new token database or session is introduced. Identity descriptions are not returned. The frontend redirects to login after success without signing in. The deployed Umbraco token provider, transport, data-protection key persistence and existing member store must be configured independently.
