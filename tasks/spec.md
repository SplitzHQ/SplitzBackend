# Rate Limiting Specification

Status: Approved, implementation in progress
Owners: SplitzBackend and SplitzFrontend
Last updated: 2026-07-12

## Objective

Add application-level rate limiting that protects SplitZ from credential attacks, outbound-email abuse, and expensive image-upload bursts while preserving normal use for legitimate group-expense users.

Success means:

- Selected anonymous account endpoints enforce both client-IP and normalized account/email quotas.
- Selected authenticated upload endpoints enforce a shared per-user hourly quota and a process-wide concurrency cap.
- Every application-generated rejection returns a neutral `429 Too Many Requests` Problem Details response with `Retry-After`.
- The Vue client recognizes `429`, shows a localized live countdown, and prevents manual resubmission until the cooldown expires.
- Limits, proxy trust, endpoint coverage, and rejection behavior are covered by focused automated tests.
- Existing successful requests and non-rate-limit error behavior remain unchanged.

## Target Users And Threats

The target user is a legitimate SplitZ user registering, signing in, recovering an account, confirming an email address, or attaching an image. The controls are intended to reduce:

- Password and token guessing against anonymous Identity endpoints.
- Repeated registration, confirmation, and password-recovery email delivery.
- CPU, memory, and object-storage pressure from image processing.
- Accidental rapid resubmission from the browser.

Rate limiting is not an account lockout, CAPTCHA, bot-detection, email deduplication, or distributed denial-of-service solution.

## Scope

### Anonymous Account Endpoints

The following endpoints are in scope:

| Logical policy | Endpoints | IP quota | Account/email quota | Key |
| --- | --- | ---: | ---: | --- |
| Login | `POST /account/login` | 20 per 5 minutes | 10 per 5 minutes | Normalized submitted email |
| Registration | `POST /account/register` | 10 per hour | 6 per hour | Normalized submitted email |
| Email delivery | `POST /account/resendConfirmationEmail`, `POST /account/forgotPassword`, `POST /account/recovery/request` | 10 per hour, shared across the three routes | 6 per hour, shared across the three routes | Normalized submitted email |
| Email confirmation | `GET /account/confirmEmail` | 20 per 15 minutes | 10 per 15 minutes | Submitted `userId` |
| Password reset | `POST /account/resetPassword`, `POST /account/recovery/reset` | 20 per 15 minutes, shared across both routes | 10 per 15 minutes, shared across both routes | Normalized submitted email |

Each request must pass both applicable quotas. A rejection from either quota stops endpoint execution. Known and unknown email addresses consume the same quota and receive the same response.

All time-based policies use sliding windows with no queue:

- 5-minute windows: 5 one-minute segments.
- 15-minute windows: 15 one-minute segments.
- 1-hour windows: 12 five-minute segments.

### Authenticated Upload Endpoints

The following endpoints are in scope:

- `POST /account/avatar`
- `POST /group/{groupId}/avatar`
- `POST /transaction/{id}/receipt`
- `POST /transactiondraft/{id}/receipt`

All four routes share both limits:

- A process-wide concurrency cap of 5 active upload requests.
- A sliding quota of 200 upload requests per authenticated user per hour, using 12 five-minute segments.

Both limiters have queue size 0. The concurrency limiter returns a 5-second `Retry-After` fallback because an active upload's completion time cannot be calculated. The hourly limiter returns its calculated retry duration.

Authentication must run before the upload quota is partitioned. The stable Identity user ID is the partition key; unauthenticated requests continue to receive the existing authentication response and must not obtain an upload lease.

### Frontend Experience

Rate-limit handling is required for the current account screens and current upload callers:

- Login and resend-confirmation actions use independent cooldowns.
- Registration shows the neutral rate-limit message instead of the generic registration failure.
- Forgot-password remains enumeration-resistant and does not transition to its success state after a rejected request.
- Reset-password checks for `429` before interpreting validation errors.
- Email confirmation uses a dedicated rate-limited state instead of presenting the link as expired or invalid. Its initial request may run on mount, but subsequent retry is user initiated through a button enabled when the countdown reaches zero.
- User-avatar upload disables its picker trigger during cooldown and shows the remaining time.
- Transaction-receipt upload keeps the already-created transaction ID and selected receipt after a rate-limited upload. Retrying after cooldown uploads the receipt only; it must not create a duplicate transaction.

