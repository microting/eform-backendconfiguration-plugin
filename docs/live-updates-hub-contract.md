# Live-update hub contract (mobile), version 1

The server tells connected clients that something changed. It never sends the change itself. A
message is a **hint to refetch** through the existing gRPC calls (`ListChanges`, `GetEvent`).
The source of truth is `BackendConfiguration.Pn/Hubs/LiveHubContract.cs`. This note explains it.

## Discovery

- `GetServerInfo.capabilities` contains `"live_hub"` when the hub is served.
  - A server without it, or one that answers `GetServerInfo` with UNIMPLEMENTED, has no hub. The
    client keeps polling as it does today.
- `contract_version` stays at 2. It counts revisions of the gRPC proto, and the hub is outside
  the proto. Clients test for the capability, as `events.proto` says.
- The path follows a convention, so `GetServerInfoResponse` has no field for it.
  - The URL is `<web origin>/hub/backend-configuration`, for example
    `https://<customerNo>.microting.com/hub/backend-configuration`.
  - The web origin is the one the app already uses for REST, which is the same host name as gRPC.
  - It is **not** the gRPC port. Locally the hub is on the REST port (5000), not on 5001.
  - The pattern is kanban's (`/hub/kanban`). In the cluster, the gRPC ingress claims only
    `/eform.`, `/timeplanning.` and `/backend_configuration.`. Everything else, including this
    path and its WebSocket upgrade, goes to the web port, so no chart change is needed.

## Connecting

- SignalR, with the JSON protocol. All transports are allowed; WebSockets is preferred.
- The token is the host JWT that the gRPC `EformAuth` login returns.
  - Pass it through the client's `accessTokenFactory`.
  - Over WebSockets the client sends it as `?access_token=`. The server also accepts an
    `Authorization: Bearer` header, and it accepts the query on every transport.
- The token is accepted until `exp` plus 5 minutes, JwtBearer's default ClockSkew (the host does not override it),
  the same rule as for the gRPC and REST calls. The connection ends at that instant (see
  "Token expiry" below).
- A user may hold at most 10 live connections. This is checked before any database lookup.
- **Refusals.** A refused connection is not failed with an exception, because SignalR would log
  every refused reconnect at Error level.
  - Instead the server sends `Refused {"v":1,"code":"..."}` to the caller and closes the
    connection.
  - The same message comes before a server-side close later in the connection's life (token
    expired, worker revoked).
  - Delivery is best effort: a close may arrive without it. A client treats a close without
    `Refused` as an ordinary disconnect, and reconnects with backoff.
- **Codes:**
  - `not_authenticated`: no valid token, the token expired, or its user no longer exists. Get a
    fresh token (or sign in again) before reconnecting.
  - `account_disabled`: the account is disabled or the worker resigned. This is the same detail
    the gRPC calls send. Sign out, and do not reconnect.
  - `too_many_connections`: the user already has 10 live connections. Back off before retrying.
  - `revoked`: the server ended the connection because the worker or site was removed or
    resigned. Reconnect once; the connect checks then decide.
- After `Refused`, a client must stop its automatic reconnect and act on the code.
- **Two caller kinds** (package 11b). Both get the same token, expiry, connection-cap and refusal
  rules.
  - A **worker** (the user resolves to an SDK site, as for the gRPC calls) joins its own site
    group automatically on connect.
  - A **web user** (signed in and active, but not a worker: SDK site 0) connects as a "web"
    connection in **no site group**. It never receives `AssignmentsChanged`, and every
    `JoinProperty` call is refused (as for an unassigned caller). It subscribes with
    `JoinCalendarProperty` instead.
- **Wire details**, observed end to end with the real host JwtBearer handler and a raw
  WebSocket client (`LiveHubWireTests`):
  - The client may skip the negotiate step: it opens `ws(s)://<origin>/hub/backend-configuration?access_token=...`
    directly. The upgrade answers HTTP 101, even for a missing, garbage or expired token.
  - The handshake reply `{}` comes first, and then a refusal: `Refused`, then SignalR's Close
    message `{"type":7}`, then a WebSocket close (1000, normal closure).

## Client → server

