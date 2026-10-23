import argparse
import hashlib
import importlib.metadata
import json
import time
from datetime import datetime, timezone
from pathlib import Path

import ddddocr
import onnxruntime
from PIL import Image


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("image", type=Path)
    parser.add_argument("output", type=Path)
    parser.add_argument("--source-note", default="Fresh DMA memory scan of account 1; current image confirmed by user")
    args = parser.parse_args()
    image_bytes = args.image.read_bytes()
    with Image.open(args.image) as image:
        image.verify()
    with Image.open(args.image) as image:
        dimensions = list(image.size)
    report = {
        "captured_test_utc": datetime.now(timezone.utc).isoformat(),
        "ddddocr_version": importlib.metadata.version("ddddocr"),
        "onnxruntime_version": onnxruntime.__version__,
        "image_path": str(args.image.resolve()),
        "image_sha256": hashlib.sha256(image_bytes).hexdigest(),
        "image_dimensions": dimensions,
        "image_source": args.source_note,
        "preprocessing": "Original recovered PNG; no additional image preprocessing or character-range restriction",
        "input_submitted": False,
        "models": [],
    }
    for name, beta in [("default", False), ("beta", True)]:
        print(f"Loading {name} model...", flush=True)
        started = time.perf_counter()
        ocr = ddddocr.DdddOcr(beta=beta, use_gpu=False, show_ad=False)
        initialized = time.perf_counter()
        result = ocr.classification(image_bytes)
        finished = time.perf_counter()
        item = {
            "model": name,
            "beta": beta,
            "raw_result": result,
            "initialization_seconds": round(initialized - started, 3),
            "inference_milliseconds": round((finished - initialized) * 1000, 2),
        }
        report["models"].append(item)
        print(json.dumps(item, ensure_ascii=True), flush=True)
        args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
        del ocr
    print(f"Saved report: {args.output}", flush=True)


if __name__ == "__main__":
    main()
