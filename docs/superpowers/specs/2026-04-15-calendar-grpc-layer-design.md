# Backend-Configuration gRPC Layer for Calendar + Properties (Flutter)

## Context

The Flutter mobile app needs to consume 3 existing backend-configuration REST
endpoints over gRPC so it can use generated models and streaming-friendly
transport instead of JSON. The endpoints must also enforce a property-worker
scoping rule: a user (identified by SDK site id) can only see properties they
are assigned to via `PropertyWorker`, and can only query tasks/boards for those
properties.

Endpoints to expose:

1. `POST /api/backend-configuration-pn/calendar/tasks/week`
2. `GET /api/backend-configuration-pn/properties/dictionary?fullNames=false`
3. `GET /api/backend-configuration-pn/calendar/boards/{propertyId}`

The pattern to mirror is the existing gRPC layer in
`eform-angular-timeplanning-plugin` (server-side only, C# + `.proto`, consumed
by the same Flutter client).

User decisions (captured):
- **Server-side only** (no Angular client work). Flutter consumes it.
- **Keep `propertyId` on tasks/week and boards**; server rejects with
  `PERMISSION_DENIED` when the caller's site id has no `PropertyWorker` row for
  that property.
- **Properties dictionary** naturally returns only the caller's accessible
  properties (no `propertyId` input).

## Files to modify

All paths are relative to
`/home/rene/Documents/workspace/microting/eform-backendconfiguration-plugin/eFormAPI/Plugins/BackendConfiguration.Pn/BackendConfiguration.Pn/`.

### New files

| File | Purpose |
|------|---------|
| `Protos/common.proto` | Shared messages: `OperationResult`, `Empty`. Matches timeplanning's `common.proto`. |
| `Protos/calendar.proto` | `BackendConfigurationCalendarService` with `GetTasksForWeek` + `GetBoards` RPCs, plus their request/response/item messages. |
| `Protos/properties.proto` | `BackendConfigurationPropertiesService` with `GetCommonDictionary` RPC and its messages. |
| `Services/GrpcServices/CalendarGrpcService.cs` | Server implementation of `BackendConfigurationCalendarService.BackendConfigurationCalendarServiceBase`. Injects `IBackendConfigurationCalendarService` + the new access-check helper. |
| `Services/GrpcServices/PropertiesGrpcService.cs` | Server implementation of `BackendConfigurationPropertiesService.BackendConfigurationPropertiesServiceBase`. Injects `IBackendConfigurationPropertiesService` + the helper. |
| `Services/UserPropertyAccess/IBackendConfigurationUserPropertyAccess.cs` | Interface for the access helper. |
| `Services/UserPropertyAccess/BackendConfigurationUserPropertyAccess.cs` | Implementation — given `sdkSiteId`, returns `List<int>` of property ids where the caller has a non-removed `PropertyWorker` row, and a `HasAccessAsync(sdkSiteId, propertyId)` check. |

### Modified files

| File | Change |
|------|--------|
| `BackendConfiguration.Pn.csproj` | Add `Grpc.AspNetCore 2.76.0`, `Google.Protobuf 3.34.1`, and `<Protobuf Include="Protos/*.proto" GrpcServices="Server" ProtoRoot="Protos" />` items. Match versions used in `TimePlanning.Pn.csproj` (lines 35, 39-47, 50). |
| `EformBackendConfigurationPlugin.cs` | In `ConfigureServices`: `services.AddGrpc();` plus DI registrations for `IBackendConfigurationUserPropertyAccess` and the 2 gRPC services. In `Configure` (or the endpoint mapper used by the host): `endpoints.MapGrpcService<CalendarGrpcService>();` and `endpoints.MapGrpcService<PropertiesGrpcService>();`. Mirror `EformTimePlanningPlugin.cs` lines 95 and 215-224. |

## Proto design (high level)

### `common.proto`
```proto
syntax = "proto3";
package backend_configuration;
option csharp_namespace = "BackendConfiguration.Pn.Grpc";

message OperationResult { bool success = 1; string message = 2; }
message Empty {}
```

### `properties.proto` — replaces `GET /properties/dictionary`
```proto
service BackendConfigurationPropertiesService {
  rpc GetCommonDictionary(GetCommonDictionaryRequest) returns (GetCommonDictionaryResponse);
}

message GetCommonDictionaryRequest {
  int32 sdk_site_id = 1;
  bool full_names = 2;
}

message CommonDictionaryItem {
  int32 id = 1;
  string name = 2;
  string description = 3;
}

message GetCommonDictionaryResponse {
  bool success = 1;
  string message = 2;
  repeated CommonDictionaryItem items = 3;
}
```

