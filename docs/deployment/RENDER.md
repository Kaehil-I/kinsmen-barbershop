# Deploying to Render

The system runs as two Render web services defined in [`render.yaml`](../../render.yaml):

| Service | Image | Public URL | Health check |
|---|---|---|---|
| `kinsmen-web` | `src/Kinsmen.Web/Dockerfile` | The site customers and staff use | `/` |
| `kinsmen-api` | `src/Kinsmen.Api/Dockerfile` | Called only by `kinsmen-web`, server to server | `/health/ready` (includes a MongoDB ping) |

Browsers talk only to `kinsmen-web`; its controllers call the API, so no CORS configuration is needed. Both services deploy from `integration-part2` while we test, and only after that commit's GitHub checks pass. Switch `branch` to `main` for the submission build.

## One-time setup

1. **Atlas network access.** Render's free tier has no fixed outbound IP, so allow `0.0.0.0/0` under *Network Access* in the `kinsmen-dev` cluster. Pair this with a dedicated database user that has read/write on `kinsmen_dev` only and a long generated password.
2. **Create the Blueprint.** In Render: *New → Blueprint*, select this repository and the `integration-part2` branch. Render reads `render.yaml` and asks for every `sync: false` value:
   - `Mongo__ConnectionString`: the Atlas `mongodb+srv://` URI for the database user above.
   - `Auth__Authority`: the HTTPS identity provider that issues access tokens (see *Authentication* below).
   - `Api__BaseUrl`: the public URL of `kinsmen-api`, for example `https://kinsmen-api.onrender.com`. If the API URL is not known yet, enter a placeholder and update it after the first deploy.
3. **Initialise the database once** (validators and indexes) from a developer machine with `./scripts/Initialize-Atlas-Development.ps1` (see `docs/backend/GETTING-STARTED.md`). The hosted API does not seed demo data.

Secrets live only in the Render dashboard. Do not commit connection strings, passwords, signing keys or tokens; `.dockerignore` also keeps `appsettings.Development.json` out of the images.

## Authentication

In Production the API refuses to start without `Auth:Authority`, and development tokens are disabled by design. The hosted booking flow therefore depends on the real authentication work: the API needs the identity provider's HTTPS URL, and `kinsmen-web` needs to replace `DevelopmentTokenProvider` with a provider that uses the signed-in user's token.

## Free-tier behaviour

Free services sleep after about 15 minutes without traffic, and the first request afterwards can take up to a minute. Open both services shortly before a demo or presentation.

## Checks before a deploy

The *Build and test* workflow builds both Docker images and starts each one with production settings (`Container images` job), alongside the unit and MongoDB tests. A deploy only starts when those checks pass.