Cooldown state is in memory and local to the active screen/action. It is not persisted across navigation or reload, and requests are never retried automatically.

The client parses both valid `Retry-After` forms:

- Delta seconds, which is the backend's emitted format.
- An HTTP date, for compatibility with proxies.

If a `429` response has a missing, malformed, or already-expired value, the client uses a 60-second fallback. Remaining time is rounded up, updated once per second, announced through an accessible live region, and used to disable the affected command. Form fields may remain editable.

## Out Of Scope

- A global fallback limit for all API requests.
- General authenticated mutation limits for transactions, invoices, settlements, group links, or reads.
- Limits for refresh tokens, email-capabilities, account-management, or 2FA endpoints beyond attempts sent through `POST /account/login`.
- Redis, a distributed limiter, or edge/CDN rate-limit rules.
- Multiple backend instances. The approved topology is one backend process behind a known reverse proxy.
- New group-avatar or draft-receipt frontend controls; those API endpoints are protected for future callers.
- Persistent cooldowns, background retries, automatic retries, or queued requests.
- CAPTCHA, Identity lockout policy, provider-side email quotas, or changing the current CORS origin policy beyond exposing `Retry-After`.

## Tech Stack

### Backend

- ASP.NET Core Web API targeting `net10.0`.
- ASP.NET Core Identity minimal APIs plus MVC controllers.
- Built-in `Microsoft.AspNetCore.RateLimiting` middleware and `System.Threading.RateLimiting` primitives.
- Swashbuckle OpenAPI generation.
- xUnit backend tests.

### Frontend

- Vue 3.5 with `<script setup lang="ts">`.
- TypeScript 5.9, Vite 7, Pinia, and the generated `typescript-fetch` OpenAPI client.
- Fluent translations for English and Simplified Chinese.
- Existing `@vueuse/core` timing utilities.
- Vitest, Vue Test Utils, and Playwright.
- Bun as the package manager and script runner.

## Packages And Dependencies

### Reuse

- Reuse ASP.NET Core's built-in rate-limiting middleware; do not add a third-party limiter.
- Reuse `PartitionedRateLimiter`, sliding-window limiters, a concurrency limiter, and chained limiters.
- Reuse Identity's email normalizer so quota keys follow the same normalization rules as account lookup.
- Reuse `ProblemDetails`, the existing generated `ResponseError`, existing `SButton`, Fluent, and VueUse.

### Add

- Add `Microsoft.AspNetCore.Mvc.Testing` version `10.0.9` to `SplitzBackend.Tests` for hosted integration tests. `Microsoft.AspNetCore.TestHost` remains transitive.

No new production backend package or frontend package is approved by this specification.

## Design

### Framework Policy Layer

Register rate limiting through a focused service-registration extension called from `Program.cs`. Use named endpoint policies and route metadata rather than path checks spread through middleware.

Client-IP policies run in ASP.NET Core rate-limiting middleware. The partition key is `HttpContext.Connection.RemoteIpAddress` after trusted forwarded-header processing. A missing address uses one stable `unknown` partition and emits a structured warning without request secrets.

Upload enforcement uses a chained policy:

1. A global concurrency limiter with a single process-wide partition and permit limit 5.
2. A per-user sliding-window limiter with permit limit 200 per hour.

Using framework limiters avoids a hand-written timer, counter dictionary, or cleanup loop. The framework owns partition lifecycle and idle cleanup.

### Model-Bound Account Key Layer

Email/account keys are carried in JSON bodies or confirmation query values, so they are not read by middleware before model binding. Add one small endpoint-filter/service layer that:

1. Receives the already-bound request value.
2. Extracts the policy's email, account ID, or user ID.
3. Trims email and then normalizes it through Identity's `ILookupNormalizer`.
4. Acquires one permit from a singleton partitioned limiter for the logical policy.
5. Calls the endpoint only when the lease is acquired.
6. Uses the shared rejection writer when the lease is rejected.

The built-in and custom variants of a logical action must use the same limiter instance so changing paths cannot bypass the quota. Do not read and rewind request bodies in middleware, and do not log raw email addresses, reset codes, confirmation codes, passwords, or partition keys.

