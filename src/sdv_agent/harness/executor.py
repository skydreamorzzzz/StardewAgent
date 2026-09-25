from __future__ import annotations

import time
from dataclasses import dataclass, replace

from .adapter import GameAdapter
from .models import ControlView, OperationView


@dataclass
class OperationExecutor:
    """Minimal operation lifecycle used by the verified primitive slice.

    This executor submits one immutable operation ID and only polls that same ID.
    It does not retry side effects or contain Stardew-specific business logic.
    """

    adapter: GameAdapter
    timeout_s: float = 2.0
    poll_s: float = 0.05

    def execute(self, action_id: str, args: dict[str, object] | None = None) -> OperationView:
        accepted = self.adapter.submit_operation(action_id, args)
        return self.wait(accepted.operation_id)

    def execute_primitive(self, action_id: str) -> OperationView:
        return self.execute(action_id)

    def cancel(self, operation_id: str) -> ControlView:
        return self.adapter.cancel(operation_id)

    def wait(self, operation_id: str) -> OperationView:
        deadline = time.monotonic() + self.timeout_s
        latest = self.adapter.status(operation_id)
        while latest.status not in {"settled", "rejected", "unknown"} and time.monotonic() < deadline:
            time.sleep(self.poll_s)
            latest = self.adapter.status(operation_id)
        if latest.status not in {"settled", "rejected", "unknown"}:
            # A harness timeout is an observation boundary, not permission to
            # retry a side effect. Request cancellation once, then surface the
            # result as UNKNOWN until the bridge can be checked independently.
            try:
                self.adapter.cancel(operation_id)
            except Exception:
                pass
            return replace(
                latest,
                status="unknown",
                outcome="unknown",
                error="EXECUTOR_TIMEOUT",
                effect_status="unknown",
                quiescent=None,
                postcondition="unknown",
            )
        return latest
