from __future__ import annotations

from typing import Any, Protocol

from .models import Observation, OperationView


class GameAdapter(Protocol):
    """Actor-aware boundary between the harness and a game integration."""

    def health(self) -> dict[str, Any]: ...

    def observe(self) -> Observation: ...

    def submit_primitive(self, action_id: str) -> OperationView: ...

    def status(self, operation_id: str) -> OperationView: ...
