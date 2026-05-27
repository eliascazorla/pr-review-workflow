from __future__ import annotations

import json
import os
from dataclasses import dataclass
import logging
from typing import Any
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen

from .models import TokenUsage


logger = logging.getLogger(__name__)


@dataclass(slots=True)
class ToolCallResult:
    payload: dict[str, Any]
    usage: TokenUsage


@dataclass(slots=True)
class OpenAICompatibleClient:
    api_key: str
    base_url: str
    model: str
    timeout: int = 60

    @classmethod
    def from_env(cls) -> "OpenAICompatibleClient | None":
        api_key = os.getenv("OPENAI_API_KEY", "").strip()
        if not api_key:
            logger.info("llm client not created because OPENAI_API_KEY is missing")
            return None
        raw_base_url = os.getenv("OPENAI_BASE_URL", "").strip()
        raw_model = os.getenv("MULTIAGENT_MODEL", "").strip()
        base_url = raw_base_url or "https://api.openai.com/v1"
        model = raw_model or "gpt-4.1-mini"
        timeout_raw = os.getenv("MULTIAGENT_TIMEOUT_SECONDS", "60").strip()
        try:
            timeout = int(timeout_raw)
        except ValueError:
            timeout = 60
        if not raw_base_url:
            logger.info("llm client using default base_url because OPENAI_BASE_URL is empty")
        if not raw_model:
            logger.info("llm client using default model because MULTIAGENT_MODEL is empty")
        logger.info("llm client created with model=%s base_url=%s timeout=%s", model, base_url, timeout)
        return cls(api_key=api_key, base_url=base_url, model=model, timeout=timeout)

    @property
    def available(self) -> bool:
        return bool(self.api_key and self.base_url and self.model)

    def _post(self, path: str, payload: dict[str, Any]) -> dict[str, Any]:
        url = self.base_url.rstrip("/") + path
        request = Request(
            url,
            data=json.dumps(payload).encode("utf-8"),
            headers={
                "Authorization": f"Bearer {self.api_key}",
                "Content-Type": "application/json",
            },
            method="POST",
        )

        try:
            with urlopen(request, timeout=self.timeout) as response:
                raw = response.read().decode("utf-8")
        except HTTPError as exc:
            details = exc.read().decode("utf-8", errors="replace")
            raise RuntimeError(f"LLM request failed with HTTP {exc.code}: {details}") from exc
        except URLError as exc:
            raise RuntimeError(f"LLM request failed: {exc.reason}") from exc

        return json.loads(raw)

    def call_tool(
        self,
        *,
        system_prompt: str,
        user_prompt: str,
        tool: dict[str, Any],
    ) -> ToolCallResult:
        logger.info("llm tool call started for %s", tool["function"]["name"])
        payload = {
            "model": self.model,
            "messages": [
                {"role": "system", "content": system_prompt},
                {"role": "user", "content": user_prompt},
            ],
            "tools": [tool],
            "tool_choice": {"type": "function", "function": {"name": tool["function"]["name"]}},
            "temperature": 0,
        }
        response = self._post("/chat/completions", payload)
        logger.info("llm tool call completed for %s", tool["function"]["name"])
        return ToolCallResult(
            payload=self._extract_tool_arguments(response, tool["function"]["name"]),
            usage=TokenUsage.from_api_payload(response.get("usage")),
        )

    def _extract_tool_arguments(self, response: dict[str, Any], tool_name: str) -> dict[str, Any]:
        choices = response.get("choices") or []
        if not choices:
            raise RuntimeError("LLM response did not include any choices.")

        message = choices[0].get("message") or {}
        tool_calls = message.get("tool_calls") or []
        if tool_calls:
            arguments = tool_calls[0].get("function", {}).get("arguments", "{}")
            try:
                return json.loads(arguments)
            except json.JSONDecodeError as exc:
                raise RuntimeError(f"LLM tool call for {tool_name} returned invalid JSON.") from exc

        content = (message.get("content") or "").strip()
        if content:
            try:
                return json.loads(content)
            except json.JSONDecodeError:
                pass

        raise RuntimeError(f"LLM did not call the expected tool: {tool_name}")
