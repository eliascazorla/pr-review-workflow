from __future__ import annotations

import re
from dataclasses import dataclass
from pathlib import Path

from .models import DiffFile, DiffLine


@dataclass(slots=True)
class ParsedDiff:
    raw: str
    files: list[DiffFile]

    @property
    def text(self) -> str:
        return self.raw


def load_input(path_or_text: str) -> str:
    candidate = Path(path_or_text)
    if candidate.exists() and candidate.is_file():
        return candidate.read_text(encoding="utf-8", errors="replace")
    if candidate.exists() and candidate.is_dir():
        return directory_to_text(candidate)
    return path_or_text


def directory_to_text(root: Path) -> str:
    parts: list[str] = []
    for file_path in sorted(root.rglob("*")):
        if not file_path.is_file():
            continue
        if file_path.suffix.lower() not in {".py", ".js", ".ts", ".tsx", ".jsx", ".json", ".md", ".txt"}:
            continue
        try:
            content = file_path.read_text(encoding="utf-8", errors="replace")
        except OSError:
            continue
        rel = file_path.relative_to(root).as_posix()
        parts.append(f"FILE: {rel}\n{content}")
    return "\n\n".join(parts)


def parse_unified_diff(raw: str) -> ParsedDiff:
    files: list[DiffFile] = []
    current: DiffFile | None = None
    hunk_lines: list[str] = []
    current_hunk_header = ""
    current_new_line = 0

    for line in raw.splitlines():
        if line.startswith("diff --git "):
            if current is not None:
                if hunk_lines:
                    current.hunks.append("\n".join(hunk_lines).strip())
                    hunk_lines = []
                files.append(current)
            current = DiffFile(path="")
            continue

        if current is None:
            continue

        if line.startswith("+++ b/"):
            current.path = line.removeprefix("+++ b/").strip()
            continue

        if line.startswith("@@"):
            if hunk_lines:
                current.hunks.append("\n".join(hunk_lines).strip())
            hunk_lines = [line]
            current_hunk_header = line
            match = re.search(r"\+(\d+)(?:,(\d+))?", line)
            current_new_line = int(match.group(1)) if match else 0
            continue

        if line.startswith("+") and not line.startswith("+++"):
            current.additions += 1
            if current.path:
                current.added_lines.append(
                    DiffLine(
                        number=current_new_line,
                        content=line[1:],
                        hunk=current_hunk_header,
                    )
                )
            current_new_line += 1
        elif line.startswith("-") and not line.startswith("---"):
            current.deletions += 1
        else:
            if current_hunk_header:
                current_new_line += 1

        if hunk_lines:
            hunk_lines.append(line)

    if current is not None:
        if hunk_lines:
            current.hunks.append("\n".join(hunk_lines).strip())
        files.append(current)

    for file in files:
        parts = []
        if file.path:
            parts.append(f"File: {file.path}")
        if file.hunks:
            parts.append("\n\n".join(file.hunks))
        file.patch = "\n".join(parts)

    return ParsedDiff(raw=raw, files=files)

