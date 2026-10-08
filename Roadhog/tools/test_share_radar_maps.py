import copy
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import share_radar_maps as migration


NOW = "2026-10-08T08:00:00+00:00"


def segment(identity="wall", start=(0, 0), end=(0, 5)):
    return {"Id": identity, "Start": dict(zip(("X", "Y"), start)), "End": dict(zip(("X", "Y"), end))}


def document(*segments, map_id=47):
    return dict(Version=1, MapId=map_id, MapCode="", CreatedAt=NOW, UpdatedAt=NOW, Segments=list(segments))


class SharedRadarMigrationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.base = Path(self.temporary.name)
        self.root = self.base / "client" / "config"
        self.plan = self.base / "plan"
        self.config = dict(Version=3, FutureField={"preserve": True}, Accounts=[
            dict(AccountName="脚本" + str(n), InstanceId=str(n), Region="一区" if n < 5 else "六区",
                 RadarMapDirectory=f"preserved/{n}/radar-maps", HardwareKey="device-" + str(n),
                 ProfileName="profile-" + str(n), ScriptSettings={"Combat": {"Enabled": True}},
                 LicenseCredentialPath=f"preserved/{n}/license.dat") for n in range(1, 7)])
        self.write(self.root / "accounts.json", self.config)
        self.write(self.root / "radar-maps" / "47.json", document(segment("global", (-5, 0), (-5, 5))))
        for n in range(1, 7):
            self.write(self.root / f"preserved/{n}/radar-maps/47.json", document(segment("wall", (n, 0), (n, 5))))

    def write(self, path, value):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(migration.encode(value))

    def prepare(self):
        return migration.prepare(self.root, self.plan)

    def test_union_preserves_all_six_accounts_and_public_map(self):
        original = {p: p.read_bytes() for p in self.root.rglob("*.json")}
        result = self.prepare()
        self.assertEqual({"47": 7}, result["UnionSegmentCounts"])
        union = migration.read_json(self.plan / "47.json")
        self.assertEqual(7, len({s["Id"] for s in union["Segments"]}))
        self.assertEqual({migration.segment_key(s) for p in original if p.name == "47.json"
                          for s in migration.read_json(p)["Segments"]},
                         {migration.segment_key(s) for s in union["Segments"]})
        self.assertTrue(all(p.read_bytes() == data for p, data in original.items()))

    def test_reversed_duplicate_and_same_id_different_geometry(self):
        a = segment()
        reversed_a = segment("another", (0, 5), (0, 0))
        b = segment("wall", (1, 0), (1, 5))
        result = migration.union_maps([document(a), document(reversed_a, b)], 47, NOW)
        self.assertEqual(2, len(result["Segments"]))
        self.assertEqual(2, len({s["Id"] for s in result["Segments"]}))
        self.assertEqual({migration.segment_key(a), migration.segment_key(b)},
                         {migration.segment_key(s) for s in result["Segments"]})

    def test_independent_map_ids_and_absolute_source(self):
        self.write(self.root / "preserved/2/radar-maps/48.json", document(segment(), map_id=48))
        self.config["Accounts"][0]["RadarMapDirectory"] = str(self.root / "preserved/1/radar-maps")
        self.write(self.root / "accounts.json", self.config)
        self.assertEqual({"47": 7, "48": 1}, self.prepare()["UnionSegmentCounts"])

    def test_apply_changes_only_radar_references_and_preserves_originals(self):
        original = {p: p.read_bytes() for p in (self.root / "preserved").rglob("*.json")}
        self.prepare()
        backup = migration.apply(self.root, self.plan)
        changed = migration.read_json(self.root / "accounts.json")
        for account in changed["Accounts"]:
            self.assertEqual("radar-maps", account["RadarMapDirectory"])
        for old, new in zip(self.config["Accounts"], changed["Accounts"]):
            new["RadarMapDirectory"] = old["RadarMapDirectory"]
        self.assertEqual(self.config, changed)
        self.assertTrue(all(p.read_bytes() == data for p, data in original.items()))
        self.assertEqual("applied", migration.read_json(backup / "migration.json")["Status"])

    def test_live_client_rejected_before_any_change(self):
        self.prepare()
        before = (self.root / "accounts.json").read_bytes()
        with patch.object(migration, "running_client", return_value=True):
            with self.assertRaisesRegex(ValueError, "Exit"):
                migration.apply(self.root, self.plan)
        self.assertEqual(before, (self.root / "accounts.json").read_bytes())
        self.assertFalse((self.root / "shared-radar-backups").exists())

    def test_concurrent_config_or_map_edits_and_new_files_rejected(self):
        for kind in ("config", "map", "new"):
            with self.subTest(kind=kind):
                plan = self.base / ("plan-" + kind)
                migration.prepare(self.root, plan)
                if kind == "config":
                    self.config["FutureField"]["concurrent"] = 1
                    self.write(self.root / "accounts.json", self.config)
                elif kind == "map":
                    self.write(self.root / "preserved/1/radar-maps/47.json", document(segment("changed", (20, 0), (20, 5))))
                else:
                    self.write(self.root / "preserved/1/radar-maps/48.json", document(segment(), map_id=48))
                with self.assertRaisesRegex(ValueError, "changed since"):
                    migration.apply(self.root, plan)

    def test_tampered_candidate_and_wrong_root_rejected(self):
        self.prepare()
        (self.plan / "47.json").write_bytes(b"{}")
        with self.assertRaisesRegex(ValueError, "candidate was changed"):
            migration.apply(self.root, self.plan)
        with self.assertRaisesRegex(ValueError, "different configuration root"):
            migration.apply(self.base, self.plan)

    def test_failed_account_commit_restores_shared_maps_and_config(self):
        self.write(self.root / "preserved/1/radar-maps/48.json", document(segment(), map_id=48))
        original = {p: p.read_bytes() for p in self.root.rglob("*.json")}
        self.prepare()
        replace = migration.os.replace
        failed = False

        def fail_once(source, target):
            nonlocal failed
            if Path(target).resolve() == (self.root / "accounts.json").resolve() and not failed:
                failed = True
                raise OSError("injected write failure")
            return replace(source, target)

        with patch.object(migration.os, "replace", side_effect=fail_once):
            with self.assertRaisesRegex(OSError, "injected"):
                migration.apply(self.root, self.plan)
        self.assertTrue(all(p.read_bytes() == data for p, data in original.items()))
        self.assertFalse((self.root / "radar-maps/48.json").exists())
        self.assertFalse(list(self.root.rglob("*.tmp")))

    def test_explicit_rollback_is_byte_exact(self):
        original = {p: p.read_bytes() for p in self.root.rglob("*.json")}
        self.prepare()
        backup = migration.apply(self.root, self.plan)
        migration.rollback(backup)
        self.assertTrue(all(p.read_bytes() == data for p, data in original.items()))

    def test_rollback_protects_newer_edits(self):
        self.prepare()
        backup = migration.apply(self.root, self.plan)
        self.write(self.root / "radar-maps/47.json", document(segment("newer")))
        with self.assertRaisesRegex(ValueError, "newer edits"):
            migration.rollback(backup)

    def test_rollback_rejects_target_outside_config(self):
        self.prepare()
        backup = migration.apply(self.root, self.plan)
        record = migration.read_json(backup / "migration.json")
        record["Targets"]["../outside.json"] = str(self.base / "outside.json")
        self.write(backup / "migration.json", record)
        with self.assertRaisesRegex(ValueError, "filename"):
            migration.rollback(backup)

    def test_bad_map_id_version_coordinates_or_short_segment_rejected(self):
        scenarios = [document(segment(), map_id=48), {**document(segment()), "Version": 2},
                     document(segment(end=(float("inf"), 5))), document(segment(end=(0, 0.01)))]
        for doc in scenarios:
            with self.subTest(document=doc), self.assertRaises((ValueError, TypeError)):
                migration.union_maps([doc], 47, NOW)

    def test_map_code_conflict_and_runtime_rounding_collision_rejected(self):
        with self.assertRaisesRegex(ValueError, "MapCode"):
            migration.union_maps([{**document(segment()), "MapCode": "A"},
                                  {**document(segment()), "MapCode": "B"}], 47, NOW)
        with self.assertRaisesRegex(ValueError, "rounding"):
            migration.union_maps([document(segment(), segment("near", (0.000001, 0), (0.000001, 5)))], 47, NOW)

    def test_segment_limit_and_missing_source_rejected(self):
        with self.assertRaisesRegex(ValueError, "5000"):
            migration.union_maps([document(*(segment(str(n), (n, 0), (n, 5)) for n in range(5001)))], 47, NOW)
        self.config["Accounts"][0]["RadarMapDirectory"] = "missing"
        self.write(self.root / "accounts.json", self.config)
        with self.assertRaisesRegex(ValueError, "missing"):
            self.prepare()

    def test_repeated_merge_is_idempotent(self):
        first = migration.union_maps([document(segment(), segment("second", (1, 0), (1, 5)))], 47, NOW)
        second = migration.union_maps([first, document(segment())], 47, NOW)
        self.assertEqual(first, second)

    def test_remigration_never_restores_deleted_walls_from_archives(self):
        self.prepare()
        migration.apply(self.root, self.plan)
        self.write(self.root / "radar-maps/47.json", document(segment("edited")))
        second_plan = self.base / "second-plan"
        result = migration.prepare(self.root, second_plan)
        self.assertEqual({"47": 1}, result["UnionSegmentCounts"])
        self.assertEqual("edited", migration.read_json(second_plan / "47.json")["Segments"][0]["Id"])

    def test_legacy_null_code_and_empty_directory_use_shared_default(self):
        self.config["Accounts"][0]["RadarMapDirectory"] = None
        self.write(self.root / "accounts.json", self.config)
        self.write(self.root / "radar-maps/47.json", {**document(segment("default")), "MapCode": None})
        result = self.prepare()
        self.assertEqual({"47": 6}, result["UnionSegmentCounts"])
        self.assertEqual("", migration.read_json(self.plan / "47.json")["MapCode"])

    def test_string_coordinate_rejected_without_conversion(self):
        invalid = segment()
        invalid["Start"]["X"] = "0"
        with self.assertRaisesRegex(ValueError, "JSON numbers"):
            migration.union_maps([document(invalid)], 47, NOW)


if __name__ == "__main__":
    unittest.main()
