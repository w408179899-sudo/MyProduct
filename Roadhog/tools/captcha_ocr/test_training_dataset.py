import sys
from pathlib import Path
import tempfile
import unittest

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from training_dataset import freeze_dataset, inspect_folder, validate_label_provenance


class DatasetIsolationTests(unittest.TestCase):
    def image(self, folder, name, color):
        Image.new('RGB', (120, 60), color).save(folder / name)

    def test_numbered_queries_and_short_labels_do_not_train(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.image(folder, 'swxyka.png', 'red')
            self.image(folder, '000100.png', 'blue')
            self.image(folder, 'NQ8RN.png', 'green')
            rows = {row['filename']: row for row in inspect_folder(folder)}
            self.assertEqual(rows['swxyka.png']['label'], 'SWXYKA')
            self.assertEqual(rows['swxyka.png']['label_raw'], 'swxyka')
            self.assertEqual(rows['000100.png']['role'], 'query')
            self.assertIsNone(rows['000100.png']['label'])
            self.assertEqual(rows['NQ8RN.png']['role'], 'excluded')

    def test_same_image_cannot_cross_train_and_query(self):
        with tempfile.TemporaryDirectory() as temporary:
            folder = Path(temporary)
            self.image(folder, 'ABC123.png', 'red')
            (folder / '000100.png').write_bytes((folder / 'ABC123.png').read_bytes())
            with self.assertRaisesRegex(ValueError, 'leaking'):
                inspect_folder(folder)

    def test_frozen_dataset_survives_source_rename(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            source = root / 'source'
            source.mkdir()
            self.image(source, 'ABC123.png', 'red')
            self.image(source, '000100.png', 'blue')
            result = root / 'result'
            freeze_dataset(source, result)
            (source / 'ABC123.png').rename(source / 'XYZ789.png')
            self.assertTrue((result / 'dataset' / 'ABC123.png').exists())
            self.assertIn('ABC123', (result / 'dataset_manifest.json').read_text(encoding='utf-8'))

    def test_provenance_requires_same_image_and_label(self):
        rows = [dict(filename='ABC123.png', role='train', sha256='image-a', label='ABC123')]
        good = dict(filename='ABC123.png', sha256='image-a', label='abc123')
        validate_label_provenance(rows, dict(rows=[good]))
        for items in [[], [dict(good, sha256='image-b')], [dict(good, label='XYZ789')], [good, good]]:
            with self.assertRaises(ValueError):
                validate_label_provenance(rows, dict(rows=items))

    def test_query_does_not_acquire_label_from_provenance(self):
        rows = [dict(filename='000001.png', role='query', sha256='image-a', label=None)]
        validate_label_provenance(rows, dict(rows=[dict(filename='000001.png', sha256='image-a', label='ABC123')]))
        self.assertIsNone(rows[0]['label'])


if __name__ == '__main__':
    unittest.main()
