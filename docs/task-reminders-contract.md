# Task reminders (mobile), contract version 1

A worker gets a push notification **10 minutes before each of today's tasks is planned to start**.
The web host plans the reminders (this plugin's `TaskReminderPlanner`); the background service
sends them through Firebase. This note is the contract between the server and the eForm app.

## Which tasks, who, when

- **Tasks:** today's open tasks **with a start time** (not all-day tasks), exactly the tasks the
  app's own task list shows for the worker (the same calendar read as `ListEvents` /
  `ListChanges`).
- **Today** is the property's local date. A property is in Danish time (`Europe/Copenhagen`)
  unless `Property.TimeZone` is set (an IANA id). Summer time is handled; times are stored in UTC.
- **When:** the reminder is due at planned start − 10 minutes. A task planned with less than
  10 minutes to go is announced at once; a task that has already started is not announced.
- **After the start:** a reminder already shown stays on the phone while the task is still open;
  it is withdrawn only when the task is finished, removed, moved or unassigned (owner decision).
- **Who:** every worker whose list shows the task, on **each of their phones that opted in**
  (below).
- **Changes:** finishing, removing, moving to another day or time, or unassigning a task cancels
  its pending reminder, or withdraws one already sent (a silent `task_withdrawn`, below). Moving
  it back plans it again. Renaming it updates a reminder not sent yet.

## Opt-in and capability

- `GetServerInfo.capabilities` contains `"task_notifications"` when the server plans reminders.
  `contract_version` is unchanged. It is advertised from the release that adds the planner
  (owner decision); reminders planned before the service's sender is deployed wait in the outbox
  and are sent, or expire at their start, once it is.
- The app opts in per install with `RegisterPushToken`:

  ```proto
  message RegisterPushTokenRequest {
    ...
    optional bool task_reminders = 5;
  }
  ```

  - `true`: this install receives task reminders. `false`: it does not (the app's push switch).
  - **Absent** (every older app): the stored choice is kept; a new install starts at `false`.
- **Old apps see nothing**: they never send the field, so they never get a task reminder, and the
  rest of `RegisterPushToken` behaves as before.

## Payloads (sent by the background service)

One FCM message per token. Ids are decimal strings, as on the gRPC and hub wires.

**Tag.** `tag = task:<event_id>:<plan_day_key>:<planned start in UTC as yyyyMMdd'T'HHmm'Z'>`,
for example `task:7:2026-11-23:20261123T0700Z` (at most 41 bytes). It is schedule-specific:
after a task is moved to another time, the withdrawal of the old reminder carries the old tag and
cannot remove the replacement. The same value is the Android notification tag, the APNs
collapse id and the `tag` data field of both payloads.

### `task_due` (visible), to every opted-in token of the worker

```
notification: { title: <task name>, body: "<local start time> · <property name>" }
data: {
  type: "task_due", v: "1",
  event_id: "<AreaRulePlanningId>", plan_day_key: "yyyy-MM-dd", property_id: "<PropertyId>",
  compliance_id: "<ComplianceId>"        // omitted when unknown
  planned_start_utc: "yyyy-MM-ddTHH:mm:ssZ",
  tag: "<tag>"
}
android: { priority: high, ttl: (planned start − now, minimum 1 s),
           notification: { channel_id: "tasks", tag: "<tag>" } }
apns: { headers: { apns-collapse-id: "<tag>", apns-expiration: "<planned start as epoch seconds>" },
        aps: { thread-id: "tasks" } }
```

- Transport expiry at the planned start: a reminder still undelivered then is dropped by
  FCM/APNs instead of arriving late (the server never sends one after the start either).
- The text is rendered on the server in the worker's language (the language of the worker's
  site, the same one the app's task titles use), with English as the fallback. Example: title
  `Feeding`, body `Starts at 07:00 · North farm`.
- On Android an unknown `channel_id` falls back to the app's default channel.

### `task_withdrawn` (silent), to every live eForm token of the worker, opted in or not

```
data: {
  type: "task_withdrawn", v: "1",
  event_id: "<AreaRulePlanningId>", plan_day_key: "yyyy-MM-dd",
  planned_start_utc: "yyyy-MM-ddTHH:mm:ssZ",
  tag: "<tag>"                            // the same tag as the reminder it removes
}
android: { priority: high, ttl: (planned start + 24 h − now, minimum 1 s) }
apns: { headers: { apns-push-type: "background", apns-priority: "5",
                   apns-expiration: "<planned start + 24 h as epoch seconds>" },
        aps: { content-available: 1 } }
```

### What the app does

- On `task_withdrawn`, cancel the shown notification whose tag equals `data.tag`. Best effort
  on iOS: silent pushes are throttled and do not reach an app the user closed.
- Never cancel by event or day alone: a rescheduled task has a different tag. A notification the
  app shows itself (in the foreground) must use `data.tag` as its tag, so a later
  `task_withdrawn` can find it.
- An install that opted out may still receive a `task_withdrawn` for a reminder sent before;
  apps that do not know the type ignore it.

The service's report ("Payloads (final)") is the source of these shapes.

## Privacy

What leaves the server to Google and Apple: the task name, the local start time, the property
name, ids and the planned start. Never: worker names, e-mails, comments or field values. Server
logs carry ids and counts only.

## Evolution

Fields and `type` values are added, never changed or removed. A client ignores unknown fields and
unknown types.
