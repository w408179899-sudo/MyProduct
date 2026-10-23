import hashlib
import json
from pathlib import Path
import struct
import tempfile
import unittest

from import_filename_labels import inspect_dataset


class FilenameLabelTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        (self.root / "_metadata").mkdir()

    def sample(self, sample_id, filename, label=None):
        # A header fixture suffices: byte identity and dimensions, not image decoding, are under test.
        data = b"\x89PNG\r\n\x1a\n" + b"\0\0\0\rIHDR" + struct.pack(">II", 120, 60) + bytes([sample_id])
        (self.root / filename).write_bytes(data)
        record = {"id": sample_id, "filename": f"{sample_id:06d}.png",
                  "png_sha256": hashlib.sha256(data).hexdigest().upper(), "label": label}
        (self.root / "_metadata" / f"sample-{sample_id:06d}.json").write_text(json.dumps(record), encoding="utf-8")

    def test_filename_correction_wins_without_reusing_old_label(self):
        self.sample(1, "n4vn6n_000001.png", "N3VN6N")
        summary, rows = inspect_dataset(self.root)
        self.assertFalse(summary["errors"])
        self.assertEqual(rows[0]["label"], "N4VN6N")
        self.assertEqual(rows[0]["label_raw"], "n4vn6n")

    def test_duplicate_answers_are_allowed_for_different_images(self):
        self.sample(1, "ABC123_000001.png")
        self.sample(2, "abc123_000002.png")
        summary, _ = inspect_dataset(self.root)
        self.assertFalse(summary["errors"])
        self.assertEqual(summary["labeled"], 2)

    def test_swapped_id_is_rejected_even_when_label_has_six_characters(self):
        self.sample(1, "ABC123_000002.png")
        summary, _ = inspect_dataset(self.root)
        self.assertTrue(summary["errors"])

    def test_skip_and_pending_do_not_become_training_labels(self):
        self.sample(1, "SKIP_000001.png")
        self.sample(2, "000002.png", "ABC123")
        summary, rows = inspect_dataset(self.root)
        self.assertEqual((summary["skipped"], summary["unlabeled"]), (1, 1))
        self.assertTrue(all(row["label"] is None for row in rows))

    def test_edited_image_is_not_associated_by_filename_alone(self):
        self.sample(1, "ABC123_000001.png")
        (self.root / "ABC123_000001.png").write_bytes(b"modified")
        summary, _ = inspect_dataset(self.root)
        self.assertTrue(any("Unknown or modified" in error for error in summary["errors"]))
        self.assertTrue(any("Missing image" in error for error in summary["errors"]))

    def test_empty_or_missing_dataset_is_not_a_successful_audit(self):
        for folder in (self.root, self.root / "missing"):
            with self.subTest(folder=folder):
                summary, rows = inspect_dataset(folder)
                self.assertTrue(any("No acquisition metadata" in error for error in summary["errors"]))
                self.assertEqual(rows, [])


if __name__ == "__main__":
    unittest.main()