| Method | Argument | Result |
|---|---|---|
| `JoinProperty` | `propertyId` (string, as `Property.id` on the gRPC wire) | A result object, `{"v":1,"code":"ok"}` or `{"v":1,"code":"<refusal>"}` |
| `LeaveProperty` | `propertyId` | No result. Always completes, and is idempotent. |
| `JoinCalendarProperty` | `propertyId` | Same result object and codes as `JoinProperty` (web calendar, package 11b) |
| `LeaveCalendarProperty` | `propertyId` | No result. Always completes, and is idempotent. |

- **`JoinProperty` refusal codes:** `property_not_available`, `too_many_properties`,
  `account_disabled` and `not_authenticated`.
- A refusal is a normal result, not an invocation error.
- A failed invocation (an error from `invoke`) means an unexpected server fault. The client
  retries later, or resyncs on reconnect.

- `property_not_available` covers all of these cases with one answer, so ids cannot be probed:
  - the property does not exist;
  - it is not assigned to the caller;
  - its assignment was removed;
  - the id is malformed;
  - the caller is not a worker.
- The access rule is the gRPC Events rule: a non-removed `PropertyWorker` row for the caller's
  SDK site (`IBackendConfigurationUserPropertyAccess.HasAccessAsync`).
- The caller is resolved again on every join, so a worker who resigned after connecting is
  refused.
  - When that check finds the account disabled, resigned or gone, the join answers
    `account_disabled` (or `not_authenticated`) **and the server ends the connection**: `Refused`
    with the same code, then close. The connection stops receiving hints for the groups it
    had joined.