Identity-generated routes remain provided by `MapIdentityApi<SplitzUser>()`. A local endpoint-mapping/convention extension applies the named IP policy, account-key filter, and `429` metadata to the selected generated routes. Hosted tests must enumerate route metadata so a framework or route-name change fails visibly.

### Rejection Writer

Middleware and endpoint-filter rejections share one response writer. Every application-generated rejection must return:

- HTTP status `429`.
- Content type `application/problem+json`.
- Header `Retry-After: <whole-seconds>` using a delta value, rounded up with a minimum of 1.
- A Problem Details body with `status: 429`, title `Too Many Requests`, a neutral retry-later detail, and extension `code: rate_limit_exceeded`.

The body must not expose the policy name, threshold, partition key, account existence, or whether the IP or account quota rejected the request. Exact quotas remain server configuration, not public response data.

The CORS policy must expose the `Retry-After` response header so a cross-origin browser client can read it.

### Proxy Trust

Production uses one application instance behind a fixed proxy IP or private CIDR, but the actual value is deployment configuration and has no source-controlled default.

Forwarded-header handling must:

- Accept `X-Forwarded-For` only from explicitly configured known proxies or networks.
- Run before client-IP rate-limit partitioning.
- Parse and validate all configured IP/CIDR values at startup.
- Fail production startup when rate limiting is enabled for the approved proxy topology but no trusted proxy/network is configured.
- Never clear the framework trust lists to accept forwarded headers from arbitrary senders.

Local development may use the direct connection address without forwarded headers.

### Middleware Order

The effective request order must be:

1. Forwarded headers.
2. Routing and CORS.
3. Authentication.
4. Rate-limiting middleware.
5. Authorization.
6. Endpoint filters and endpoint execution.

Add an explicit authentication middleware call so user-based partitioning does not depend on implicit middleware insertion.

### Configuration

Add a validated `RateLimiting` options section following the existing `SectionName`, options binding, validation, and `ValidateOnStart` convention. Configuration contains:

- Enabled state.
- Trusted proxy IPs and networks, with no production default.
- Permit limit, window, segment count, and queue size for each logical anonymous policy.
- Upload hourly permit/window/segments.
- Upload global concurrency permit and fallback retry seconds.
- Default retry seconds for any rejection whose limiter provides no retry metadata.

Checked-in defaults match the approved values in this specification. Production-specific trusted proxy values belong in deployment configuration or secrets, not `appsettings.json`.

### Observability

Emit one structured warning for each rejection with:

- HTTP method and route pattern.
- Logical policy category.
- Partition type (`ip`, `account`, `user`, or `global`), but not its value.
- Retry-after seconds.

Never log request bodies, raw emails, IP partition values, credentials, or tokens. Metrics infrastructure and alerting are outside this release; rejection logs provide the initial tuning signal.

## Frontend Design

Add a non-generated rate-limit helper that:

- Detects generated `ResponseError` instances with status `429`.
- Parses `Retry-After` delta seconds or HTTP dates.
- Applies the 60-second fallback.
- Returns an absolute expiry time so countdowns do not drift.

Add a small reusable cooldown composable using existing Vue/VueUse APIs. It owns an expiry timestamp, exposes rounded remaining seconds and an active flag, ticks once per second, and stops its timer on disposal. It does not own API calls or retry them.

Pages remain responsible for deciding which command owns a cooldown. This keeps login and resend independent and avoids a global throttle state that could disable unrelated work.

Add synchronized English and Simplified Chinese Fluent messages for neutral rate-limit copy and retry actions. Countdown copy must not imply that an email address or account exists.

Do not edit files under `SplitzFrontend/src/backend/openapi` manually. Add the documented `429` response to backend OpenAPI, regenerate the client, and keep application handling based on `ResponseError` because generated fetch clients still throw for non-success responses.

## Project Structure

Expected backend changes:

```text
SplitzBackend/
  appsettings.json
  src/
    Program.cs
    Services/RateLimiting/
      RateLimitOptions.cs
      RateLimitPolicyNames.cs
      RateLimitingServiceCollectionExtensions.cs
      AccountRateLimitEndpointFilter.cs
      RateLimitRejectionWriter.cs
    OpenAPIGen/Filter/
      RateLimitResponseOperationFilter.cs
  SplitzBackend.Tests/
    RateLimitOptionsTests.cs
    RateLimitingEndpointMetadataTests.cs
    RateLimitingIntegrationTests.cs
```

