from __future__ import annotations

from dataclasses import dataclass

from sdv_agent.harness.executor import OperationExecutor
from sdv_agent.harness.models import OperationView


@dataclass
class PrimitiveTools:
    """The first primitive layer.

    These are intentionally below any LLM-facing capability. They only prove that
    the agent can cause bounded, player-equivalent input in the game.
    """

    executor: OperationExecutor

    def bounded_move(
        self,
        direction: str,
        *,
        max_ticks: int = 12,
        max_duration_ms: int = 1000,
    ) -> OperationView:
        spec = BoundedMoveSpec(direction, max_ticks, max_duration_ms)
        return self.executor.execute("movement.bounded", spec.as_args())

    def move_once(self, direction: str) -> OperationView:
        action = {
            "up": "input.move_up",
            "right": "input.move_right",
            "down": "input.move_down",
            "left": "input.move_left",
        }.get(direction.lower())
        if action is None:
            raise ValueError("direction must be one of: up, right, down, left")
        return self.executor.execute_primitive(action)

    def interact(self) -> OperationView:
        return self.executor.execute_primitive("input.action")

    def use_tool(self) -> OperationView:
        return self.executor.execute_primitive("input.use_tool")


@dataclass(frozen=True)
class BoundedMoveSpec:
    direction: str
    max_ticks: int
    max_duration_ms: int

    def __post_init__(self) -> None:
        direction = self.direction.lower()
        if direction not in {"up", "down", "left", "right"}:
            raise ValueError("direction must be one of: up, down, left, right")
        if not 1 <= self.max_ticks <= 120:
            raise ValueError("max_ticks must be between 1 and 120")
        if not 50 <= self.max_duration_ms <= 5000:
            raise ValueError("max_duration_ms must be between 50 and 5000")
        object.__setattr__(self, "direction", direction)

    def as_args(self) -> dict[str, object]:
        return {
            "direction": self.direction,
            "max_ticks": self.max_ticks,
            "max_duration_ms": self.max_duration_ms,
        }
