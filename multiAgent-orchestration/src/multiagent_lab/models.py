from __future__ import annotations

from dataclasses import dataclass, field
from enum import Enum


class Severity(str, Enum):
    LOW = "low"
    MEDIUM = "medium"
    HIGH = "high"


class Dimension(str, Enum):
    QUALITY = "quality"
    SECURITY = "security"
    TESTS_COVERAGE = "tests_coverage"


@dataclass(slots=True)
class DiffLine:
    number: int
    content: str
    hunk: str = ""


@dataclass(slots=True)
class TokenUsage:
    prompt_tokens: int = 0
    completion_tokens: int = 0
    total_tokens: int = 0

    @classmethod
    def from_api_payload(cls, payload: object) -> "TokenUsage":
        data = payload if isinstance(payload, dict) else {}

        def _to_int(value: object) -> int:
            try:
                return int(value)
            except (TypeError, ValueError):
                return 0

        prompt_tokens = _to_int(data.get("prompt_tokens", 0))
        completion_tokens = _to_int(data.get("completion_tokens", 0))
        total_tokens = _to_int(data.get("total_tokens", 0))
        if total_tokens == 0 and (prompt_tokens or completion_tokens):
            total_tokens = prompt_tokens + completion_tokens
        return cls(
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            total_tokens=total_tokens,
        )

    @property
    def is_zero(self) -> bool:
        return self.prompt_tokens == 0 and self.completion_tokens == 0 and self.total_tokens == 0

    def __add__(self, other: object) -> "TokenUsage":
        if not isinstance(other, TokenUsage):
            return NotImplemented
        return TokenUsage(
            prompt_tokens=self.prompt_tokens + other.prompt_tokens,
            completion_tokens=self.completion_tokens + other.completion_tokens,
            total_tokens=self.total_tokens + other.total_tokens,
        )

    def __iadd__(self, other: object) -> "TokenUsage":
        if not isinstance(other, TokenUsage):
            return NotImplemented
        self.prompt_tokens += other.prompt_tokens
        self.completion_tokens += other.completion_tokens
        self.total_tokens += other.total_tokens
        return self


@dataclass(slots=True)
class DiffFile:
    path: str
    additions: int = 0
    deletions: int = 0
    hunks: list[str] = field(default_factory=list)
    added_lines: list[DiffLine] = field(default_factory=list)
    patch: str = ""


@dataclass(slots=True)
class Finding:
    file: str
    line: int
    message: str
    suggestion: str
    severity: Severity = Severity.MEDIUM


@dataclass(slots=True)
class DiffAnalysis:
    dimension: Dimension
    summary: str
    findings: list[Finding] = field(default_factory=list)
    confidence: float = 0.0
    discarded_findings: int = 0
    token_usage: TokenUsage = field(default_factory=TokenUsage)
    execution_mode: str = "heuristic"
    final_execution_path: str = "heuristic"
    llm_startup_state: str = "disabled"
    fallback_reason: str = "missing_secret"
    token_usage_source: str = "none"


@dataclass(slots=True)
class SupervisorDecision:
    invoked_agents: list[Dimension] = field(default_factory=list)
    rationale: str = ""
    token_usage: TokenUsage = field(default_factory=TokenUsage)
    execution_mode: str = "heuristic"
    final_execution_path: str = "heuristic"
    llm_startup_state: str = "disabled"
    fallback_reason: str = "missing_secret"
    token_usage_source: str = "none"


@dataclass(slots=True)
class ConsolidatedReport:
    title: str
    overview: str
    decisions: SupervisorDecision
    analyses: list[DiffAnalysis] = field(default_factory=list)
    coordination: "CoordinationMetrics | None" = None
    resolved_labels: list[str] = field(default_factory=list)


@dataclass(slots=True)
class CoordinationMetrics:
    total_agents: int
    invoked_agents: int
    analyses: int
    findings: int
    duplicate_findings: int
    discarded_findings: int
    agent_execution_mode: str
    coverage_score: float
    runtime_ms: float
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int


@dataclass(slots=True)
class ComparativeMetric:
    name: str
    baseline: str
    extended: str
    delta: str


@dataclass(slots=True)
class ComparativeReport:
    title: str
    overview: str
    baseline_label: str
    extended_label: str
    baseline: ConsolidatedReport
    extended: ConsolidatedReport
    metrics: list[ComparativeMetric] = field(default_factory=list)
    conclusion: str = ""