- **Remaining exposure, for workers and web users alike.** A user deactivated outside this
  plugin (the host's user admin sets `EformUser.IsActive = false` and does not tell the hub)
  keeps receiving ids-only hints for the groups it already joined until any of these happens:
  - its token expires (at most 24 h + 5 min);
  - it reconnects;
  - it calls `JoinProperty` or `JoinCalendarProperty` again.
- Deactivation through this plugin ends the connection at once:
  - resigning a worker ends its site's connections **and** its login's connections by user id,
    so a web connection of the same login ends too;
  - deleting a worker ends its site's connections.
- A connection may hold at most 200 property groups.
- Once the token has expired (`exp` + ClockSkew), `JoinProperty` answers `not_authenticated`
  and the server closes the connection.

### `JoinCalendarProperty` (web calendar, package 11b)

**Who may join what.** This is the calendar's own rule, a product decision:
`CalendarController` carries only `[Authorize]`, so any signed-in, active user may load any
property's calendar.
- The same user, worker or web user, may subscribe to that property's hints: any **existing,
  non-removed** property, with no assignment needed.
- The property group is the **same** group `JoinProperty` joins and the publishers send to, so
  a calendar member receives exactly the `TaskChanged` hints a worker receives.
- Refusals use the same uniform `property_not_available` for a removed, non-existent or
  malformed id, so ids cannot be probed.
- Every join runs the same checks as `JoinProperty`:
  - expiry;
  - the caller is resolved again, so a disabled account answers `account_disabled` and the
    connection ends;
  - the 200-group cap;
  - the revoke race guard.
- **`JoinProperty` is not widened.** A worker still needs the assignment there.

**One membership per (connection, property group).** It does not matter which method joined it.
- `LeaveProperty` and `LeaveCalendarProperty` both end it.
- A worker's unassignment (`RevokePropertyAccess`) also ends a membership that worker holds
  through `JoinCalendarProperty`. A calendar client may simply join again, because the calendar
  rule allows it.

**No capability string.** `GetServerInfo` does not list one for this method, because the
contract test pins the capability list. The Angular calendar ships with the plugin (same
version), and the mobile app does not use this method.

**Security note.** Any signed-in, active web user can learn **that** something changed on any
property id (ids only, never data), as they can already load that property's calendar.

**The web calendar client (package 11c).** The code is `BackendConfigurationCalendarLiveService`
and `CalendarLiveReloadScheduler` in `eform-client/.../modules/calendar/services/`.
- There is one connection per open calendar view, and it ends with the view. It follows the
  property on screen: `LeaveCalendarProperty` for the old one, `JoinCalendarProperty` for the new
  one.
- After a reconnect it joins again and the calendar reloads once (resync).
- After the first join of a property, the calendar reloads once only if none of its own loads
  started after that join was sent.
- A `TaskChanged` for the property on screen reloads the week or month after about 1 s, one
  reload per burst.
- A live reload runs in the background. It uses the request that does not toast, and when any
  request fails it keeps what is on screen. Loads the user starts are unchanged.
- It never reloads while any of these is going on:
  - a task popover or a dialog is open;
  - a drag or a resize is under way;
  - a move or resize is being saved;
  - the tab is hidden.

  The reload is then owed and runs once, after the interaction. A load the calendar starts itself
  (navigation, the reload after its own save) counts as the owed reload.
- The client is optional. It goes quiet for the life of the view, with at most one
  `console.debug` line, when any of these happens:
  - the negotiate answers 4xx (an older server without the hub);
  - the method does not exist;
  - `Refused` arrives with `account_disabled`, or with `not_authenticated` or `revoked` after one
    reconnect.
- The client never refreshes the session, because the host's refresh signs the user out when it
  fails. Each connect sends the token the store holds at that moment.
- An ordinary drop restarts with back-off: 5 s, doubling, up to 5 min. After
  `too_many_connections` it waits at least 60 s. There is no safety poll.

## Server → client

Every payload is one JSON object with `"v": 1`. Fields that the server does not know are
**omitted**, not sent as null.

### `TaskChanged` (to the property's group)

```json
{"v":1,"property_id":"42","event_id":"7","plan_day_key":"2026-11-21","compliance_id":900,"kind":"changed"}
```

- `property_id` is always present.
- The key fields `event_id`, `plan_day_key` and `compliance_id` have the same types and meaning
  as gRPC `EventKey`.
- `kind` is one of these values:
  - `changed`: an occurrence was created or changed;
  - `removed`: an occurrence, or the whole task when only `event_id` is set, is gone for at
    least some callers;
  - `property`: anything in the property may have changed, and there is no key.
- Unknown values are treated as `changed`.

### `AssignmentsChanged` (to the worker's own site group)

```json
{"v":1}
```

The worker's property assignments or worker tags changed.

### `Refused` (to one connection, right before the server closes it)

```json
{"v":1,"code":"not_authenticated"}
```

`code` is one of the codes under "Connecting".

## How a client reacts

1. **`TaskChanged`**:
   - Debounce for about 1 s, then call `ListChanges(cursor)`. This is cheap when nothing changed
     for the caller.
   - If only one occurrence is on screen and the hint carries `event_id` + `plan_day_key`, the
     client may call `GetEvent(key)` instead.
   - Its own writes come back as hints too. The etag or version tells the client nothing changed.
2. **`AssignmentsChanged`**:
   - Call `ListChanges`. Its `properties` list is the new complete list.
   - Then `JoinProperty` the new properties and `LeaveProperty` the ones that are gone.
3. **Connect, reconnect, or app resume**:
   - `JoinProperty` every property from the last `ListChanges.properties`, then call
     `ListChanges`.
   - There are no sequence numbers. Any gap is closed by refetching.
4. **Keep a slow safety poll** (for example `ListChanges` every 5 min). Some writers are not
   announced (see "What is announced" below), and messages may be lost.

Installed apps ignore all of this. The 5-second `StreamEventChanges` is unchanged.

## What is announced

- **Writes in the web host** (calendar, task wizard, compliance, assignments, the mobile gRPC
  writes): right after the change is committed, after the request ends.
- **Writes of the background service** (its scheduled jobs and message handlers): through the
  change feed. The service appends a row to `TaskChangeFeedEntries`; the web host polls it about
  once a second and forwards each row as the same `TaskChanged` / `AssignmentsChanged` hints.
  - Expected delay: about 1-2 s after the service's commit. A row that becomes visible late (an
    id committed out of order) is caught by a 10 s window, so the delay is at most about 10 s.
  - A burst (a daily job writing hundreds of rows) is coalesced like a request: more than 5 tasks
    of one property become one `kind: property` hint. A reader that falls far behind sends one
    `kind: property` hint per affected property and continues from the newest row.
  - Nothing is replayed: rows written while no client was connected are not sent later (the
    client resyncs on connect anyway).
