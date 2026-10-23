import sys
from pathlib import Path
import tempfile
import unittest

from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parent))
from training_dataset import freeze_dataset, inspect_folder


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


if __name__ == '__main__':
    unittest.main()
