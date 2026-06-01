from __future__ import annotations

import logging
import re

from ..diff_parser import ParsedDiff
from ..models import DiffAnalysis, Dimension, Finding, Severity, TokenUsage
from ..prompts import QUALITY_SYSTEM_PROMPT
from .base import BaseAgent


logger = logging.getLogger(__name__)


class QualityAgent(BaseAgent):
    name = "quality"

    def analyze(self, diff: ParsedDiff) -> DiffAnalysis:
        if self.llm and self.llm.available:
            logger.info("quality agent starting in llm mode")
            try:
                return self._analyze_with_llm(diff)
            except Exception as exc:
                logger.warning("quality agent fell back to heuristics after llm error: %s", exc)
                return self._analyze_with_heuristics(
                    diff,
                    fallback_reason=f"provider_error: {exc}",
                )

        logger.info("quality agent using heuristics because llm is unavailable or disabled")
        return self._analyze_with_heuristics(diff, fallback_reason=self._heuristic_reason())

    def _analyze_with_llm(self, diff: ParsedDiff) -> DiffAnalysis:
        tool = {
            "type": "function",
            "function": {
                "name": "submit_quality_review",
                "description": "Return the code quality review in structured form.",
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
        
        # Format system prompt with context variables from WorkflowContext
        system_prompt = QUALITY_SYSTEM_PROMPT.format(
            review_id=self.context.get("review_id", "unknown"),
            repo_owner=self.context.get("repo_owner", "unknown"),
            repo_name=self.context.get("repo_name", "unknown"),
            pr_number=self.context.get("pr_number", "unknown"),
        )
        
        # Build analysis summary from code_analyzer context
        analysis_summary = ""
        code_analysis = self.context.get("code_analysis")
        if code_analysis:
            complexity_score = code_analysis.get("complexity_score", 0)
            security_issues_length = len(code_analysis.get("security_issues", []))
            patterns_found = code_analysis.get("patterns_found", [])
            patterns_str = ", ".join(patterns_found) if patterns_found else "None"
            tech_debt_length = len(code_analysis.get("tech_debt", []))
            analysis_summary = f"""
Previous Code Analysis:
- Complexity: {complexity_score}/10
- Security Issues: {security_issues_length}
- Patterns Found: {patterns_str}
- Tech Debt Items: {tech_debt_length}
"""
        
        # Format user_prompt to match TypeScript userMessage format
        repo_owner = self.context.get("repo_owner", "unknown")
        repo_name = self.context.get("repo_name", "unknown")
        title = self.context.get("title", "PR")
        
        user_prompt = f"""Evaluate the code quality for this PR:

Repository: {repo_owner}/{repo_name}
PR Title: {title}
{analysis_summary}

Provide quality metrics including readability score (0-10), test coverage estimate (0-100), performance concerns, and overall quality score (0-10)."""
        
        result = self.llm.call_tool(
            system_prompt=system_prompt,
            user_prompt=user_prompt,
            tool=tool,
        )
        payload = result.payload
        findings, discarded = self._parsed_findings(diff, payload.get("findings", []))
        return DiffAnalysis(
            dimension=Dimension.QUALITY,
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
        logger.info("quality agent using heuristic path: %s", fallback_reason)
        findings: list[Finding] = []

        for file in diff.files:
            for index, line in enumerate(file.added_lines):
                lower = line.content.lower()
                if re.search(r"\btmp\b|\bfoo\b|\bbar\b|\bdata1\b", lower):
                    findings.append(
                        Finding(
                            file=file.path,
                            line=line.number,
                            message="Generic or temporary names reduce readability.",
                            suggestion="Rename the variable so it clearly describes its purpose.",
                            severity=Severity.LOW,
                        )
                    )

                if "duplicate" in lower or "copy" in lower:
                    findings.append(
                        Finding(
                            file=file.path,
                            line=line.number,
                            message="There are signs of duplicated logic or copied code.",
                            suggestion="Extract the repeated logic into a shared helper or function.",
                            severity=Severity.MEDIUM,
                        )
                    )

                if "todo" in lower or "fixme" in lower:
                    findings.append(
                        Finding(
                            file=file.path,
                            line=line.number,
                            message="Technical debt was marked with TODO/FIXME.",
                            suggestion="Resolve the item before merge or create a tracked issue.",
                            severity=Severity.LOW,
                        )
                    )

                if lower.strip().startswith("for ") and sum(1 for item in file.added_lines if "for " in item.content.lower()) > 1:
                    findings.append(
                        Finding(
                            file=file.path,
                            line=line.number,
                            message="The iterative logic appears nested and can grow in complexity.",
                            suggestion="Simplify the loops or extract the inner logic into a helper function.",
                            severity=Severity.MEDIUM,
                        )
                    )

        summary = (
            "No clear code quality issues were detected."
            if not findings
            else "Signals for code quality improvement were detected."
        )
        return DiffAnalysis(
            dimension=Dimension.QUALITY,
            summary=summary,
            findings=findings,
            confidence=0.72 if findings else 0.54,
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
