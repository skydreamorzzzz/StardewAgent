using System.Text.Json.Serialization;

namespace StardewAgentBridge;

internal sealed record OperationRequest(
    [property: JsonPropertyName("operation_id")] Guid OperationId,
    [property: JsonPropertyName("actor_id")] string ActorId,
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("args")] Dictionary<string, object>? Args
);

internal sealed record OperationView(
    [property: JsonPropertyName("operation_id")] Guid OperationId,
    [property: JsonPropertyName("action_id")] string ActionId,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("outcome")] string? Outcome,
    [property: JsonPropertyName("error")] string? Error,
    [property: JsonPropertyName("submitted_tick")] ulong? SubmittedTick,
    [property: JsonPropertyName("settled_tick")] ulong? SettledTick
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