- **Not announced, left to the safety poll:** writes made by other plugins (for example the
  items-planning plugin's own UI), writes through other processes that do not append to the feed,
  a feed row visible more than 10 s late, and messages lost on a reconnect.
- **Capability:** there is no separate capability for the feed. `live_hub` stays the only one;
  a client keeps the same safety poll either way.

## Evolution

- Fields and `kind` values are added, never changed or removed.
- Clients ignore unknown fields and unknown server methods.
- A breaking change gets a new method name and a new capability string.

## Operational limits

- Keep-alive values: these are the SignalR defaults, and kanban runs on the same defaults.
  - The server pings every 15 s.
  - A client that is silent for 30 s is dropped.
  - The handshake must finish within 15 s.
  - Client timers: server timeout ≥ 30 s, keep-alive ≤ 15 s (the SignalR client defaults).
- Client → server messages are limited to 4 KB, and a connection runs one invocation at a time.
- These options apply to this hub only (`AddHubOptions<BackendConfigurationHub>`). Kanban's hub
  in the same host keeps its own.
- **No backplane.** Groups live in the memory of one web-host process.
  - This is correct for one web-host replica per customer (helm `replicaCount: 1`, no
    autoscaling).
  - During a rolling deploy two pods overlap briefly. Clients reconnect to the new pod and
    resync.
  - Running several replicas would need a backplane (for example Redis), or would have to route
    every publish through a shared feed.
  - The feed reader assumes it too: with two web-host replicas, each would forward every feed row
    to its own connections. Each connection still receives each hint once (it is on one replica);
    the only cost is that both replicas poll the table.
- **Token expiry.** The server records each connection's "valid until" instant.
  - That instant is the token's `exp` plus 5 minutes (`LiveHubAuthenticator.ClockSkew`, equal to
    JwtBearer's default `ClockSkew`, which the host does not override).
  - The bearer handler refuses a token without `exp`. If the server cannot read the expiry of a
    validated token, it accepts the connection with a cut-off of connect time + 24 h (the host's
    token lifetime) + 5 min, and logs one Warning per process.
  - The same instant is used at connect, on `JoinProperty` and in the publish sweep, so the hub
    accepts a token exactly as long as gRPC and REST do.
  - An expired connection gets no further hints. Every publish first ends the expired
    connections (taken out of every group, `Refused not_authenticated`, closed).
  - `JoinProperty` refuses and closes an expired connection too.
  - An idle connection that receives nothing may stay open past its expiry until one of these
    happens, but it receives nothing.
  - **The client must reconnect with a fresh token.** The simplest way is to refresh the token
    before `exp` and reconnect.
- **Connections per user:** at most 10.
- Logs carry connection, user and site ids only. They never contain an email or a token.

## Publishing (server side)

`ILiveNotifier` is a singleton in DI. It never throws, and it does nothing when no connection
joined the target group.

**Who receives a hint.** Group membership lives in one place: the in-memory connection registry.
The hub does not use SignalR groups.
- A hint is sent to the connection ids the registry lists as members of the group with a token
  that is still valid (`Clients.Clients(ids)`, read atomically).
- A connection the registry has taken out of a group (token expired, site revoked, property
  access revoked) therefore receives nothing from that moment on, including from a publish that
  runs while another call is still ending the connection.
- The cost is bounded by the group's member count.

- `TaskChangedAsync(propertyId, eventId?, planDay?, complianceId?, kind)`
- `AssignmentsChangedAsync(sdkSiteId)`
- `RevokePropertyAccessAsync(sdkSiteId, propertyId)`: takes the site's connections out of the
  property group.
- `RevokeSiteAsync(sdkSiteId)`: ends every connection of the site (taken out of every group,
  `Refused revoked`, close).
- `RevokePropertyForEveryoneAsync(propertyId)`: takes every connection, worker or web user, out
  of the property group. The connections stay open.
- `RevokeUserAsync(userId)`: ends every connection of the host user (`EformUser.Id`), whatever
  its site (`Refused revoked`, close).

Call them after the change has been committed.

**Before any publisher is added**, these server paths MUST call the revokes. Group membership is
checked only when a group is joined, so without these calls a connection keeps receiving hints
for access it has lost.

| Path | Call |
|---|---|
| A worker is unassigned from a property (PropertyWorker removed) | `RevokePropertyAccessAsync(site, property)`, then `AssignmentsChangedAsync(site)` |
| A property is deleted | `RevokePropertyAccessAsync(site, property)` for every site assigned to it, **and** `RevokePropertyForEveryoneAsync(property)` (web members have no PropertyWorker row). The interceptor does both: the PropertyWorker rows and the Property row are watched. |
| A worker or its site is removed | `RevokeSiteAsync(site)` |
| A worker resigns, or `EformUser.IsActive` is set to false | `RevokeSiteAsync(site)`, and `RevokeUserAsync(login)` where the plugin writes `IsActive = false` (resign) |
