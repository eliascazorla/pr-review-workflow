from __future__ import annotations

import logging
from pathlib import Path

from ..diff_parser import ParsedDiff
from ..models import DiffAnalysis, Dimension, Finding, Severity, TokenUsage
from ..prompts import TESTS_COVERAGE_SYSTEM_PROMPT
from .base import BaseAgent


logger = logging.getLogger(__name__)


class TestsCoverageAgent(BaseAgent):
    name = "tests_coverage"

    def analyze(self, diff: ParsedDiff) -> DiffAnalysis:
        if self.llm and self.llm.available:
            logger.info("tests coverage agent starting in llm mode")
            try:
                return self._analyze_with_llm(diff)
            except Exception as exc:
                logger.warning("tests coverage agent fell back to heuristics after llm error: %s", exc)
                return self._analyze_with_heuristics(
                    diff,
                    fallback_reason=f"provider_error: {exc}",
                )

        logger.info("tests coverage agent using heuristics because llm is unavailable or disabled")
        return self._analyze_with_heuristics(diff, fallback_reason=self._heuristic_reason())

    def _analyze_with_llm(self, diff: ParsedDiff) -> DiffAnalysis:
        tool = {
            "type": "function",
            "function": {
                "name": "submit_tests_coverage_review",
                "description": "Return the test coverage review in structured form.",
                "parameters": {
                    "type": "object",
                    "properties": {
                        "summary": {"type": "string"},
                        "findings": {
                            "type": "array",
                            "items": {
                                "type": "object",
                                "properties": {
                                    "file": {"type": "string"},
                                    "line": {"type": "integer"},
                                    "message": {"type": "string"},
                                    "suggestion": {"type": "string"},
                                    "severity": {"type": "string", "enum": ["low", "medium", "high"]},
                                },
                                "required": ["file", "line", "message", "suggestion", "severity"],
                                "additionalProperties": False,
                            },
                        },
                        "confidence": {"type": "number"},
                    },
                    "required": ["summary", "findings", "confidence"],
                    "additionalProperties": False,
                },
            },
        }
        result = self.llm.call_tool(
            system_prompt=TESTS_COVERAGE_SYSTEM_PROMPT,
            user_prompt=(
                "Analyze this diff for missing or weak test coverage. "
                "Return only findings that can be tied to a specific file and line. "
                "Use the exact line numbers from the provided context.\n\n"
                f"{self._render_llm_context(diff)}"
            ),
            tool=tool,
        )
        payload = result.payload
        findings, discarded = self._parsed_findings(diff, payload.get("findings", []))
        return DiffAnalysis(
            dimension=Dimension.TESTS_COVERAGE,
            summary=str(payload.get("summary", "No summary provided.")),
            findings=findings,
            confidence=float(payload.get("confidence", 0.5)),
            discarded_findings=discarded,
            token_usage=result.usage,
            execution_mode="llm",
            final_execution_path="llm",
            llm_startup_state=self.llm_startup_state,
            fallback_reason="not_used",
            token_usage_source="provider",
        )

    def _heuristic_reason(self) -> str:
        if not self.llm or not self.llm.available:
            return self.default_fallback_reason
        return "provider_error"

    def _analyze_with_heuristics(self, diff: ParsedDiff, fallback_reason: str) -> DiffAnalysis:
        logger.info("tests coverage agent using heuristic path: %s", fallback_reason)
        code_files = [
            file
            for file in diff.files
            if file.path
            and file.path.endswith(".py")
            and not file.path.startswith("tests/")
            and not Path(file.path).name.startswith("test_")
        ]
        tests_changed = any(
            file.path.startswith("tests/")
            or Path(file.path).name.startswith("test_")
            or Path(file.path).name.endswith("_test.py")
            for file in diff.files
        )

        findings: list[Finding] = []
        if code_files and not tests_changed:
            for file in code_files:
                if not file.added_lines:
                    continue
                line_number = file.added_lines[0].number
                findings.append(
                    Finding(
                        file=file.path,
                        line=line_number,
                        message="Code changed without a corresponding test update.",
                        suggestion="Add or update tests that cover the modified behavior and edge cases.",
                        severity=Severity.HIGH,
                    )
                )

        summary = (
            "No obvious test coverage gap was detected."
            if not findings
            else "The diff changes code but does not appear to update tests accordingly."
        )
        return DiffAnalysis(
            dimension=Dimension.TESTS_COVERAGE,
            summary=summary,
            findings=findings,
            confidence=0.7 if findings else 0.55,
            token_usage=TokenUsage(),
            execution_mode="heuristic",
            final_execution_path="heuristic",
            llm_startup_state=self.llm_startup_state,
            fallback_reason=fallback_reason,
            token_usage_source="none",
        )

    def _finding_from_payload(self, diff: ParsedDiff, item: object) -> Finding | None:
        data = item if isinstance(item, dict) else {}
        file = str(data.get("file", ""))
        line = int(data.get("line", 0))
        resolved_line = self._resolve_line(diff, file, line)
        if resolved_line is None:
            return None
        return Finding(
            file=file,
            line=resolved_line,
            message=str(data.get("message", "")),
            suggestion=str(data.get("suggestion", "")),
            severity=Severity(str(data.get("severity", "medium"))),
        )

    def _parsed_findings(self, diff: ParsedDiff, items: object) -> tuple[list[Finding], int]:
        findings: list[Finding] = []
        discarded = 0
        for item in items if isinstance(items, list) else []:
            finding = self._finding_from_payload(diff, item)
            if finding is None:
                discarded += 1
            else:
                findings.append(finding)
        return findings, discarded
