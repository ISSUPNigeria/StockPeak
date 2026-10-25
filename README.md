# StockPeak

Stock peak and bottom scanner with Gemini AI forecasts, workspaces and roles, email OTP verification, invitations, a trade journal and Flutterwave subscriptions. ASP.NET Core 10, PostgreSQL, and a plain HTML/CSS/JavaScript front-end.

> Prices are simulated until you plug in a real market feed (see "Real market data"). Signals and forecasts are information, not financial advice, and StockPeak does not place orders.

## What is in the box

| Area | What it does |
|---|---|
| Market | Scanner with PEAK / BOTTOM / HOLD signals (RSI plus recent highs and lows), chart with marked peaks and troughs, watchlist, position calculator |
| AI | 7-day forecast from the free Gemini API (Pro and Elite) |
| Learn | Slider on the home page: how to pick, read, plan, place and review a trade |
| Accounts | Register as User or Merchant, email OTP verification, login, forgot and reset password |
| Roles | User, Merchant, Agent, Manager, Admin, scoped per workspace |
| Workspaces | The same email can belong to many workspaces, each with its own role. Switch from the header |
| Invites | Managers and admins invite people by email. The link lets them create an account or join with an existing one |
| Trade journal | Log a trade when you open it. When you complete it, record what you did, what went well and what you would change. Stats and CSV export |
| Billing | Flutterwave checkout (Pro $15, Elite $39 per month), verified server side by redirect and webhook |
| Alerts | In-app alerts plus emails when a watched stock hits a peak (paid plans) |
| Ops | Swagger UI, health check, rate limiting, Docker, docker-compose with PostgreSQL and automatic HTTPS |

## Project layout

```
src/StockPeak.Shared          DTOs, enums (Role, Plan, ...)
src/StockPeak.Domain          Entities: User, Workspace, Membership, OtpCode, Invite, Trade, ...
src/StockPeak.Application     Interfaces, PeakDetector, RoleRules, Crypto
src/StockPeak.Infrastructure  EF Core + PostgreSQL, Gemini, Flutterwave, SMTP, OTP, tokens, background alerts
src/StockPeak.API             Minimal API endpoints (Endpoints/), Swagger, wwwroot front-end
```

## Run locally (Visual Studio or CLI)

1. Install the .NET 10 SDK and Docker.
2. Start PostgreSQL: `docker compose up -d db` (the override file exposes it on localhost:5432 with user, password and database `stockpeak`).
3. Run the API: `dotnet run --project src/StockPeak.API`, then open the printed URL.
4. Swagger UI is at `/swagger` in Development.

With no `Smtp:Host` set, emails (OTP codes, invitations) are written to the API console instead of being sent. Copy the code from the log while developing, or run the full stack with `docker compose up --build` and read mail in Mailpit at http://localhost:8025.

To make yourself an admin, set `Admin:Emails` (for example `"Admin": { "Emails": ["you@example.com"] }`) before you register with that email.

## Run in production

```bash
cp .env.example .env        # fill in every value
docker compose -f docker-compose.yml up -d --build
```

- `-f docker-compose.yml` skips the development override (Mailpit, open ports).
- Point your domain's DNS at the server. Caddy gets and renews the HTTPS certificate for `DOMAIN` automatically (ports 80 and 443 must be open).
- PostgreSQL data lives in the `pgdata` volume. Back it up with `docker compose exec db pg_dump -U stockpeak stockpeak > backup.sql`.
- Set the Flutterwave webhook URL to `https://DOMAIN/api/billing/webhook` and use the same value as `FLW_SECRET_HASH`.
- Keep `SWAGGER_ENABLED=false` unless you want public API docs.

### Configuration

