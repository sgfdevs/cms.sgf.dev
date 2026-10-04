# Member password reset

The frontend server posts to `/api/v1/member/forgot-password` and `/api/v1/member/reset-password` through the existing private bridge. Both routes have separate aggregate limits of 20 requests per minute, no queue, and no-store responses. Browser Origin requests remain rejected.

Configure `SGFDevs:MemberBridge:FrontendOrigin`, or environment variable `SGFDevs__MemberBridge__FrontendOrigin`, with the canonical frontend origin, for example `https://sgf.dev`. Paths, credentials, queries and fragments are rejected. HTTP is allowed only for `localhost`, `127.0.0.1` and `[::1]` in Development. Missing or invalid configuration makes forgotten-password unavailable without affecting login or public pages. Links always target `/reset-password`; incoming hosts and CMS content never select the destination.

Mail uses Umbraco `IEmailSender.CanSendRequiredEmail()`, `Umbraco:CMS:Global:Smtp:From` and `Umbraco:CMS:Global:Smtp:EmailExpiration`. Readiness is checked before account lookup. Existing and nonexistent accounts receive the same confirmation. Account-specific send failures keep that response and log only a fixed message. Email HTML encodes the name and link. Identity member IDs and reset tokens appear only in the emailed link and private reset request, never in response DTOs or logs.

Reset reuses `ResetPasswordModel` and its shared password rules. Umbraco generates and verifies the token and updates the password. No new token database or session is introduced. Identity descriptions are not returned. The frontend redirects to login after success without signing in. The deployed Umbraco token provider, transport, data-protection key persistence and existing member store must be configured independently.
