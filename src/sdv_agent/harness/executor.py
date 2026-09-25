from __future__ import annotations

import time
import uuid
from dataclasses import dataclass, replace

from .adapter import GameAdapter
from .models import ControlView, OperationView


_TERMINAL_STATUSES = {"settled", "rejected", "unknown"}


@dataclass
class OperationExecutor:
    """Minimal reliable operation lifecycle for primitive execution.

    The operation ID is fixed before dispatch. A lost submit acknowledgement is
    reconciled against that same ID; this executor never retries a side effect
    under a fresh operation ID.
    """

    adapter: GameAdapter
    timeout_s: float = 2.0
    poll_s: float = 0.05

    def execute(self, action_id: str, args: dict[str, object] | None = None) -> OperationView:
        operation_id = str(uuid.uuid4())
        try:
            accepted = self.adapter.submit_operation(operation_id, action_id, args)
        except Exception:
            return self._poll_until_terminal(
                operation_id,
                action_id,
                time.monotonic() + self.timeout_s,
                initial=None,
                timeout_error="SUBMIT_ACK_UNKNOWN",
            )

        return self._poll_until_terminal(
            operation_id,
            action_id,
            time.monotonic() + self.timeout_s,
            initial=accepted,
            timeout_error="EXECUTOR_TIMEOUT",
        )

    def execute_primitive(self, action_id: str) -> OperationView:
        return self.execute(action_id)

    def cancel(self, operation_id: str) -> ControlView:
        return self.adapter.cancel(operation_id)

    def wait(self, operation_id: str) -> OperationView:
        return self._poll_until_terminal(
            operation_id,
            "",
            time.monotonic() + self.timeout_s,
            initial=None,
            timeout_error="EXECUTOR_TIMEOUT",
        )

    def _poll_until_terminal(
        self,
        operation_id: str,
        action_id: str,
        deadline: float,
        *,
        initial: OperationView | None,
        timeout_error: str,
    ) -> OperationView:
        latest = initial

        while time.monotonic() < deadline:
            if latest is not None and latest.status in _TERMINAL_STATUSES:
                return latest

            try:
                latest = self.adapter.status(operation_id)
            except Exception:
                if self.poll_s > 0:
                    time.sleep(self.poll_s)
                continue

            if latest.status in _TERMINAL_STATUSES:
                return latest
            if self.poll_s > 0:
                time.sleep(self.poll_s)

        try:
            self.adapter.cancel(operation_id)
        except Exception:
            pass

        if latest is None:
            latest = OperationView(
                operation_id=operation_id,
                action_id=action_id,
                status="unknown",
                outcome="unknown",
                error=timeout_error,
                submitted_tick=None,
                settled_tick=None,
                raw={},
            )

        return replace(
            latest,
            status="unknown",
            outcome="unknown",
            error=timeout_error,
            effect_status="unknown",
            quiescent=None,
            postcondition="unknown",
        )
