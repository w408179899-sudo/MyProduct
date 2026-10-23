"""Validate manually renamed images against their acquisition hashes; never infer labels."""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import re
import struct


def inspect_dataset(folder: Path) -> tuple[dict, list[dict]]:
    records = {}
    errors = []
    for path in sorted((folder / "_metadata").glob("sample-*.json")):
        record = json.loads(path.read_text(encoding="utf-8-sig"))
        digest = record["png_sha256"].upper()
        if digest in records:
            errors.append(f"Duplicate acquisition image hash: {path.name}")
        records[digest] = record

    if not records:
        errors.append("No acquisition metadata found in _metadata/sample-*.json.")

    consumed = set()
    rows = []
    for path in sorted(folder.glob("*.png")):
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest().upper()
        record = records.get(digest)
        if record is None:
            errors.append(f"Unknown or modified image: {path.name}")
            continue
        if digest in consumed:
            errors.append(f"Duplicate image file: {path.name}")
            continue
        consumed.add(digest)
        if len(data) < 24 or data[:8] != b"\x89PNG\r\n\x1a\n" or struct.unpack(">II", data[16:24]) != (120, 60):
            errors.append(f"Invalid image dimensions or format: {path.name}")
            continue
        sample_id = f"{record['id']:06d}"
        stem = path.stem
        label = None
        label_raw = None
        if stem == sample_id:
            state = "unlabeled"
        elif stem.upper() == f"SKIP_{sample_id}" or stem == f"跳过_{sample_id}":
            state = "skipped"
        else:
            match = re.fullmatch(r"([A-Za-z0-9]{6})_([0-9]{6})", stem)
            if match is None or match[2] != sample_id:
                errors.append(f"Invalid label format or image/id mismatch: {path.name}; expected ANSWER_{sample_id}.png")
                continue
            label_raw = match[1]
            label = label_raw.upper()
            state = "labeled"
        rows.append({"sample_id": sample_id, "file": path.name, "image_sha256": digest,
                     "status": state, "label_raw": label_raw, "label": label,
                     "label_source": "user_filename", "server_verified": False})

    for digest, record in records.items():
        if digest not in consumed:
            errors.append(f"Missing image for sample {record['id']:06d}")
    rows.sort(key=lambda item: item["sample_id"])
    summary = {"acquired": len(records), "images": len(consumed),
               "labeled": sum(row["status"] == "labeled" for row in rows),
               "unlabeled": sum(row["status"] == "unlabeled" for row in rows),
               "skipped": sum(row["status"] == "skipped" for row in rows),
               "errors": errors, "training_started": False}
    return summary, rows


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("folder", type=Path)
    parser.add_argument("--export", type=Path, help="Optional JSONL label snapshot; only written after all validation passes.")
    args = parser.parse_args()
    summary, rows = inspect_dataset(args.folder)
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    if summary["errors"]:
        return 2
    if args.export:
        args.export.parent.mkdir(parents=True, exist_ok=True)
        pending = args.export.with_name(args.export.name + ".writing")
        pending.write_text("".join(json.dumps(row, ensure_ascii=False) + "\n" for row in rows), encoding="utf-8")
        pending.replace(args.export)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