Exact helper names may be adjusted to match the implementation, but responsibilities must remain focused. Avoid a general-purpose policy engine.

Expected frontend changes:

```text
SplitzFrontend/
  src/
    libs/
      rate-limit.ts
      use-rate-limit-cooldown.ts
      __tests__/
        rate-limit.spec.ts
        use-rate-limit-cooldown.spec.ts
    pages/
      LoginPage/
      RegisterPage/
      ForgotPasswordPage/
      ResetPasswordPage/
      ConfirmEmailPage/
      ProfilePage/
      NewExpensePage/ReviewAndCompletePage/
    locales/en/auth.ftl
    locales/zh-cn/auth.ftl
  e2e/auth-email.spec.ts
```

Generated OpenAPI output may change only through regeneration.

## Design Patterns And Local Idioms

### Named Policy Strategy

- Intent: vary algorithms and thresholds by endpoint category without branching inside controllers.
- Stable part: endpoint-to-policy assignment and the rejection contract.
- Variable part: configuration-backed quotas and window sizes.
- Participants: ASP.NET Core named policies, endpoint metadata, and options.
- Trade-off: several policy names require metadata tests, but they keep enforcement declarative and auditable.

### Composite/Chain For Uploads

- Intent: require both server-capacity and per-user fairness constraints.
- Participants: one global concurrency limiter and one per-user sliding limiter combined with the framework's chained limiter.
- Trade-off: a request can be rejected by either limiter, so the public response stays deliberately generic.

### Endpoint Filter For Bound Keys

- Intent: enforce email/account quotas without manually parsing request bodies in middleware or replacing Identity endpoints.
- Participants: generated/custom account endpoints, bound request DTOs, Identity normalization, partitioned limiter, and shared rejection writer.
- Trade-off: this is one extra endpoint-execution layer, justified because middleware cannot safely obtain JSON keys before model binding.

### No Additional Pattern

Do not introduce repositories, mediator commands, a custom middleware pipeline framework, or a distributed counter abstraction. Existing dependency injection, options, endpoint metadata, and framework limiters are sufficient.

## Acceptance Criteria

1. Requests below each configured limit reach the existing endpoint and preserve its existing status/body behavior.
2. The first request beyond either applicable anonymous quota returns the specified neutral Problem Details response and does not execute the endpoint.
3. Different IP partitions and different normalized account/email partitions do not consume each other's quota.
4. Email case and surrounding whitespace cannot create separate account partitions.
5. Built-in and custom recovery/reset variants consume shared logical quotas as listed in the scope table.
6. Known and unknown emails have indistinguishable rate-limit status, body, headers, and timing behavior within normal test tolerance.
7. Six active upload operations may not run concurrently: the first five can enter processing and the sixth receives `429` immediately with `Retry-After: 5`.
8. The 201st upload request by one authenticated user within the rolling hour is rejected; another user retains an independent hourly quota.
9. All four upload paths share the global concurrency limiter and each user's hourly quota.
10. `Retry-After` is readable by the browser across the configured CORS boundary.
11. Each affected frontend command shows a localized live countdown, remains disabled until expiry, and requires a manual retry.
12. A rate-limited confirmation link is not labeled expired or invalid.
13. A rate-limited receipt upload cannot create a duplicate transaction when retried.
14. Production startup fails with an actionable options-validation error when the approved proxy mode has no trusted proxy/network.
15. OpenAPI documents `429`, `application/problem+json`, and `Retry-After` for every protected endpoint.
16. No raw account key, email, password, token, confirmation code, reset code, or client-IP partition value appears in rejection logs.

## Testing Strategy

### Backend Unit Tests

- Validate checked-in defaults and reject non-positive limits, invalid windows/segments, nonzero queues, invalid retry values, malformed proxy entries, and incomplete production proxy configuration.
- Verify normalization maps equivalent email spellings to the same key.
- Verify the shared rejection writer's body, content type, status, delta-seconds header, and generic detail.
- Verify route metadata maps every in-scope endpoint to the intended IP policy and bound-key policy and leaves out-of-scope endpoints unmodified.

### Backend Hosted Integration Tests