### `calendar.proto` — replaces tasks/week + boards
```proto
service BackendConfigurationCalendarService {
  rpc GetTasksForWeek(GetTasksForWeekRequest) returns (GetTasksForWeekResponse);
  rpc GetBoards(GetBoardsRequest) returns (GetBoardsResponse);
}

message GetTasksForWeekRequest {
  int32 sdk_site_id = 1;
  int32 property_id = 2;
  string week_start = 3;               // ISO-8601 yyyy-MM-dd
  string week_end = 4;                 // ISO-8601 yyyy-MM-dd
  repeated int32 board_ids = 5;
  repeated string tag_names = 6;
  repeated int32 site_ids = 7;         // optional assignee filter
}

message CalendarTaskItem {             // mirrors CalendarTaskResponseModel
  int32 id = 1;
  string title = 2;
  int32 start_hour = 3;
  int32 duration = 4;
  string task_date = 5;                // ISO-8601
  repeated string tags = 6;
  repeated int32 assignee_ids = 7;
  int32 board_id = 8;
  string color = 9;
  string repeat_type = 10;
  int32 repeat_every = 11;
  bool completed = 12;
  int32 property_id = 13;
  int32 compliance_id = 14;
  bool is_from_compliance = 15;
  string deadline = 16;                // ISO-8601 (nullable via empty string)
  string next_execution_time = 17;     // ISO-8601
  int32 planning_id = 18;
  bool is_all_day = 19;
  int32 exception_id = 20;
}

message GetTasksForWeekResponse {
  bool success = 1;
  string message = 2;
  repeated CalendarTaskItem tasks = 3;
}

message GetBoardsRequest {
  int32 sdk_site_id = 1;
  int32 property_id = 2;
}

message CalendarBoardItem {            // mirrors CalendarBoardModel
  int32 id = 1;
  string name = 2;
  string color = 3;
  int32 property_id = 4;
}

message GetBoardsResponse {
  bool success = 1;
  string message = 2;
  repeated CalendarBoardItem boards = 3;
}
```

Field ordering in `CalendarTaskItem` must match the REST DTO at
`Infrastructure/Models/Calendar/CalendarTaskResponseModel.cs`. Same for
`CalendarBoardItem` vs `CalendarBoardModel.cs`. Keep field numbers stable once
shipped (never renumber).

## User-scoping helper — `BackendConfigurationUserPropertyAccess`

Reuses the existing query pattern found in
`Services/BackendConfigurationAreaRulePlanningsService/BackendConfigurationAreaRulePlanningsService.cs`
lines 144-147 and
`Services/BackendConfigurationAssignmentWorkerService/BackendConfigurationAssignmentWorkerService.cs`
lines 486-489:

```csharp
var propertyIds = await _dbContext.PropertyWorkers
    .Where(x => x.WorkerId == sdkSiteId
             && x.WorkflowState != Constants.WorkflowStates.Removed)
    .Select(x => x.PropertyId)
    .ToListAsync();
```

Interface:
```csharp
public interface IBackendConfigurationUserPropertyAccess
{
    Task<List<int>> GetAccessiblePropertyIdsAsync(int sdkSiteId);
    Task<bool> HasAccessAsync(int sdkSiteId, int propertyId);
}
```

Registered as `Transient`. The `sdkSiteId` comes from the gRPC request message
(same pattern as timeplanning's `request.SdkSiteId` in
`TimePlanningWorkingHoursGrpcService.cs` line 20). This is consistent with how
the Flutter app already authenticates in timeplanning.

## gRPC service method wiring

### `PropertiesGrpcService.GetCommonDictionary`
1. Call `IBackendConfigurationPropertiesService.GetCommonDictionary(request.FullNames)` — existing method at `Services/BackendConfigurationPropertiesService/BackendConfigurationPropertiesService.cs` lines 482-504.
2. Intersect the returned list with `GetAccessiblePropertyIdsAsync(request.SdkSiteId)` (match on `CommonDictionaryModel.Id`).
3. Map to `CommonDictionaryItem` and return.

