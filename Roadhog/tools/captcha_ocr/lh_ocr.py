"""Process-isolated adapter for the verified x86 lh.dll COM OCR interface.

Registration codes are accepted only from the caller/environment and sent over stdin.
This module does not click, capture screens, read DMA, or silently fall back to another OCR.
"""
from __future__ import annotations

import atexit
import base64
from dataclasses import asdict, dataclass
import json
import math
import os
from pathlib import Path
import queue
import re
import subprocess
import threading
from typing import Any

ENGINE = "lh.dll model OCR"
PREFIX = "LH_OCR_JSON:"


class OcrError(RuntimeError):
    def __init__(self, code: str, plugin_code: int | None = None):
        self.code = code
        self.plugin_code = plugin_code
        super().__init__(code if plugin_code is None else f"{code} (plugin_code={plugin_code})")


@dataclass(frozen=True)
class OcrResult:
    raw_text: str
    raw_details: str
    text: str
    eligible_to_submit: bool
    engine: str = ENGINE


def parse_result(message: dict[str, Any]) -> OcrResult:
    if message.get("status") != "ok":
        raise OcrError(str(message.get("code", "invalid_host_response")), message.get("plugin_code"))
    raw, details = message.get("raw_text"), message.get("raw_details")
    if not isinstance(raw, str) or not isinstance(details, str):
        raise OcrError("invalid_host_response")
    if raw == "-88":
        raise OcrError("license_required", -88)
    # The library appends CRLF per detected text line. Remove only that final delimiter.
    # Never concatenate separate boxes/lines, strip arbitrary spaces, pad or infer characters.
    text = raw[:-2] if raw.endswith("\r\n") else raw
    return OcrResult(raw, details, text, re.fullmatch(r"[A-Za-z0-9]{6}", text) is not None)


