"""Freeze filename-labeled images for an offline experiment; never infer labels."""
from __future__ import annotations

import hashlib
import io
import json
from pathlib import Path
import re

from PIL import Image


def validate_label_provenance(rows: list[dict], provenance: dict) -> None:
    evidence = {}
    for item in provenance['rows']:
        name = item['filename']
        if name in evidence:
            raise ValueError(f'Duplicate label provenance: {name}')
        evidence[name] = item
    for row in rows:
        if row['role'] != 'train':
            continue
        item = evidence.get(row['filename'])
        if not item or item['sha256'] != row['sha256'] or item['label'].upper() != row['label']:
            raise ValueError(f'Missing or mismatched label provenance: {row["filename"]}')


def inspect_folder(folder: Path) -> list[dict]:
    rows = []
    hashes = {}
    for path in sorted(folder.glob('*.png'), key=lambda item: item.name.casefold()):
        content = path.read_bytes()
        digest = hashlib.sha256(content).hexdigest()
        if digest in hashes:
            raise ValueError(f'Duplicate image, possibly leaking between train and test: {path.name}, {hashes[digest]}')
        hashes[digest] = path.name
        with Image.open(io.BytesIO(content)) as image:
            image.verify()
        with Image.open(io.BytesIO(content)) as image:
            if image.size != (120, 60):
                raise ValueError(f'Unexpected image dimensions: {path.name}: {image.size}')
        stem = path.stem
        label_raw = None
        reason = None
        if re.fullmatch(r'[0-9]{6}', stem):
            role = 'query'
        elif re.fullmatch(r'[A-Za-z0-9]{6}(?:_[0-9]{6})?', stem):
            role = 'train'
            label_raw = stem.split('_')[0]
        else:
            role = 'excluded'
            reason = 'Filename is neither a six-digit sample ID nor a six-character label.'
        rows.append(dict(filename=path.name, sha256=digest, role=role,
                         label_raw=label_raw, label=label_raw.upper() if label_raw else None,
                         exclusion_reason=reason))
    if not any(row['role'] == 'train' for row in rows):
        raise ValueError('No valid training labels.')
    if not any(row['role'] == 'query' for row in rows):
        raise ValueError('No unlabeled query images.')
    return rows


def freeze_dataset(source: Path, output: Path) -> list[dict]:
    rows = inspect_folder(source)
    destination = output / 'dataset'
    destination.mkdir(parents=True, exist_ok=False)
    for row in rows:
        content = (source / row['filename']).read_bytes()
        if hashlib.sha256(content).hexdigest() != row['sha256']:
            raise ValueError(f'Image changed during snapshot: {row["filename"]}')
        with (destination / row['filename']).open('xb') as stream:
            stream.write(content)
    manifest = dict(source=str(source.resolve()), labels_from='user filenames',
                    all_valid_labels_used_for_training=True, holdout_accuracy=None,
                    rows=rows)
    (output / 'dataset_manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')
    return rows