### `CalendarGrpcService.GetTasksForWeek`
1. If `!await HasAccessAsync(request.SdkSiteId, request.PropertyId)` → throw `RpcException(new Status(StatusCode.PermissionDenied, "No access to property"))`.
2. Build a `CalendarTaskRequestModel` from the request fields (parse `week_start` / `week_end` with `DateTime.ParseExact(..., "yyyy-MM-dd", CultureInfo.InvariantCulture)`; empty string → null for optional assignee filter).
3. Call `IBackendConfigurationCalendarService.GetTasksForWeek(model)` — existing method at `Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs` lines 34-361.
4. On `OperationDataResult.Success`, project each `CalendarTaskResponseModel` to `CalendarTaskItem` (use empty strings for null DateTimes).
5. On failure, return a response with `success = false` + `message = result.Message`.

### `CalendarGrpcService.GetBoards`
1. Access check as above.
2. Call `IBackendConfigurationCalendarService.GetBoards(request.PropertyId)` — existing method at `Services/BackendConfigurationCalendarService/BackendConfigurationCalendarService.cs` lines 846-891.
3. Project `List<CalendarBoardModel>` → `repeated CalendarBoardItem`.

All 3 methods follow the timeplanning "thin wrapper" pattern: `GrpcService →
existing business service → convert DTOs`. No new business logic.

## Registration (EformBackendConfigurationPlugin.cs)

Mirror `EformTimePlanningPlugin.cs`:

**In `ConfigureServices`** (next to other `services.AddTransient<...>` calls):
```csharp
services.AddGrpc();
services.AddTransient<IBackendConfigurationUserPropertyAccess, BackendConfigurationUserPropertyAccess>();
// No separate DI needed for the gRPC service classes — AddGrpc + MapGrpcService handles them.
```

**In `Configure`** (alongside existing endpoint wiring):
```csharp
endpoints.MapGrpcService<CalendarGrpcService>();
endpoints.MapGrpcService<PropertiesGrpcService>();
```

## Naming conventions to follow (from timeplanning)

- Proto package: `backend_configuration` (snake_case).
- C# namespace: `BackendConfiguration.Pn.Grpc`.
- Service names in proto: `BackendConfigurationCalendarService`, `BackendConfigurationPropertiesService`.
- Implementation class suffix: `GrpcService` (e.g. `CalendarGrpcService`).
- Field names in proto: `snake_case`; messages: `PascalCase`.

## Verification

1. **Build** — from the plugin root: `dotnet build BackendConfiguration.Pn.sln`. Expect protoc to generate C# stubs under `obj/Debug/net*/Protos/` with no errors.
2. **Unit test**: add a focused test for `BackendConfigurationUserPropertyAccess.GetAccessiblePropertyIdsAsync` seeding 2 properties + 1 PropertyWorker row for site 7; call with `sdkSiteId=7` and assert exactly 1 property id returned, then call with `sdkSiteId=99` and assert empty list.
3. **Manual gRPC smoke** — start the eFormAPI host, use `grpcurl` against `localhost:<port>`:
   ```
   grpcurl -plaintext -d '{"sdk_site_id":<known>,"full_names":false}' \
     localhost:<port> backend_configuration.BackendConfigurationPropertiesService/GetCommonDictionary
   ```
   Expect only properties that seed `PropertyWorker` with the same site id.
4. **Access-denied check** — call `GetBoards` with a `property_id` the site is not assigned to; expect `Code: PermissionDenied`.
5. **Parity check against REST** — for an accessible property, call the gRPC `GetTasksForWeek` and the REST `POST /calendar/tasks/week` with equivalent payloads; response task counts and fields must match.

## Out of scope

- Angular frontend changes (separate effort; REST remains for the web client).
- Any new business logic in calendar/properties services (just wrapping).
- Proto changes for endpoints we're not exposing (tags, employees, teams, etc.) — follow-up if Flutter needs them.
- Authentication/token changes — inherits the same ASP.NET Core middleware that timeplanning's gRPC services already rely on.

## Build sequence

1. Create the 3 proto files.
2. Add the two `PackageReference`s and `Protobuf` items to the csproj; `dotnet build` to verify stubs generate.
3. Create the access-helper interface + implementation + unit test.
4. Create the two `GrpcService` implementations.
5. Wire `services.AddGrpc()`, DI for the helper, and `MapGrpcService<>()` in `EformBackendConfigurationPlugin.cs`.
6. Manual smoke with `grpcurl` (step 3 + 4 of Verification).
7. Commit on a new feat branch `feat/calendar-grpc` off `stable`; PR to `stable`.
