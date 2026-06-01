from __future__ import annotations

from abc import ABC, abstractmethod

from ..llm import OpenAICompatibleClient
from ..diff_parser import ParsedDiff
from ..models import DiffAnalysis, DiffFile


class BaseAgent(ABC):
    name: str

    def __init__(
        self,
        llm: OpenAICompatibleClient | None = None,
        llm_startup_state: str = "disabled",
        default_fallback_reason: str = "missing_secret",
    ) -> None:
        self.llm = llm
        self.llm_startup_state = llm_startup_state
        self.default_fallback_reason = default_fallback_reason

    def _resolve_line(self, diff: ParsedDiff, file_path: str, requested_line: int) -> int | None:
        added_lines = [
            line.number
            for file in diff.files
            if file.path == file_path
            for line in file.added_lines
        ]
        if not added_lines:
            return None
        if requested_line in added_lines:
            return requested_line
        return None

    def _render_file_context(self, file: DiffFile) -> str:
        lines = [f"FILE: {file.path}"]
        if file.added_lines:
            lines.append("Added lines with exact line numbers:")
            for line in file.added_lines:
                snippet = line.content.rstrip()
                lines.append(f"  {line.number}: {snippet}")
        if file.hunks:
            lines.append("Raw hunks:")
            for hunk in file.hunks:
                lines.append(hunk)
                lines.append("")
        return "\n".join(lines).rstrip()

    def _render_llm_context(self, diff: ParsedDiff) -> str:
        parts = [
            "Use only exact file and line numbers from the added lines listed below.",
            "Do not guess approximate lines. If a finding cannot be tied to an exact added line, omit it.",
            "",
        ]
        for file in diff.files:
            if not file.path:
                continue
            if not file.added_lines and not file.hunks:
                continue
            parts.append(self._render_file_context(file))
            parts.append("")
        parts.append("RAW DIFF:")
        parts.append(diff.raw)
        return "\n".join(parts).rstrip()

    @abstractmethod
    def analyze(self, diff: ParsedDiff) -> DiffAnalysis:
        raise NotImplementedError