class LhOcr:
    def __init__(self, dll_path: str | Path, host_path: str | Path,
                 *, timeout_seconds: float = 30, threads: int = 2,
                 registration_code: str | None = None, registration_ip: str | None = None,
                 model_paths: dict[str, str] | None = None,
                 _host_command: list[str] | None = None):
        timeout = float(timeout_seconds)
        if not math.isfinite(timeout) or not 0 < timeout <= 300:
            raise OcrError("invalid_timeout")
        if type(threads) is not int or not 1 <= threads <= 32:
            raise OcrError("invalid_thread_count")
        self.timeout = timeout
        self.last_result: OcrResult | None = None
        self._messages: queue.Queue[dict[str, Any] | None] = queue.Queue()
        self._lock = threading.Lock()
        self._closed = False
        command = _host_command or [str(Path(host_path).resolve())]
        env = dict(os.environ)
        for key in ("LH_OCR_REG_CODE", "LH_OCR_REG_IP"):
            env.pop(key, None)
        try:
            self._process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                             stderr=subprocess.DEVNULL, text=True, encoding="utf-8",
                                             bufsize=1, env=env,
                                             creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0))
        except OSError:
            raise OcrError("host_start_failed") from None
        self._reader = threading.Thread(target=self._read_messages, name="lh-ocr-output", daemon=True)
        self._reader.start()
        payload: dict[str, Any] = {"dll_path": str(Path(dll_path).resolve()), "threads": threads}
        if model_paths:
            for key in ("det_path", "cls_path", "rec_path", "keys_path"):
                payload[key] = model_paths.get(key, "")
        code = registration_code if registration_code is not None else os.environ.get("LH_OCR_REG_CODE", "")
        ip = registration_ip if registration_ip is not None else os.environ.get("LH_OCR_REG_IP", "")
        if code:
            payload.update(registration_code=code, registration_ip=ip)
        try:
            self._send(payload)
            initialized = self._receive()
            if initialized.get("status") != "ready":
                raise OcrError(str(initialized.get("code", "initialization_failed")), initialized.get("plugin_code"))
            if initialized.get("host_bits") != 32 or initialized.get("engine") != "lh.dll":
                raise OcrError("unexpected_host_identity")
        except BaseException:
            self.close()
            raise
        finally:
            payload.pop("registration_code", None)
        atexit.register(self.close)

    @classmethod
    def from_config(cls, path: str | Path) -> "LhOcr":
        try:
            path = Path(path).resolve()
            config = json.loads(path.read_text(encoding="utf-8-sig"))
            if any(key in config for key in ("registration_code", "password", "reg_code")):
                raise OcrError("registration_secret_must_use_environment")
            def resolve(value: str) -> Path:
                item = Path(value)
                return item if item.is_absolute() else path.parent / item
            return cls(resolve(config["dll_path"]), resolve(config["host_path"]),
                       timeout_seconds=config.get("timeout_seconds", 30), threads=config.get("threads", 2),
                       model_paths=config.get("model_paths"))
        except (OSError, ValueError, KeyError, TypeError):
            raise OcrError("invalid_or_missing_config") from None

    def _read_messages(self) -> None:
        try:
            assert self._process.stdout is not None
            for line in self._process.stdout:
                line = line.lstrip("\ufeff")
                if line.startswith(PREFIX):
                    try:
                        value = json.loads(line[len(PREFIX):])
                        if not isinstance(value, dict):
                            raise ValueError()
                        self._messages.put(value)
                    except ValueError:
                        self._messages.put({"status": "error", "code": "invalid_host_response"})
                        break
        finally:
            self._messages.put(None)

    def _send(self, request: dict[str, Any]) -> None:
        if self._closed:
            raise OcrError("host_closed")
        try:
            assert self._process.stdin is not None
            self._process.stdin.write(json.dumps(request, ensure_ascii=True) + "\n")
            self._process.stdin.flush()
        except (OSError, ValueError):
            raise OcrError("host_exited") from None

    def _receive(self) -> dict[str, Any]:
        try:
            response = self._messages.get(timeout=self.timeout)
        except queue.Empty:
            raise OcrError("host_timeout") from None
        if response is None:
            raise OcrError("host_exited")
        return response

    def recognize(self, image: bytes) -> OcrResult:
        if not isinstance(image, bytes) or not 0 < len(image) <= 8 * 1024 * 1024:
            raise OcrError("invalid_image")
        with self._lock:
            try:
                self._send({"op": "recognize", "image_base64": base64.b64encode(image).decode("ascii")})
                result = parse_result(self._receive())
                self.last_result = result
                return result
            except BaseException:
                self.close()
                raise

    def classification(self, image: bytes) -> str:
        """Compatibility entry for the existing diagnostic loop; exact raw output is retained."""
        return self.recognize(image).text

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        try:
            if self._process.stdin:
                try:
                    self._process.stdin.write('{"op":"shutdown"}\n')
                    self._process.stdin.flush()
                except (OSError, ValueError):
                    pass
                try:
                    self._process.stdin.close()
                except (OSError, ValueError):
                    pass
            try:
                self._process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                self._process.kill()
                self._process.wait(timeout=3)
        finally:
            self._reader.join(timeout=1)
            if self._process.stdout:
                self._process.stdout.close()
            atexit.unregister(self.close)

    def __enter__(self) -> "LhOcr":
        return self

    def __exit__(self, *_: Any) -> None:
        self.close()


def create_ocr() -> LhOcr:
    workspace = Path(__file__).resolve().parents[3]
    config = os.environ.get("LH_OCR_CONFIG", str(workspace / ".tmp/lh-ocr-host/config.json"))
    return LhOcr.from_config(config)


def main() -> int:
    import argparse
    parser = argparse.ArgumentParser(description="Offline lh.dll OCR; never operates the game UI.")
    parser.add_argument("image", type=Path)
    parser.add_argument("--config", type=Path)
    args = parser.parse_args()
    try:
        with LhOcr.from_config(args.config) if args.config else create_ocr() as ocr:
            print(json.dumps(asdict(ocr.recognize(args.image.read_bytes())), ensure_ascii=False))
        return 0
    except OcrError as error:
        print(json.dumps({"status": "error", "engine": ENGINE, "code": error.code, "plugin_code": error.plugin_code}))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
