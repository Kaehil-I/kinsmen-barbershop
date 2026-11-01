# Deploying to Render

The system runs as two Render web services defined in [`render.yaml`](../../render.yaml):

| Service | Image | Public URL | Health check |
|---|---|---|---|
| `kinsmen-web` | `src/Kinsmen.Web/Dockerfile` | The site customers and staff use | `/` |
| `kinsmen-api` | `src/Kinsmen.Api/Dockerfile` | Called only by `kinsmen-web`, server to server | `/health/ready` (includes a MongoDB ping) |

Browsers talk only to `kinsmen-web`; its controllers call the API, so no CORS configuration is needed. Both services deploy from `integration-part3` while Part 3 is tested, and only after that commit's GitHub checks pass. Switch `branch` to `main` for the submission build.

## One-time setup

1. **Atlas network access.** Render's free tier has no fixed outbound IP, so allow `0.0.0.0/0` under *Network Access* in the `kinsmen-dev` cluster. Pair this with a dedicated database user that has read/write on `kinsmen_dev` only and a long generated password.
2. **Create the Blueprint.** In Render: *New → Blueprint*, select this repository and the `integration-part2` branch. Render reads `render.yaml` and asks for every `sync: false` value:
   - `Mongo__ConnectionString`: the Atlas `mongodb+srv://` URI for the database user above.
   - `Auth__Authority`: the HTTPS identity provider that issues access tokens (see *Authentication* below).
   - `Api__BaseUrl`: the public URL of `kinsmen-api`, for example `https://kinsmen-api.onrender.com`. If the API URL is not known yet, enter a placeholder and update it after the first deploy.
3. **Initialise the database once** (validators and indexes) from a developer machine with `./scripts/Initialize-Atlas-Development.ps1` (see `docs/backend/GETTING-STARTED.md`). The hosted API does not seed demo data.

Secrets live only in the Render dashboard. Do not commit connection strings, passwords, signing keys or tokens; `.dockerignore` also keeps `appsettings.Development.json` out of the images.

## Authentication

Login uses Auth0; follow [`docs/auth/AUTH0-SETUP.md`](../auth/AUTH0-SETUP.md). In Production the API refuses to start without `Auth:Authority`, and development tokens are disabled by design, so the Auth0 tenant must exist before the Blueprint is applied. Render asks for `Auth__Authority` on the API and `Auth0__Domain`, `Auth0__ClientId` and `Auth0__ClientSecret` on the web app.

## Booking emails

Booking confirmation emails are sent through [Brevo](https://www.brevo.com)'s HTTPS API, because Render's free tier blocks outbound SMTP. They are off until configured: with no API key the API uses a no-op sender. To turn them on, set these on `kinsmen-api` (**Environment** in the Render dashboard):

| Variable | Value |
|---|---|
| `Email__BrevoApiKey` | Brevo API key (secret; dashboard only) |
| `Email__FromAddress` | A sender address verified in Brevo |
| `Email__FromName` | Display name; `render.yaml` sets `Kinsmen Barbers` |

Set `Email__BrevoApiKey` and `Email__FromAddress` together. The API refuses to start with a key but no from-address, which would fail the deploy.

Because the Blueprint already exists, Render does not prompt for these new `sync: false` values when `render.yaml` changes; add them by hand in the dashboard.

## Shared proxy key

`render.yaml` defines an environment group, `kinsmen-internal`, with a `Proxy__Key` that Render generates and attaches to both services. The web app sends it with each API call so the API can rate limit per visitor rather than counting the whole web server as one caller (see the API contract's error-handling notes). Nobody needs to enter or copy it. If it's missing, everything still works, but anonymous visitors share one API rate-limit bucket. To rotate it, regenerate the value in the group and redeploy both services.

## Part 3 release checklist

Part 3 adds the shop (a new `products` collection), customer profiles, booking emails with 24-hour reminders and the admin dashboard. Do these in order:

1. **Initialise the database first.** The API's `/health/ready` check, which Render uses, returns 503 until the `products` collection exists. If the deploy goes first it fails and the previous version keeps running, which is safe but confusing. From a checkout of `integration-part3`, run `./scripts/Initialize-Atlas-Development.ps1` with the Atlas admin user (the Render user is read/write only). It creates or updates validators and indexes, and seeds the shop's demo products. Seeding only adds records that are missing and leaves existing, edited ones alone. Customer profiles need no initialising (their collection is created on first use).
2. **Switch both services to `integration-part3`.** In Render, open each service, then *Settings → Build & Deploy → Branch*. Keep `render.yaml` in step with that.
3. **Check the deploy.** Open `/health/ready` on the API (it should say ready), then the site: Shop, Book a Chair with a product, the profile page, and (as an admin) Dashboard and Admin. Book with a product, cancel it, and confirm stock goes back.
4. **Emails and reminders.** They need the Brevo settings above. A reminder is sent for a *confirmed* booking that starts within 24 hours of the next sweep, which runs every 10 minutes while the API is awake.
5. **Final build.** After `integration-part3` is merged into `main`, switch both services to `main` and switch the repository's default branch to `main`.

## Free-tier behaviour

Free services sleep after about 15 minutes without traffic, and the first request afterwards can take up to a minute. Open both services shortly before a demo or presentation.

### Keeping the API awake for reminders

The reminder worker only runs while the API is awake, so reminders on the free tier are unreliable unless something requests the API now and then. Free services share a monthly allowance of instance hours (about 750 at the time of writing; check Render's usage page), and a service that never sleeps uses about 720 to 744 of them by itself. Don't keep both awake all month. A schedule that fits:

- **Demo and marking days:** request `https://kinsmen-api.onrender.com/health/live` and the site every 10 to 14 minutes from a scheduler such as cron-job.org, and switch it off afterwards.
- **Otherwise:** request the API once an hour (about a quarter of the hours). Reminders then go out within about an hour of the 24-hour mark.

No code change is needed for either.

## Checks before a deploy

The *Build and test* workflow builds both Docker images and starts each one with production settings (`Container images` job), alongside the unit and MongoDB tests and a check for NuGet packages with known security advisories. A deploy only starts when those checks pass.