Use `WebApplicationFactory<Program>` with isolated test configuration, a test database/user, trusted loopback proxy configuration, and replaceable image-processing/storage collaborators.

Cover:

- Threshold and threshold-plus-one behavior for each logical anonymous policy.
- IP isolation through trusted `X-Forwarded-For` values and rejection of spoofed forwarded values from an untrusted connection.
- Account/email isolation and case/whitespace normalization.
- Shared quotas across recovery and reset route variants.
- Neutral known/unknown-account behavior.
- Upload hourly isolation by authenticated user.
- Five blocked in-flight upload handlers followed by an immediate sixth-request rejection.
- CORS exposure of `Retry-After`.
- OpenAPI `429` documentation.
- Middleware ordering sufficient for authenticated user partitioning.

Tests must override permit limits when that makes behavior faster and clearer, while separate options tests preserve the approved defaults.

### Frontend Unit And Component Tests

- Parse delta seconds and HTTP-date headers.
- Use a 60-second fallback for missing, invalid, and expired headers.
- Return no cooldown for non-`429` errors.
- Verify countdown rounding, one-second updates, disposal, and no automatic callback.
- Use fake timers and generated `ResponseError` objects to verify each auth page's message, disabled state, expiry, and manual retry.
- Verify login `429` does not expose the resend-confirmation state.
- Verify confirmation `429` does not enter the invalid-link state.
- Verify profile upload cooldown disables the picker trigger.
- Verify receipt upload retains the transaction/receipt state and retries upload only.

### Frontend E2E

Extend `e2e/auth-email.spec.ts` with mocked `429` responses and `Retry-After` headers for at least login, recovery request, and email confirmation. Assert visible neutral copy, countdown progress, disabled action, and re-enabled manual retry. Component tests carry the broader action matrix.

### Validation Commands

Backend:

```powershell
dotnet test SplitzBackend.sln
dotnet build SplitzBackend.sln
dotnet format SplitzBackend.sln --verify-no-changes
```

Frontend:

```powershell
bun run type-check
bun run test:unit --run
bun run test:e2e e2e/auth-email.spec.ts
bun run lint
bun run format:check
```

## Rollout And Operations

1. Configure and verify the exact production proxy IP/CIDR before enabling production IP partitioning.
2. Rotate and remove any live-looking Resend credential from local source-controlled settings before rollout if it has ever been valid.
3. Deploy to the approved single backend instance.
4. Confirm forwarded client addresses and rejection logs without logging partition values.
5. Exercise one `429` per policy in a non-production or controlled production check and verify `Retry-After`, CORS, Problem Details, and frontend countdown behavior.
6. Review rejection logs after rollout. Quota changes require updating configuration, tests, and this specification before implementation.

Because limiter state is process-local, every application restart resets counters. Scaling beyond one instance requires a new design decision: enforce limits at a shared ingress/provider or adopt an approved distributed implementation.

## Boundaries

Always:

- Preserve account-enumeration resistance.
- Trust forwarded client addresses only from configured proxies/networks.
- Apply the same logical quota across equivalent built-in and custom routes.
- Keep response and log content free of secrets and partition values.
- Regenerate, never hand-edit, frontend OpenAPI files.
- Update this specification before changing scope, quotas, algorithms, topology, dependencies, or retry behavior.

Ask first:

- Adding a production dependency or distributed state store.
- Adding limits to endpoints outside the approved scope.
- Changing any approved threshold, segment count, fallback, or shared-pool relationship.
- Enabling a global limiter, queueing, automatic retries, persistent cooldowns, or multiple backend instances.
- Trusting a new proxy or network range.

Never:

- Trust arbitrary `X-Forwarded-For` values.
- Parse account JSON bodies manually in middleware.
- Return different rate-limit behavior based on whether an account exists.
- Log credentials, email/account partition values, tokens, or recovery codes.
- Retry password, token, email-send, or upload requests automatically.

## Reference Basis

The design follows current ASP.NET Core 10 guidance for `AddRateLimiter`, named endpoint policies, partitioned and concurrency limiters, `OnRejected`, `Retry-After`, authentication-aware middleware ordering, and trusted forwarded headers behind proxies. It uses repository-local Identity, options, OpenAPI, Vue, Fluent, and testing conventions discovered before drafting this specification.
