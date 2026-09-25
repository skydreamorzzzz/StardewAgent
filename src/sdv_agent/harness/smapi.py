from __future__ import annotations

import json
import os
import urllib.error
import urllib.request
import uuid
from dataclasses import dataclass, field
from typing import Any

from .models import Observation, OperationView


class BridgeError(RuntimeError):
    pass


@dataclass(frozen=True)
class SmapiConfig:
    base_url: str = "http://127.0.0.1:8765"
    token: str = field(default_factory=lambda: os.environ.get("STARDEW_AGENT_TOKEN", ""))
    actor_id: str = "agent_player"
    timeout_s: float = 2.0


class SmapiAdapter:
    """Thin client for the public SMAPI bridge.

    This is intentionally *not* the Planner-facing API. It is the harness boundary.
    """

    def __init__(self, config: SmapiConfig | None = None) -> None:
        self.config = config or SmapiConfig()

    def _request(self, method: str, path: str, payload: dict[str, Any] | None = None) -> dict[str, Any]:
        if not self.config.token:
            raise BridgeError("STARDEW_AGENT_TOKEN is not set")
        url = self.config.base_url.rstrip("/") + path
        data = None if payload is None else json.dumps(payload).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=data,
            method=method,
            headers={
                "Accept": "application/json",
                "Content-Type": "application/json",
                "X-Stardew-Agent-Token": self.config.token,
            },
        )
        try:
            with urllib.request.urlopen(req, timeout=self.config.timeout_s) as resp:
                body = resp.read().decode("utf-8")
                return json.loads(body) if body else {}
        except urllib.error.HTTPError as exc:
            body = exc.read().decode("utf-8", errors="replace")
            raise BridgeError(f"HTTP {exc.code}: {body}") from exc
        except OSError as exc:
            raise BridgeError(f"Bridge unavailable at {url}: {exc}") from exc

    def health(self) -> dict[str, Any]:
        return self._request("GET", "/health")

    def observe(self) -> Observation:
        raw = self._request("GET", f"/observe?actor_id={self.config.actor_id}")
        return Observation(
            actor_id=raw.get("actor_id", self.config.actor_id),
            world_ready=bool(raw.get("world_ready", False)),
            year=raw.get("year"),
            season=raw.get("season"),
            day=raw.get("day"),
            time_of_day=raw.get("time_of_day"),
            location=raw.get("location"),
            tile_x=raw.get("tile_x"),
            tile_y=raw.get("tile_y"),
            facing_direction=raw.get("facing_direction"),
            stamina=raw.get("stamina"),
            health=raw.get("health"),
            money=raw.get("money"),
            current_item=raw.get("current_item"),
            current_tool=raw.get("current_tool"),
            active_menu=raw.get("active_menu"),
            raw=raw,
        )

    def submit_primitive(self, action_id: str) -> OperationView:
        operation_id = str(uuid.uuid4())
        raw = self._request(
            "POST",
            "/operation",
            {
                "operation_id": operation_id,
                "actor_id": self.config.actor_id,
                "action_id": action_id,
                "args": {},
            },
        )
        return self._to_operation(raw)

    def status(self, operation_id: str) -> OperationView:
        raw = self._request("GET", f"/operation/{operation_id}")
        return self._to_operation(raw)

    @staticmethod
    def _to_operation(raw: dict[str, Any]) -> OperationView:
        return OperationView(
            operation_id=raw["operation_id"],
            action_id=raw.get("action_id", ""),
            status=raw.get("status", "unknown"),
            outcome=raw.get("outcome"),
            error=raw.get("error"),
            submitted_tick=raw.get("submitted_tick"),
            settled_tick=raw.get("settled_tick"),
            raw=raw,
        )
