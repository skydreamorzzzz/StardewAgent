from __future__ import annotations

from dataclasses import dataclass
from typing import Any


@dataclass(frozen=True)
class Observation:
    actor_id: str
    world_ready: bool
    year: int | None = None
    season: str | None = None
    day: int | None = None
    time_of_day: int | None = None
    location: str | None = None
    tile_x: int | None = None
    tile_y: int | None = None
    facing_direction: int | None = None
    stamina: float | None = None
    health: int | None = None
    money: int | None = None
    current_item: str | None = None
    current_tool: str | None = None
    active_menu: str | None = None
    raw: dict[str, Any] | None = None


@dataclass(frozen=True)
class OperationView:
    operation_id: str
    action_id: str
    status: str
    outcome: str | None
    error: str | None
    submitted_tick: int | None
    settled_tick: int | None
    raw: dict[str, Any]
    effect_status: str | None = None
    quiescent: bool | None = None
    start_tile_x: int | None = None
    start_tile_y: int | None = None
    end_tile_x: int | None = None
    end_tile_y: int | None = None
    ticks_used: int | None = None
    elapsed_ms: int | None = None
    postcondition: str | None = None


@dataclass(frozen=True)
class ControlView:
    control_id: str
    kind: str
    control_epoch: int
    dispatch_enabled: bool
    quiescent: bool | None
    affected_operation_ids: list[str]
    raw: dict[str, Any]
