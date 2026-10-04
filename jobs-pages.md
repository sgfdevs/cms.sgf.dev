# Jobs pages

GET `/api/v1/public/jobs` follows the published `/jobs` page's `Root().Descendants<Job>()` sequence, matching the legacy table. No sort, expiry filter, pagination or result limit is added. The parent company's name and native published job URL are projected with location, employment type, compensation and the invariant `MMMM d, yyyy` creation date.

GET `/api/v1/public/jobs/{company}/{job}` resolves both actual published routes through `IDocumentUrlService`, including custom URL names. The resolved job must have the resolved Company as its direct parent. Preview is disabled, and anonymous protection checks include content ancestor paths. Missing, mismatched or protected content returns 404.

Native Delivery cannot supply this ordered descendant list with its parent-company shape. This service uses native navigation, published filtering, URL and model value services instead. Existing raw job/tag exclusions, company converters, protected content rules and Delivery API-key policy remain unchanged. Only detail includes description HTML, a safe apply URL and visible skill labels. No member, picker ID, unused jobTags or banner values are returned. The frontend sanitizes description HTML and apply links before rendering.
