using System.Text.Json.Serialization;
using System.Text.Json;

namespace StardewAgentBridge;

internal sealed record OperationRequest(
    [property: JsonPropertyName("operation_id")] Guid OperationId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("args")] Dictionary<string, JsonElement>? Args,
    [property: JsonPropertyName("control_epoch")] int? ControlEpoch
);

internal sealed record OperationView(
    [property: JsonPropertyName("operation_id")] Guid OperationId,
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("submitted_tick")] ulong? SubmittedTick,
    [property: JsonPropertyName("settled_tick")] ulong? SettledTick,
    [property: JsonPropertyName("effect_status")] string? EffectStatus,
    [property: JsonPropertyName("quiescent")] bool? Quiescent,
    [property: JsonPropertyName("start_tile_x")] int? StartTileX,
    [property: JsonPropertyName("start_tile_y")] int? StartTileY,
    [property: JsonPropertyName("end_tile_x")] int? EndTileX,
    [property: JsonPropertyName("end_tile_y")] int? EndTileY,
    [property: JsonPropertyName("ticks_used")] ulong? TicksUsed,
    [property: JsonPropertyName("elapsed_ms")] long? ElapsedMs,
    [property: JsonPropertyName("postcondition")] string? Postcondition
);

internal sealed record ControlRequest(
    [property: JsonPropertyName("control_id")] Guid ControlId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("operation_id")] Guid? OperationId,
    [property: JsonPropertyName("new_control_epoch")] int? NewControlEpoch
);

internal sealed record ControlView(
    [property: JsonPropertyName("control_id")] Guid ControlId,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("control_epoch")] int ControlEpoch,
    [property: JsonPropertyName("dispatch_enabled")] bool DispatchEnabled,
    [property: JsonPropertyName("quiescent")] bool? Quiescent,
    [property: JsonPropertyName("affected_operation_ids")] IReadOnlyCollection<Guid> AffectedOperationIds
);

internal sealed record ObservationView(
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("world_ready")] bool WorldReady,
    [property: JsonPropertyName("year")] int? Year,
    [property: JsonPropertyName("season")] string? Season,
    [property: JsonPropertyName("day")] int? Day,
    [property: JsonPropertyName("time_of_day")] int? TimeOfDay,
    [property: JsonPropertyName("location")] string? Location,
    [property: JsonPropertyName("tile_x")] int? TileX,
    [property: JsonPropertyName("tile_y")] int? TileY,
    [property: JsonPropertyName("facing_direction")] int? FacingDirection,
    [property: JsonPropertyName("stamina")] float? Stamina,
    [property: JsonPropertyName("health")] int? Health,
    [property: JsonPropertyName("money")] int? Money,
    [property: JsonPropertyName("current_item")] string? CurrentItem,
    [property: JsonPropertyName("current_tool")] string? CurrentTool,
    [property: JsonPropertyName("active_menu")] string? ActiveMenu
);