| Setting (env var) | Purpose |
|---|---|
| `ConnectionStrings__Default` | PostgreSQL connection string |
| `Jwt__Key` | Signs login tokens and OTP hashes. 32+ random characters. The app refuses to start without it |
| `App__BaseUrl` | Public URL used in invite links and the Flutterwave redirect |
| `Admin__Emails__0` | Email that becomes Admin of its workspace when it registers |
| `Gemini__ApiKey` | Free key from Google AI Studio |
| `Flutterwave__SecretKey`, `Flutterwave__SecretHash` | Flutterwave secret key and webhook hash |
| `Smtp__Host`, `Port`, `EnableSsl`, `User`, `Password`, `From` | Outgoing email |
| `Swagger__Enabled` | Expose Swagger UI outside Development |

## Sign-up, verification and passwords

1. `POST /api/auth/register` creates the account as **User** or **Merchant** and emails a 6-digit code. Other roles come from invitations.
2. `POST /api/auth/confirm-email-otp` checks the code, verifies the email and signs the user in.
3. `POST /api/auth/verify-email-otp` resends the verification code. `POST /api/auth/verification-code` sends a code for either purpose (`EmailVerification` or `PasswordReset`).
4. `POST /api/auth/login` signs in. Unverified accounts get a new code and a 403 with `code: EMAIL_NOT_VERIFIED`.
5. `POST /api/auth/forgot-password` then `POST /api/auth/reset-password` (email, code, new password).

Codes are stored hashed, expire after 10 minutes, allow 5 attempts, and can be requested once a minute. Endpoints never reveal whether an email is registered. Auth endpoints are rate limited per IP.

## Roles and workspaces

| Role | Self sign-up | Can do |
|---|---|---|
| User | Yes | Watchlist, journal, alerts |
| Merchant | Yes | Same as User, intended for business owners |
| Agent | No | Also sees the member list and all workspace trades |
| Manager | No | Also invites people and manages User, Merchant and Agent roles |
| Admin | No | Everything, including managing Managers and Admins |

A token is scoped to one workspace and carries that workspace's role. `POST /api/workspaces` creates a workspace for the same email (you become its Admin). `POST /api/workspaces/{id}/switch` returns a token for another workspace.

## Invitations

A Manager or Admin calls `POST /api/invites` with an email and role. The invitee gets a link `/?invite=TOKEN` (also returned to the inviter). The page shows who invited them. They enter a name and password to create an account, or just their password if the email already has one, and join with the invited role. Links expire after 7 days and can be revoked. Tokens are stored hashed.

## Trade journal

Open a trade with `POST /api/trades`. When you complete it, `POST /api/trades/{id}/close` stores the exit price and your review: what you did, what went well, what you would change. Edit the review later with `PUT /api/trades/{id}/notes`. `GET /api/trades/stats` returns P&L, win rate, best and worst trade, and `GET /api/trades/export` downloads a CSV. Users see their own trades. Agents, Managers and Admins can include the whole workspace.

## API reference

Open `/swagger` for the interactive docs (use Authorize with the token from login or confirm-email-otp). Groups:

- **Auth**: register, verify-email-otp, confirm-email-otp, verification-code, login, forgot-password, reset-password, me
- **Workspaces and roles**: GET /api/roles, GET and POST /api/workspaces, switch, members, member role, remove member, overview
- **Invites**: create, list, revoke, preview, accept
- **Trade journal**: open, list, close, notes, delete, stats, export
- **Market, watchlist and alerts**: stocks, stock detail, AI predict, watchlist, notifications
- **Billing**: checkout, callback, webhook

## Real market data

Implement `IMarketDataService` (two methods: quotes and history) for Twelve Data, Alpha Vantage or an NGX provider, then replace the `SimulatedMarketData` registration in `Infrastructure/Services.cs`.

## Database changes

The app creates its schema on first start (`EnsureCreated`). Before you store real customer data, switch to EF Core migrations (`dotnet ef migrations add Initial` in the Infrastructure project, then `Database.Migrate()` in `Program.cs`) so later changes upgrade your data instead of needing a fresh database.

## Before you sell it

Add terms, a privacy policy and a clear risk disclaimer. Check financial-advice and data-protection rules in each country you sell in.
