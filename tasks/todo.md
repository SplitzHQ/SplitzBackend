# Rate Limiting Task List

Source plan: `tasks/plan.md`
Source specification: `tasks/spec.md`
Status: Implementation in progress; Tasks 1-6 complete

Update this checklist incrementally during implementation. Do not mark a task complete until its focused verification passes.

## Review Gate

- [x] Human approves `tasks/plan.md`.
- [x] Human confirms `tasks/spec.md` may be treated as approved and its status updated during implementation.
- [x] Human accepts that production enablement remains blocked until the real trusted proxy IP/CIDR is configured.
- [x] Human accepts one OpenAPI regeneration after backend endpoint metadata is complete.

## Task 1: Login End-To-End Baseline

Depends on: Review Gate

- [x] Add validated rate-limit options with all approved defaults and Production proxy requirements.
- [x] Add policy names, endpoint metadata, shared rejection writer, and safe rejection logging.
- [x] Configure trusted forwarded headers, explicit authentication/rate-limiter ordering, and CORS exposure of `Retry-After`.
- [x] Add login IP and normalized-email policies to `POST /account/login`.
- [x] Add hosted backend test infrastructure and `Microsoft.AspNetCore.Mvc.Testing` `10.0.9`.
- [x] Add frontend `429`/`Retry-After` parser with 60-second fallback.
- [x] Add reusable in-memory live-countdown composable.
- [x] Add synchronized neutral auth rate-limit translations.
- [x] Add login cooldown behavior and guards without exposing resend-confirmation state.
- [x] Add backend option, rejection, partition, proxy-trust, and login integration tests.
- [x] Add frontend parser, countdown, login component, and login E2E tests.
- [x] Focused verification passes as listed in Task 1 of `tasks/plan.md`.

## Task 2: Registration Path

Depends on: Task 1

- [x] Add registration IP and normalized-email pools with approved defaults.
- [x] Apply policies and endpoint metadata to `POST /account/register`.
- [x] Add registration `429` cooldown and submit guards.
- [x] Preserve successful registration, capability lookup, and non-`429` failures.
- [x] Add hosted registration and endpoint inventory tests.
- [x] Extend registration component tests.
- [x] Focused verification passes as listed in Task 2 of `tasks/plan.md`.

## Task 3: Shared Email-Delivery Path

Depends on: Task 1

- [x] Add one shared IP pool and one shared normalized-email pool for resend, built-in forgot-password, and custom recovery request.
- [x] Apply policies to all three generated/custom routes.
- [x] Test cross-route quota consumption and known/unknown email parity.
- [x] Activate independent resend cooldown on the login screen.
- [x] Add forgot-password cooldown without entering the submitted state after `429`.
- [x] Extend login and forgot-password component tests.
- [x] Add recovery rate-limit E2E coverage.
- [x] Focused verification passes as listed in Task 3 of `tasks/plan.md`.

## Task 4: Email-Confirmation Path

Depends on: Task 1

- [x] Add confirmation IP and submitted-user-ID pools with approved defaults.
- [x] Apply policies and metadata to `GET /account/confirmEmail`.
- [x] Ensure confirmation codes never enter keys or logs.
- [x] Refactor confirmation request into a reusable action.
- [x] Add a dedicated rate-limited state, countdown, and manual retry button.
- [x] Preserve incomplete-link, success, and ordinary expired-link behavior.
- [x] Add hosted confirmation, component, and E2E tests.
- [x] Focused verification passes as listed in Task 4 of `tasks/plan.md`.

## Task 5: Shared Password-Reset Path

Depends on: Task 1

- [x] Add shared IP and normalized-email pools for built-in and custom reset routes.
- [x] Apply policies to `POST /account/resetPassword` and `POST /account/recovery/reset`.
- [x] Test cross-route quota consumption and prevention of reset execution after rejection.
- [x] Handle `429` before reset validation-problem parsing.
- [x] Add reset countdown and submission guards.
- [x] Preserve mismatch, password-policy, invalid-token, success, and generic errors.
- [x] Add hosted reset and component tests.
- [x] Focused verification passes as listed in Task 5 of `tasks/plan.md`.

## Task 6: User-Avatar Upload Path

Depends on: Task 1

