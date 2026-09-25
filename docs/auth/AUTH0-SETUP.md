# Auth0 setup guide

This guide connects Kinsmen to [Auth0](https://auth0.com) for customer registration, login, logout and password handling. Auth0's free plan covers this project, and the booking API already supports it: no API code changes are needed.

**Owner:** Kyra. **Estimated time:** about 1 hour for the dashboard (Parts 1–3), then the web app changes (Part 4).

## How it fits together

```
Browser ──login──> Auth0 Universal Login (sign-up, password reset, email verification)
   │                    │
   │  session cookie    │ ID token + access token (audience "kinsmen-api", claims sub + role)
   ▼                    ▼
Kinsmen.Web ───────────────── Bearer access token ──────────────> Kinsmen.Api
(stores tokens server-side)                                   (validates against Auth0)
```

The API checks three things in every token:

| Token field | Required value | Where it comes from |
|---|---|---|
| Audience (`aud`) | `kinsmen-api` | The Auth0 API created in Part 1 |
| `sub` | The user's Auth0 ID, e.g. `auth0\|65f0c…` | Set by Auth0 automatically |
| `role` | `Customer`, `Barber` or `Admin` | The Action created in Part 3 |

Passwords never reach our database, and the API trusts only tokens signed by our Auth0 tenant.

---

## Part 1: Tenant and API (dashboard)

1. Sign up at [auth0.com](https://auth0.com) (free plan). When asked for a region, choose **EU**.
2. Note the tenant domain shown at the top left, e.g. `kinsmen.eu.auth0.com`. This is the **Domain** used below.
3. Go to **Applications → APIs → Create API**:
   - **Name:** `Kinsmen API`
   - **Identifier:** `kinsmen-api` (exactly this; it becomes the token audience and cannot be changed later)
   - **Signing algorithm:** RS256
4. In the new API's **Settings**, turn on **Allow Offline Access** and save. This lets the web app refresh tokens instead of logging users out when the access token expires.

## Part 2: Web application (dashboard)

1. Go to **Applications → Applications → Create Application**, name it `Kinsmen Web`, and choose **Regular Web Applications**.
2. In its **Settings** tab, fill in (comma-separated, no spaces around URLs):
   - **Allowed Callback URLs:**
     `https://localhost:7090/callback, https://kinsmen-web.onrender.com/callback`
   - **Allowed Logout URLs:**
     `https://localhost:7090/, https://kinsmen-web.onrender.com/`
   - Replace `kinsmen-web.onrender.com` with the real Render URL once the service exists.
3. Save, then note the **Client ID** and **Client Secret**. The Client Secret is a password: never commit it or paste it into chat.
4. Under **Authentication → Database → Username-Password-Authentication**, check that sign-ups are enabled. Optionally, under **Authentication → Social**, keep Google login switched on.
5. Under **Branding → Universal Login**, optionally set the logo and colours to match the site.

## Part 3: Roles (dashboard)

Each user's role is stored in their Auth0 profile (`app_metadata.role`) and copied into their tokens at login by an Action. This avoids Auth0's paid role-management features.

1. Go to **Actions → Library → Create Action → Build from scratch**:
   - **Name:** `Add Kinsmen role`
   - **Trigger:** Login / Post Login
2. Replace the code with:

   ```js
   exports.onExecutePostLogin = async (event, api) => {
     const allowed = ['Customer', 'Barber', 'Admin'];
     let role = event.user.app_metadata?.role;

     // New sign-ups are customers. Staff roles are only ever set by an admin in the dashboard.
     if (!allowed.includes(role)) {
       role = 'Customer';
       api.user.setAppMetadata('role', role);
     }

     api.accessToken.setCustomClaim('role', role);
     api.idToken.setCustomClaim('role', role);
   };
   ```

3. Click **Deploy**.
4. Go to **Actions → Triggers → post-login**, drag `Add Kinsmen role` between **Start** and **Complete**, and click **Apply**.

**Making someone a barber or admin:** go to **User Management → Users**, open the user, and under **app_metadata** enter `{ "role": "Barber" }` (or `"Admin"`), then save. The change takes effect at their next login.

**Linking a barber account to a barber profile:** the API treats a user as a given barber when the barber document's `userId` equals the user's Auth0 `user_id` (shown on their Users page, e.g. `auth0|65f0c…`). Until the admin screens exist, set it in Atlas: **Browse Collections → kinsmen_dev → barbers**, edit the barber's `userId` field. Each staff account can be linked to only one barber.

## Part 4: Web app changes (code)

Work on a feature branch (e.g. `feature/kyra-auth0-login`) and open a pull request into `integration-part2`.

### 4.1 Package

```powershell
dotnet add src/Kinsmen.Web package Auth0.AspNetCore.Authentication
dotnet restore src/Kinsmen.Web --use-lock-file
```

Commit the updated `packages.lock.json`; CI restores in locked mode and fails without it.

### 4.2 Configuration

Add non-secret values to `src/Kinsmen.Web/appsettings.json`:

```json
"Auth0": {
  "Domain": "kinsmen.eu.auth0.com",
  "ClientId": "<Client ID from Part 2>",
  "Audience": "kinsmen-api"
}
```

Store the secret locally with user secrets (kept outside the repository):

```powershell
dotnet user-secrets init --project src/Kinsmen.Web
dotnet user-secrets set "Auth0:ClientSecret" "<Client Secret from Part 2>" --project src/Kinsmen.Web
```

Add an HTTPS profile to `src/Kinsmen.Web/Properties/launchSettings.json` and use it for login testing. Browsers reject the login cookies over plain HTTP, which shows up as a "Correlation failed" error:

```json
"https": {
  "commandName": "Project",
  "launchBrowser": true,
  "applicationUrl": "https://localhost:7090",
  "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
}
```

Run `dotnet dev-certs https --trust` once, then `dotnet run --project src/Kinsmen.Web --launch-profile https`.

### 4.3 `Program.cs`

Register Auth0 and replace the development token provider:

```csharp
using Auth0.AspNetCore.Authentication;

builder.Services
    .AddAuth0WebAppAuthentication(options =>
    {
        options.Domain = builder.Configuration["Auth0:Domain"]!;
        options.ClientId = builder.Configuration["Auth0:ClientId"]!;
        options.ClientSecret = builder.Configuration["Auth0:ClientSecret"];
    })
    .WithAccessToken(options =>
    {
        options.Audience = builder.Configuration["Auth0:Audience"];
        options.UseRefreshTokens = true;
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITokenProvider, Auth0TokenProvider>(); // replaces DevelopmentTokenProvider
```

In the middleware section, add `app.UseAuthentication();` directly **before** `app.UseAuthorization();`.

### 4.4 Token provider

Create `src/Kinsmen.Web/Auth/Auth0TokenProvider.cs`. This is the seam Greg left in `ITokenProvider`; `KinsmenApiClient` and the controllers need no changes.

```csharp
using Microsoft.AspNetCore.Authentication;

namespace Kinsmen.Web.Auth;

/// <summary>Sends the signed-in user's Auth0 access token to the API. Anonymous visitors get
/// null, so public endpoints (services, barbers, availability) keep working.</summary>
public sealed class Auth0TokenProvider(IHttpContextAccessor accessor) : ITokenProvider
{
    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var context = accessor.HttpContext;
        if (context?.User.Identity?.IsAuthenticated != true) return null;
        return await context.GetTokenAsync("access_token");
    }
}
```

Once this works, delete `DevelopmentTokenProvider.cs`, `DevTokenOptions.cs`, its `Configure<DevTokenOptions>` line and the `DevTokens` section of `appsettings.Development.json`, so no development tokens remain in the repository.

### 4.5 Account controller

Create `src/Kinsmen.Web/Controllers/AccountController.cs`:

```csharp
using Auth0.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Controllers;

public class AccountController : Controller
{
    public async Task Login(string returnUrl = "/")
    {
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(SafeReturnUrl(returnUrl))
            .Build();
        await HttpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, properties);
    }

    public async Task Register(string returnUrl = "/")
    {
        var properties = new LoginAuthenticationPropertiesBuilder()
            .WithRedirectUri(SafeReturnUrl(returnUrl))
            .WithParameter("screen_hint", "signup")
            .Build();
        await HttpContext.ChallengeAsync(Auth0Constants.AuthenticationScheme, properties);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task Logout()
    {
        var properties = new LogoutAuthenticationPropertiesBuilder()
            .WithRedirectUri(Url.Action("Index", "Home")!)
            .Build();
        await HttpContext.SignOutAsync(Auth0Constants.AuthenticationScheme, properties);
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    // Only allow redirects back into this site, never to another domain.
    private string SafeReturnUrl(string returnUrl) => Url.IsLocalUrl(returnUrl) ? returnUrl : "/";
}
```

Logout is a POST with an antiforgery token so another site cannot log users out with a link.

### 4.6 Navigation

In `Views/Shared/_Layout.cshtml`, replace the placeholder **Log In** / **Create Account** links (in both the desktop `nav-auth` block and the mobile menu):

```cshtml
@if (User.Identity?.IsAuthenticated == true)
{
    <span class="nav-user">@User.Identity.Name</span>
    <form asp-controller="Account" asp-action="Logout" method="post" class="inline-form">
        <button type="submit" class="link-btn">Log Out</button>
    </form>
}
else
{
    <a asp-controller="Account" asp-action="Login" class="link-btn">Log In</a>
    <a asp-controller="Account" asp-action="Register" class="btn-account neo-pill-raised">Create Account</a>
}
```

Add `[Authorize]` to `MyBookingsController` (and to the booking create action) so visitors are sent to login first. To show staff-only links, check `User.FindFirst("role")?.Value == "Barber"`.

## Part 5: API configuration

The API reads two settings; no code changes are needed.

| Setting | Value |
|---|---|
| `Auth__Authority` | `https://kinsmen.eu.auth0.com/` (your Domain, with `https://` and a trailing `/`) |
| `Auth__Audience` | `kinsmen-api` |

Locally, set them in the terminal that runs the API (`$env:Auth__Authority = "https://kinsmen.eu.auth0.com/"`). When `Auth:Authority` is set, the API uses Auth0 and ignores the development signing key, so development tokens stop working in that terminal.

## Part 6: Render

When the Blueprint is created (see [`docs/deployment/RENDER.md`](../deployment/RENDER.md)), Render asks for:

| Service | Variable | Value |
|---|---|---|
| `kinsmen-api` | `Auth__Authority` | `https://kinsmen.eu.auth0.com/` |
| `kinsmen-web` | `Auth0__Domain` | `kinsmen.eu.auth0.com` |
| `kinsmen-web` | `Auth0__ClientId` | Client ID from Part 2 |
| `kinsmen-web` | `Auth0__ClientSecret` | Client Secret from Part 2 |

`Auth0__Audience` is already set to `kinsmen-api` in `render.yaml`. After the first deploy, put the real `kinsmen-web` URL into the Auth0 callback and logout URLs (Part 2).

Render restarts clear the web app's session encryption keys, so users may need to log in again after a deploy. This is acceptable for the project; persisting the keys is a later improvement.

## Part 7: Checking it works

1. Start the API (with Part 5's settings) and the web app (HTTPS profile).
2. Click **Create Account**, sign up with a test email, and confirm you return to the site logged in.
3. In the Auth0 dashboard, open the new user: `app_metadata` should now show `"role": "Customer"`.
4. Book an appointment, then open **My Bookings**: the booking should appear.
5. Log out, sign up as a second customer, and confirm the first customer's booking is not visible.
6. Set a test user's role to `Barber`, link them to a barber in Atlas (Part 3), log in again, and confirm they can confirm a booking assigned to that barber but not one assigned to another barber.
7. Log out and confirm that opening `/MyBookings` sends you to the login page.

To inspect a token while debugging, copy it from the debugger (never from production) into [jwt.io](https://jwt.io) and check `aud`, `sub` and `role`.

## Security and testing notes for the report

- Passwords, password resets and email verification are handled by Auth0; our database stores no credentials.
- The API rejects tokens with the wrong issuer, wrong audience, an invalid signature or an expired lifetime (standard JWT bearer validation against Auth0's published keys).
- Roles are assigned server-side (Auth0 dashboard and Action); a user cannot choose their own role at sign-up.
- Ownership rules (customers see only their own bookings, barbers act only on their own schedule) are enforced by the API on the token's `sub`, so they hold even if the web app has a bug.
- Regression tests to add: missing token → 401; customer token on another customer's booking → 403/404; customer token on a staff-only action → 403; barber token on another barber's time block → 403.
