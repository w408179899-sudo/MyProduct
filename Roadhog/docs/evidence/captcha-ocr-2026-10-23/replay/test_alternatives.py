import argparse
import hashlib
import importlib.metadata
import json
import os
from pathlib import Path
import time
import traceback

from PIL import Image, ImageOps

ROOT = Path(__file__).resolve().parent
INPUT = ROOT.parent / 'captcha-dma-20261023' / 'ocr-pages-103613' / 'encoded-6CA6AA0.png'
EXPECTED_SHA256 = '8293e07fd90f0dc571438449e680348feb2b4091f61d7ce4ea7c8872f4c697dd'
# This label is used only to score the raw result after inference.
GROUND_TRUTH = 'WLP63M'


def variants(include_crop=False):
    import cv2
    import numpy as np

    data = INPUT.read_bytes()
    assert hashlib.sha256(data).hexdigest() == EXPECTED_SHA256, 'Input image changed'
    folder = ROOT / 'variants' / EXPECTED_SHA256[:12]
    folder.mkdir(exist_ok=True)
    original = Image.open(INPUT).convert('RGB')
    enlarged = original.resize((original.width * 4, original.height * 4), Image.Resampling.LANCZOS)
    enlarged_path = folder / 'enlarged-4x-border.png'
    ImageOps.expand(enlarged, border=20, fill='white').save(enlarged_path)
    gray = np.array(enlarged.convert('L'))
    _, threshold = cv2.threshold(gray, 0, 255, cv2.THRESH_BINARY + cv2.THRESH_OTSU)
    threshold_path = folder / 'otsu-4x-border.png'
    ImageOps.expand(Image.fromarray(threshold), border=20, fill='white').save(threshold_path)
    output = [('original', INPUT), ('enlarged_4x_border', enlarged_path), ('otsu_4x_border', threshold_path)]
    if include_crop:
        # Generic foreground bounding box, independent of the answer and character positions.
        mask = np.array(original.convert('L')) < 120
        ys, xs = np.where(mask)
        bounds = (max(0, int(xs.min()) - 2), max(0, int(ys.min()) - 2),
                  min(original.width, int(xs.max()) + 3), min(original.height, int(ys.max()) + 3))
        crop_path = folder / 'foreground-crop.png'
        original.crop(bounds).save(crop_path)
        output.append(('foreground_crop', crop_path))
    return output


def score(text):
    stripped = text.strip()
    return {'raw_text': text, 'trimmed_text': stripped, 'length': len(stripped),
            'six_characters': len(stripped) == 6, 'exact_match': stripped == GROUND_TRUTH,
            'case_insensitive_match': stripped.upper() == GROUND_TRUTH}


def main():
    global INPUT, EXPECTED_SHA256, GROUND_TRUTH
    parser = argparse.ArgumentParser()
    parser.add_argument('engine', choices=['paddle', 'tesseract'])
    parser.add_argument('--image', type=Path, default=INPUT)
    parser.add_argument('--expected-sha256', default=EXPECTED_SHA256)
    parser.add_argument('--ground-truth', default=GROUND_TRUTH)
    parser.add_argument('--models', nargs='+', default=['en_PP-OCRv5_mobile_rec', 'PP-OCRv6_medium_rec'])
    parser.add_argument('--include-crop', action='store_true')
    parser.add_argument('--only-crop', action='store_true')
    parser.add_argument('--output-tag', default='103613')
    args = parser.parse_args()
    INPUT = args.image.resolve()
    EXPECTED_SHA256 = args.expected_sha256.lower()
    GROUND_TRUTH = args.ground_truth
    inputs = variants(args.include_crop or args.only_crop)
    if args.only_crop:
        inputs = [(name, path) for name, path in inputs if name == 'foreground_crop']
    result = {'input_path': str(INPUT), 'input_sha256': EXPECTED_SHA256,
              'ground_truth_from_user': GROUND_TRUTH, 'ground_truth_used_for_inference': False,
              'input_or_verification_submitted': False, 'engine': args.engine,
              'versions': {}, 'results': [], 'errors': []}
    output = ROOT / f'result-{args.engine}-{args.output_tag}.json'

    def save():
        output.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')

    def record(row):
        result['results'].append(row)
        save()
        print(json.dumps(row, ensure_ascii=False), flush=True)

    save()
    if args.engine == 'tesseract':
        from tesserocr import PyTessBaseAPI, OEM, PSM, tesseract_version
        result['versions'] = {'tesserocr': importlib.metadata.version('tesserocr'),
                              'tesseract': tesseract_version()}
        result['traineddata_sha256'] = hashlib.sha256((ROOT / 'tessdata' / 'eng.traineddata').read_bytes()).hexdigest()
        modes = [('single_line', PSM.SINGLE_LINE), ('single_word', PSM.SINGLE_WORD), ('raw_line', PSM.RAW_LINE)]
        with PyTessBaseAPI(path=str(ROOT / 'tessdata'), lang='eng', oem=OEM.LSTM_ONLY,
                          variables={'load_system_dawg': '0', 'load_freq_dawg': '0',
                                     'tessedit_char_whitelist': 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789'}) as api:
            for name, path in inputs:
                for mode_name, mode in modes:
                    api.Clear()
                    api.SetPageSegMode(mode)
                    api.SetImageFile(str(path))
                    started = time.perf_counter()
                    raw = api.GetUTF8Text()
                    elapsed_ms = (time.perf_counter() - started) * 1000
                    record({'model': 'tessdata_best/eng', 'variant': name, 'segmentation': mode_name,
                            **score(raw), 'word_confidences': api.AllWordConfidences(),
                            'inference_ms': round(elapsed_ms, 3)})
    else:
        os.environ['PADDLE_PDX_MODEL_SOURCE'] = 'BOS'
        os.environ['PADDLE_PDX_CACHE_HOME'] = str(ROOT / 'paddle-cache')
        os.environ['PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK'] = 'True'
        from paddleocr import TextRecognition
        result['versions'] = {name: importlib.metadata.version(name) for name in ('paddleocr', 'paddlepaddle', 'paddlex')}
        for model_name in args.models:
            model = None
            try:
                print(f'Loading {model_name}', flush=True)
                started = time.perf_counter()
                model = TextRecognition(model_name=model_name, device='cpu', enable_mkldnn=False, cpu_threads=4)
                initialization_s = round(time.perf_counter() - started, 3)
                for name, path in inputs:
                    started = time.perf_counter()
                    predictions = model.predict(input=str(path), batch_size=1)
                    elapsed_ms = (time.perf_counter() - started) * 1000
                    for prediction in predictions:
                        raw = str(prediction['rec_text'])
                        record({'model': model_name, 'variant': name, **score(raw),
                                'model_score': float(prediction['rec_score']),
                                'initialization_s': initialization_s, 'inference_ms': round(elapsed_ms, 3)})
            except Exception as exc:
                row = {'model': model_name, 'error': repr(exc), 'traceback': traceback.format_exc()}
                result['errors'].append(row)
                print(json.dumps(row), flush=True)
                save()
            finally:
                if model is not None:
                    model.close()
    save()
    print(f'Saved {output}', flush=True)


if __name__ == '__main__':
    main()
