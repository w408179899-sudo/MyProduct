"""Offline PARSeq-Tiny finetuning and a self-contained image/prediction report."""
from __future__ import annotations

import argparse
import base64
import csv
import hashlib
import html
import json
from pathlib import Path
import random
import shutil
import subprocess
import sys
import time

from training_dataset import freeze_dataset


def write_json(path, data):
    pending = path.with_suffix(path.suffix + '.writing')
    pending.write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
    pending.replace(path)


def write_results(output, rows, predictions, summary):
    lookup = {row['filename']: row for row in rows}
    queries = [row for row in predictions if lookup[row['filename']]['role'] == 'query']
    with (output / '识别结果.csv').open('w', newline='', encoding='utf-8-sig') as stream:
        writer = csv.writer(stream)
        writer.writerow(['文件名', '模型原始输出', '忽略大小写显示', '字符数', '是否为6位字母数字'])
        for row in queries:
            writer.writerow([row['filename'], row['prediction_raw'], row['prediction_raw'].upper(),
                             len(row['prediction_raw']), row['six_alphanumeric']])
    (output / '识别结果.txt').write_text('\n'.join(f"{row['filename']}\t{row['prediction_raw']}" for row in queries) + '\n', encoding='utf-8')
    cards = []
    for row in queries:
        data = base64.b64encode((output / 'dataset' / row['filename']).read_bytes()).decode('ascii')
        prediction = row['prediction_raw']
        cards.append('<article><div class="name">' + html.escape(row['filename']) + '</div>'
                     + '<img width="240" height="120" src="data:image/png;base64,' + data + '" alt="测试图片">'
                     + '<div class="prediction">' + html.escape(prediction or '(空输出)') + '</div>'
                     + '<div class="length">' + str(len(prediction)) + ' 位</div></article>')
    document = '''<!doctype html><html lang="zh-CN"><meta charset="utf-8">
<title>TEST 识别结果</title><style>
body{font:16px system-ui,sans-serif;margin:32px;background:#f5f6f8;color:#182231}
h1{font-size:25px}p{line-height:1.8;color:#495467}.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(280px,1fr));gap:18px}
article{background:white;border:1px solid #d9dfe8;border-radius:12px;padding:20px;text-align:center}
.name{font-size:15px;color:#657185;margin-bottom:16px}img{object-fit:contain;border:1px solid #eee}
.prediction{font:bold 28px ui-monospace,Consolas,monospace;letter-spacing:3px;margin-top:18px}.length{font-size:13px;color:#68758a;margin-top:8px}
</style><h1>未标注图片 · 识别结果</h1><p>使用 TRAIN_COUNT 张有效标注训练 PARSeq-Tiny，共 QUERY_COUNT 张测试图片。<br>
下方文字是模型原始输出，大小写按相同字符看待；未补位或改字。请对照图片自行判断。</p><div class="grid">CARDS</div></html>'''
    document = document.replace('TRAIN_COUNT', str(summary['training_images'])).replace('QUERY_COUNT', str(len(queries))).replace('CARDS', '\n'.join(cards))
    (output / '识别结果.html').write_text(document, encoding='utf-8')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--parseq-repo', type=Path, required=True)
    parser.add_argument('--epochs', type=int, default=100)
    parser.add_argument('--batch-size', type=int, default=16)
    parser.add_argument('--learning-rate', type=float, default=1e-4)
    parser.add_argument('--seed', type=int, default=20261023)
    parser.add_argument('--device', choices=['cuda', 'cpu'], default='cuda')
    args = parser.parse_args()
    if args.epochs < 1 or args.batch_size < 1:
        parser.error('epochs and batch size must be positive')
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    started = time.monotonic()
    rows = freeze_dataset(args.source, output)
    train_rows = [row for row in rows if row['role'] == 'train']
    query_rows = [row for row in rows if row['role'] == 'query']
    excluded = [row for row in rows if row['role'] == 'excluded']
    sys.path.insert(0, str(args.parseq_repo.resolve()))
    import numpy as np
    from PIL import Image
    import torch
    from torch.utils.data import Dataset, DataLoader
    from torchvision import transforms as T
    import pytorch_lightning as pl
    from strhub.models.utils import create_model

    if args.device == 'cuda' and not torch.cuda.is_available():
        raise RuntimeError('The requested CUDA device is unavailable.')
    pl.seed_everything(args.seed, workers=True)
    random.seed(args.seed)
    np.random.seed(args.seed)
    torch.set_num_threads(8 if args.device == 'cpu' else 4)
    torch.set_float32_matmul_precision('high')
    torch.hub.set_dir(str(args.parseq_repo.resolve().parent / 'torch-cache'))
    cached_weights = Path(torch.hub.get_dir()) / 'checkpoints' / 'parseq_tiny-e7a21b54.pt'
    if cached_weights.exists() and not hashlib.sha256(cached_weights.read_bytes()).hexdigest().startswith('e7a21b54'):
        raise RuntimeError('Pretrained checkpoint hash does not match the upstream release.')
    model = create_model('parseq-tiny', pretrained=True)
    model.rng = np.random.default_rng(args.seed)
    model.batch_size = args.batch_size
    # The official optimizer scales LR by batch_size/256. Preserve its implementation.
    model.lr = args.learning_rate * 256 / args.batch_size
    model.warmup_pct = 0.1
    model.weight_decay = 0.01
    model.hparams.update(batch_size=model.batch_size, lr=model.lr, warmup_pct=model.warmup_pct,
                         weight_decay=model.weight_decay)
    img_size = tuple(model.hparams.img_size)
    normalize = [T.Resize(img_size, T.InterpolationMode.BICUBIC), T.ToTensor(), T.Normalize(0.5, 0.5)]
    inference_transform = T.Compose(normalize)
    training_transform = T.Compose([
        T.RandomApply([T.RandomAffine(degrees=3, translate=(0.015, 0.025), scale=(0.97, 1.03),
                                     interpolation=T.InterpolationMode.BILINEAR, fill=(242, 242, 232))], p=0.7),
        T.ColorJitter(brightness=0.08, contrast=0.12), *normalize])

    class Images(Dataset):
        def __init__(self, data, transform):
            self.rows, self.transform = data, transform
            self.images = []
            for row in data:
                with Image.open(output / 'dataset' / row['filename']) as image:
                    self.images.append(image.convert('RGB'))

        def __len__(self):
            return len(self.rows)

        def __getitem__(self, index):
            return self.transform(self.images[index]), self.rows[index]['label']

    dataset = Images(train_rows, training_transform)
    loader = DataLoader(dataset, batch_size=args.batch_size, shuffle=True, num_workers=0,
                        pin_memory=True, generator=torch.Generator().manual_seed(args.seed))

    @torch.inference_mode()
    def predict(system, selected):
        system.eval()
        result = []
        for start in range(0, len(selected), args.batch_size):
            batch = selected[start:start + args.batch_size]
            pictures = []
            for row in batch:
                with Image.open(output / 'dataset' / row['filename']) as image:
                    pictures.append(inference_transform(image.convert('RGB')))
            images = torch.stack(pictures).to(system.device)
            # No fixed output length, dictionary correction, padding, or label-dependent decoding.
            logits = system(images)
            predictions, probabilities = system.tokenizer.decode(logits.softmax(-1))
            for row, text, scores in zip(batch, predictions, probabilities):
                result.append(dict(filename=row['filename'], role=row['role'], prediction_raw=text,
                                   six_alphanumeric=len(text) == 6 and text.isascii() and text.isalnum(),
                                   sequence_score=float(scores.prod().cpu()),
                                   image_sha256=row['sha256']))
        return result

    commit = subprocess.check_output(['git', '-C', str(args.parseq_repo), 'rev-parse', 'HEAD'], text=True).strip()
    summary = dict(model='PARSeq-Tiny', pretrained=True, source_repository='https://github.com/baudm/parseq',
                   upstream_commit=commit, training_images=len(train_rows), query_images=len(query_rows),
                   excluded=[row['filename'] for row in excluded], epochs=args.epochs,
                   seed=args.seed, batch_size=args.batch_size, effective_peak_lr=args.learning_rate,
                   augmentation='mild affine and brightness/contrast; no flips or crops',
                   normalization='resize 32x128 bicubic, RGB, normalize(mean=0.5,std=0.5)',
                   device=args.device, gpu=torch.cuda.get_device_name() if args.device == 'cuda' else None,
                   torch_version=torch.__version__,
                   test_ground_truth_available=False, measured_test_accuracy=None,
                   checkpoint_selection='fixed final epoch; query images not used for training or selection',
                   captcha_submitted=False)
    write_json(output / 'experiment.json', summary)

    class Progress(pl.Callback):
        def __init__(self):
            self.losses = []
        def on_train_batch_end(self, trainer, module, outputs, batch, batch_idx):
            loss = outputs['loss'] if isinstance(outputs, dict) else outputs
            self.losses.append(float(loss.detach().cpu()))
        def on_train_epoch_end(self, trainer, module):
            current = trainer.current_epoch + 1
            status = dict(state='training', epoch=current, epochs=args.epochs,
                          loss=sum(self.losses) / len(self.losses),
                          elapsed_seconds=round(time.monotonic() - started, 1))
            self.losses.clear()
            write_json(output / 'training-status.json', status)
            with (output / 'training-history.jsonl').open('a', encoding='utf-8') as stream:
                stream.write(json.dumps(status) + '\n')
            if current == 1 or current % 5 == 0 or current == args.epochs:
                print(json.dumps(status), flush=True)

    trainer = pl.Trainer(accelerator='gpu' if args.device == 'cuda' else 'cpu', devices=1,
                         precision='16-mixed' if args.device == 'cuda' else '32-true',
                         max_epochs=args.epochs, logger=False, enable_checkpointing=False,
                         enable_progress_bar=False, enable_model_summary=False,
                         limit_val_batches=0, num_sanity_val_steps=0, gradient_clip_val=1.0,
                         callbacks=[Progress()], default_root_dir=str(output))
    trainer.fit(model, train_dataloaders=loader)
    checkpoint = output / 'parseq-tiny-final.ckpt'
    trainer.save_checkpoint(checkpoint, weights_only=True)
    model = model.float().to(args.device)
    # Training fit is recorded only as a sanity check, never as held-out accuracy.
    train_predictions = predict(model, train_rows)
    write_json(output / 'training-fit.json', train_predictions)
    summary['training_fit_correct'] = sum(
        pred['prediction_raw'].casefold() == row['label'].casefold()
        for pred, row in zip(train_predictions, train_rows))
    predictions = predict(model, query_rows + excluded)
    write_json(output / 'predictions.json', predictions)

    from strhub.models.parseq.system import PARSeq
    reloaded = PARSeq.load_from_checkpoint(str(checkpoint), map_location='cpu').float().to(args.device)
    repeated = predict(reloaded, query_rows + excluded)
    if [row['prediction_raw'] for row in repeated] != [row['prediction_raw'] for row in predictions]:
        raise RuntimeError('Saved checkpoint does not reproduce inference outputs.')
    summary.update(checkpoint_reload_verified=True,
                   checkpoint_sha256=hashlib.sha256(checkpoint.read_bytes()).hexdigest(),
                   elapsed_seconds=round(time.monotonic() - started, 1))
    write_json(output / 'experiment.json', summary)
    write_results(output, rows, predictions, summary)
    (output / '运行环境.txt').write_text(subprocess.check_output([sys.executable, '-m', 'pip', 'freeze'], text=True), encoding='utf-8')
    shutil.copy2(__file__, output / Path(__file__).name)
    shutil.copy2(Path(__file__).with_name('training_dataset.py'), output / 'training_dataset.py')
    (output / '说明.txt').write_text(
        f"本次使用 {len(train_rows)} 张有效标注图片微调 PARSeq-Tiny {args.epochs} 轮。\n"
        f"未标注测试图片 {len(query_rows)} 张，打开识别结果.html对照查看，或查看CSV/TXT。\n"
        f"未用于训练的异常标注：{', '.join(summary['excluded'])}。\n"
        "保留模型原始输出，没有强制补成6位，也没有自动修改你的标注。\n"
        "所有有效标注都用于训练，因此没有独立测试准确率；请自行核对未标注图片。\n"
        "未标注图片未参与训练、调参或模型选择。原 TEST 文件夹未修改。\n"
        "模型文件：parseq-tiny-final.ckpt；已重新加载核对推理结果一致。\n", encoding='utf-8')
    write_json(output / 'training-status.json', dict(state='completed', epoch=args.epochs,
               training_images=len(train_rows), query_images=len(query_rows),
               elapsed_seconds=summary['elapsed_seconds']))
    print(json.dumps(dict(state='completed', output=str(output), predictions=predictions,
                          training_fit_correct=summary['training_fit_correct']), ensure_ascii=False), flush=True)


if __name__ == '__main__':
    main()