- [x] Add upload endpoint metadata.
- [x] Add metadata-gated process-wide concurrency limiter with limit 5 and queue size 0.
- [x] Add per-user hourly upload limiter with limit 200 and 12 segments.
- [x] Preserve unauthenticated behavior through a no-op upload partition before authorization.
- [x] Apply upload policies to `POST /account/avatar`.
- [x] Add controllable blocking image-storage test fake.
- [x] Test sixth concurrent request rejection, per-user hourly isolation, and non-upload exclusion.
- [x] Add avatar cooldown, picker guards, and localized profile copy.
- [x] Add `ProfilePage` component tests.
- [x] Focused verification passes as listed in Task 6 of `tasks/plan.md`.

## Task 7: Transaction-Receipt Retry Path

Depends on: Task 6

- [ ] Apply shared upload policies to `POST /transaction/{id}/receipt`.
- [ ] Test shared avatar/receipt concurrency and hourly pools.
- [ ] Split transaction save from pending receipt upload in `AddExpenseDetailsSheet.vue`.
- [ ] Preserve transaction ID, selected file, and preview after a receipt `429`.
- [ ] Make manual retry call receipt upload only, without a second transaction save.
- [ ] Disable save/upload and receipt-picker commands during cooldown.
- [ ] Add receipt backend integration and component tests.
- [ ] Focused verification passes as listed in Task 7 of `tasks/plan.md`.

## Task 8: Complete Shared Upload Pools

Depends on: Task 6

- [ ] Apply upload policies to `POST /group/{groupId}/avatar`.
- [ ] Apply upload policies to `POST /transactiondraft/{id}/receipt`.
- [ ] Seed minimum ownership data needed by hosted upload tests.
- [ ] Test global concurrency across all four upload routes.
- [ ] Test one user's shared hourly pool and cross-user isolation across all four routes.
- [ ] Assert exactly the four approved upload actions carry upload metadata.
- [ ] Confirm existing authorization and ownership responses remain unchanged.
- [ ] Focused verification passes as listed in Task 8 of `tasks/plan.md`.

## Task 9: Publish The OpenAPI Contract

Depends on: Tasks 2, 3, 4, 5, 7, and 8

- [ ] Add and register the rate-limit Swashbuckle operation filter.
- [ ] Document `429`, `application/problem+json`, `ProblemDetails`, and `Retry-After` on every protected endpoint.
- [ ] Assert out-of-scope endpoints do not gain rate-limit documentation.
- [ ] Add Swagger document tests through `ISwaggerProvider`.
- [ ] Start the backend in Development with safe local configuration.
- [ ] Regenerate `SplitzFrontend/src/backend/openapi` through the documented command.
- [ ] Review generated changes and confirm success signatures remain unchanged.
- [ ] Run frontend type-check and diff checks.
- [ ] Focused verification passes as listed in Task 9 of `tasks/plan.md`.

## Task 10: Hardening And Release Validation

Depends on: Task 9 and all feature slices

- [ ] Run the complete endpoint inventory and threshold matrix with isolated limiter state.
- [ ] Verify every generated `429` contract, CORS exposure, safe log shape, and retry fallback.
- [ ] Verify disabled enforcement bypasses limits without missing-policy failures.
- [ ] Verify application restart resets process-local counters.
- [ ] Map all 16 specification acceptance criteria to automated coverage or deployment checks.
- [ ] Run full backend test, build, and format validation.
- [ ] Run full frontend type-check, unit, E2E, lint, and format validation.
- [ ] Run `git diff --check` in both repositories.
- [ ] Review diffs for unrelated files, credentials, lockfiles, and unintended generated changes.
- [ ] Update specification/plan status only after human confirmation.
- [ ] Record the trusted proxy IP/CIDR as an unresolved deployment prerequisite without inventing a value.

## Production Enablement

- [ ] Obtain the actual trusted reverse-proxy IP/CIDR.
- [ ] Configure the trusted proxy/network outside source-controlled defaults.
- [ ] Rotate and remove any real Resend credential that has appeared in local source-controlled settings.
- [ ] Verify forwarded client addresses in the deployed single-instance topology.
- [ ] Exercise one controlled `429` per policy and verify response, CORS, logs, and frontend countdown.
- [ ] Review rejection logs and return to the specification before changing quotas or topology.
