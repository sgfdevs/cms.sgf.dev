# Public leadership page

GET `/api/v1/public/leadership` resolves the published `/about/leadership` route as the existing Leadership model. Missing, protected or preview content returns 404.

The projection reads only the model's officers, boardOfDirectors and history selections, in picker order. It uses MemberConverter and the existing public profile lookup for member publication, normalized usernames and anonymous member-anchor access. Non-member and protected selections are omitted. Names and uncropped images match the legacy view. Officer titles are included for officers and history; biographies only for officers. History has no year field in the native model or legacy view.

The leadership alias remains excluded from Delivery. No picker values, member IDs, keys, email, security fields or general member properties enter this response. The frontend must sanitize public biographies and map images through its approved media helper.
