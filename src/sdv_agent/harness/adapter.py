from __future__ import annotations

from typing import Any, Protocol

from .models import ControlView, Observation, OperationView


class GameAdapter(Protocol):
    """Actor-aware boundary between the harness and a game integration."""

    def health(self) -> dict[str, Any]: ...

    def observe(self) -> Observation: ...

    def submit_operation(
        self,
        operation_id: str,
        action_id: str,
        args: dict[str, Any] | None = None,
    ) -> OperationView: ...

    def status(self, operation_id: str) -> OperationView: ...

    def cancel(self, operation_id: str) -> ControlView: ...

    def pause(self) -> ControlView: ...

    def enable(self) -> ControlView: ...
