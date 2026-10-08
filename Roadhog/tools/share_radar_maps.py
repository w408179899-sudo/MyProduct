"""Explicit, backed-up migration from account radar libraries to one shared union.

Prepare while the client runs; apply only after exiting the client normally.
Existing runtime, editor and file-watcher implementations are unchanged.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import tempfile
from datetime import datetime, timezone


def read_json(path):
    return json.loads(Path(path).read_text(encoding="utf-8-sig"))


def encode(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + "\n").encode("utf-8")


def digest(data):
    return hashlib.sha256(data).hexdigest()


def segment_key(segment):
    points = []
    for name in ("Start", "End"):
        point = segment[name]
        if any(isinstance(point[axis], bool) or not isinstance(point[axis], (int, float)) for axis in ("X", "Y")):
            raise ValueError("Obstacle coordinates must be JSON numbers")
        coords = tuple(float(point[axis]) for axis in ("X", "Y"))
        if not all(math.isfinite(v) for v in coords):
            raise ValueError("Non-finite obstacle coordinate")
        points.append(coords)
    if math.dist(*points) < 0.05:
        raise ValueError("Segment is below the runtime's 0.05m minimum")
    # Use exact coordinates for the union, and reject ambiguity with the runtime
    # four-decimal deduplication rather than silently losing a distinct segment.
    return tuple(sorted(points))


def union_maps(documents, map_id, now):
    if not 0 < map_id <= 0xffffffff:
        raise ValueError("Invalid map ID")
    codes, segments, keys, rounded, ids = set(), [], set(), {}, set()
    created = []
    for document in documents:
        if document.get("MapId") != map_id:
            raise ValueError("MapId differs from its filename")
        if document.get("Version", 1) != 1:
            raise ValueError("Unsupported radar map version")
        code = (document.get("MapCode") or "").strip()
        if code:
            codes.add(code)
        if document.get("CreatedAt"):
            value = datetime.fromisoformat(document["CreatedAt"])
            if value.tzinfo is None:
                raise ValueError("CreatedAt must include a timezone")
            created.append((value, document["CreatedAt"]))
        for original in document.get("Segments", []) or []:
            key = segment_key(original)
            if key in keys:
                continue
            runtime_key = tuple(sorted(tuple(round(v, 4) for v in p) for p in key))
            if runtime_key in rounded and rounded[runtime_key] != key:
                raise ValueError("Distinct segments collide under runtime coordinate rounding")
            rounded[runtime_key] = key
            keys.add(key)
            segment = copy.deepcopy(original)
            segment.pop("Length", None)  # computed by RadarObstacleSegment
            identity = str(segment.get("Id", "")).strip()
            if not identity or identity in ids:
                identity = "wall-shared-" + digest(encode(key))[:16]
                while identity in ids:
                    identity += "-union"
            segment["Id"] = identity
            ids.add(identity)
            segments.append(segment)
    if len(codes) > 1:
        raise ValueError("Conflicting MapCode values for the same map ID")
    if len(segments) > 5000:
        raise ValueError("Shared union exceeds runtime's 5000-segment limit")
    return dict(Version=1, MapId=map_id, MapCode=next(iter(codes), ""),
                CreatedAt=min(created)[1] if created else now,
                UpdatedAt=now, Segments=segments)


def source_directories(root, config):
    shared = root / "radar-maps"
    directories = [shared.resolve()]
    accounts = config.get("Accounts")
    if not isinstance(accounts, list) or not accounts:
        raise ValueError("Expected a nonempty Accounts list")
    for account in accounts:
        reference = (account.get("RadarMapDirectory") or "").strip()
        selected = (root / reference).resolve() if reference else shared.resolve()
        if not selected.is_dir() and reference:
            raise ValueError("Account radar directory is missing: " + str(selected))
        if selected not in directories:
            directories.append(selected)
    return directories


def snapshot(root):
    config_path = root / "accounts.json"
    config_bytes = config_path.read_bytes()
    config = json.loads(config_bytes.decode("utf-8-sig"))
    sources = {str(config_path): digest(config_bytes)}
    groups, counts = {}, {}
    for directory in source_directories(root, config):
        for path in sorted(directory.glob("*.json")):
            if not path.stem.isascii() or not path.stem.isdecimal():
                raise ValueError("Non-numeric radar map filename: " + str(path))
            data = path.read_bytes()
            document = json.loads(data.decode("utf-8-sig"))
            map_id = int(path.stem)
            sources[str(path)] = digest(data)
            groups.setdefault(map_id, []).append(document)
            counts[str(path)] = len(document.get("Segments", []) or [])
    return config, sources, groups, counts


def prepare(root, plan_directory):
    root, plan_directory = Path(root).resolve(), Path(plan_directory).resolve()
    if plan_directory.exists():
        raise ValueError("Plan directory must be new")
    config, sources, groups, counts = snapshot(root)
    now = datetime.now(timezone.utc).isoformat()
    maps = {str(map_id): union_maps(documents, map_id, now) for map_id, documents in groups.items()}
    if not maps:
        raise ValueError("No radar map files to merge")
    changed = copy.deepcopy(config)
    for account in changed["Accounts"]:
        account["RadarMapDirectory"] = "radar-maps"
    plan_directory.mkdir(parents=True)
    (plan_directory / "accounts.json").write_bytes(encode(changed))
    hashes = {"accounts.json": digest(encode(changed))}
    for map_id, document in maps.items():
        name = map_id + ".json"
        (plan_directory / name).write_bytes(encode(document))
        hashes[name] = digest(encode(document))
    manifest = dict(ConfigRoot=str(root), PreparedAt=now, Sources=sources,
                    CandidateHashes=hashes, InputSegmentCounts=counts,
                    UnionSegmentCounts={key: len(doc["Segments"]) for key, doc in maps.items()},
                    AccountCount=len(config["Accounts"]))
    (plan_directory / "plan.json").write_bytes(encode(manifest))
    return manifest


def running_client(root):
    if os.name != "nt":
        return False
    exe = str(root.parent / "Roadhog.exe").replace("'", "''")
    result = subprocess.run(["powershell.exe", "-NoProfile", "-Command",
        "$ErrorActionPreference='Stop'; @(Get-CimInstance Win32_Process -Filter \"Name = 'Roadhog.exe'\" | "
        "Where-Object { $_.ExecutablePath -eq '" + exe + "' }).Count"],
        check=True, capture_output=True, text=True)
    return int(result.stdout.strip()) != 0


def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, temporary = tempfile.mkstemp(prefix="." + path.name + ".", suffix=".tmp", dir=path.parent)
    try:
        with os.fdopen(fd, "wb") as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def target_path(root, name):
    if name == "accounts.json":
        return root / name
    stem, suffix = os.path.splitext(name)
    if suffix != ".json" or not stem.isascii() or not stem.isdecimal() or not 0 < int(stem) <= 0xffffffff:
        raise ValueError("Invalid shared radar target filename")
    return root / "radar-maps" / name


def apply(root, plan_directory):
    root, plan_directory = Path(root).resolve(), Path(plan_directory).resolve()
    manifest = read_json(plan_directory / "plan.json")
    if manifest["ConfigRoot"] != str(root):
        raise ValueError("Plan belongs to a different configuration root")
    if running_client(root):
        raise ValueError("Exit this Roadhog client normally before applying the migration")
    config, sources, groups, _ = snapshot(root)
    if sources != manifest["Sources"]:
        raise ValueError("Configuration/maps changed since preparation; prepare a fresh plan")
    changed = copy.deepcopy(config)
    for account in changed["Accounts"]:
        account["RadarMapDirectory"] = "radar-maps"
    candidates = {"accounts.json": encode(changed)}
    candidates.update({str(map_id) + ".json": encode(union_maps(documents, map_id, manifest["PreparedAt"]))
                       for map_id, documents in groups.items()})
    if {name: digest(data) for name, data in candidates.items()} != manifest["CandidateHashes"]:
        raise ValueError("Plan does not match the validated union")
    for name, data in candidates.items():
        if (plan_directory / name).read_bytes() != data:
            raise ValueError("Prepared candidate was changed: " + name)
    backup = root / "shared-radar-backups" / (datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%S") + "-" + os.urandom(4).hex())
    backup.mkdir(parents=True)
    for index, path in enumerate(sources):
        (backup / (str(index) + ".original")).write_bytes(Path(path).read_bytes())
    (backup / "sources.json").write_bytes(encode(dict(enumerate(sources))))
    targets = {name: target_path(root, name) for name in candidates}
    originals = {name: path.read_bytes() if path.exists() else None for name, path in targets.items()}
    for name, data in originals.items():
        if data is not None:
            (backup / ("original-" + name)).write_bytes(data)
    record = dict(ConfigRoot=str(root), Plan=str(plan_directory), Targets={name: str(path) for name, path in targets.items()},
                  OriginalPresent={name: data is not None for name, data in originals.items()},
                  AppliedHashes=manifest["CandidateHashes"], Status="applying")
    atomic_write(backup / "migration.json", encode(record))
    touched = []
    try:
        # Publish maps first and the account pointer last, all while the client is stopped.
        for name in [key for key in candidates if key != "accounts.json"] + ["accounts.json"]:
            touched.append(name)
            atomic_write(targets[name], candidates[name])
        record["Status"] = "applied"
        atomic_write(backup / "migration.json", encode(record))
    except BaseException:
        for name in reversed(touched):
            if originals[name] is None:
                targets[name].unlink(missing_ok=True)
            else:
                atomic_write(targets[name], originals[name])
        record["Status"] = "rolled_back"
        atomic_write(backup / "migration.json", encode(record))
        raise
    return backup


def rollback(backup):
    backup = Path(backup).resolve()
    record = read_json(backup / "migration.json")
    root = Path(record["ConfigRoot"]).resolve()
    if running_client(root):
        raise ValueError("Exit this Roadhog client normally before rollback")
    if record["Status"] != "applied":
        raise ValueError("Migration is not in the applied state")
    for name, target in record["Targets"].items():
        expected = target_path(root, name)
        if Path(target).resolve() != expected.resolve():
            raise ValueError("Backup target is outside the expected configuration files")
        if digest(expected.read_bytes()) != record["AppliedHashes"][name]:
            raise ValueError("Files changed after migration; refusing to overwrite newer edits")
    for name in ["accounts.json"] + [key for key in record["Targets"] if key != "accounts.json"]:
        target = Path(record["Targets"][name])
        if record["OriginalPresent"][name]:
            atomic_write(target, (backup / ("original-" + name)).read_bytes())
        else:
            target.unlink()
    record["Status"] = "rolled_back"
    atomic_write(backup / "migration.json", encode(record))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("operation", choices=("prepare", "apply", "rollback"))
    parser.add_argument("path")
    parser.add_argument("plan", nargs="?")
    args = parser.parse_args()
    if args.operation != "rollback" and not args.plan:
        parser.error("prepare/apply require a plan directory")
    if args.operation == "prepare":
        result = prepare(args.path, args.plan)
        print(json.dumps({"Accounts": result["AccountCount"], "UnionSegments": result["UnionSegmentCounts"]}))
    elif args.operation == "apply":
        print("Backup: " + str(apply(args.path, args.plan)))
    else:
        rollback(args.path)
        print("Original configuration and shared maps restored.")
