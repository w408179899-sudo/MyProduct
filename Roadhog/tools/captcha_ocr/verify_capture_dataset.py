"""Audit the acquired PNG files without making OCR guesses or changing labels."""
import argparse
import hashlib
import io
import json
from pathlib import Path
from datetime import datetime, timezone

from PIL import Image

from import_filename_labels import inspect_dataset


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('folder', type=Path)
    parser.add_argument('--expected', type=int, default=300)
    args = parser.parse_args()
    hashes = set()
    errors = []
    files = sorted(args.folder.glob('*.png'))
    for path in files:
        data = path.read_bytes()
        digest = hashlib.sha256(data).hexdigest()
        if digest in hashes:
            errors.append(f'Duplicate image: {path.name}')
        hashes.add(digest)
        try:
            with Image.open(io.BytesIO(data)) as image:
                image.verify()
            with Image.open(io.BytesIO(data)) as image:
                if image.size != (120, 60):
                    errors.append(f'Unexpected dimensions: {path.name}')
                histogram = image.convert('L').histogram()
            if sum(histogram[:60]) < 20 or sum(histogram[181:]) < 100:
                errors.append(f'Black, blank, or low-content image: {path.name}')
        except (OSError, ValueError) as error:
            errors.append(f'Image decode failed: {path.name}: {error}')
    labels, _ = inspect_dataset(args.folder)
    if len(files) != args.expected:
        errors.append(f'Expected {args.expected} images; found {len(files)}')
    result = dict(checked_utc=datetime.now(timezone.utc).isoformat(), images=len(files),
                  unique_images=len(hashes), expected_images=args.expected, image_errors=errors,
                  label_summary=labels, captcha_submitted=False, training_started=False)
    path = args.folder / '_metadata' / 'validation.json'
    staging = path.with_name(path.name + '.writing')
    staging.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    staging.replace(path)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    return 2 if errors or labels['errors'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
