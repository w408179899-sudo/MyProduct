"""Read-only migration guarantees using isolated synthetic clients, never desktop data."""
import copy
import json
import os
from pathlib import Path
import tempfile
import unittest

import prepare_six_account_migration as migration


class SixAccountMigrationTests(unittest.TestCase):
    ROUTES = {
        'RevivePathName': '复活', 'CombatPathName': '战斗',
        'MaintenancePathName': '清包', 'GatherPathName': '采集',
        'AuctionPathName': '拍卖', 'StallPathName': '摆摊',
    }

    def setUp(self):
        temporary_root = Path(__file__).resolve().parents[2] / '.tmp' / 'multi-account-tests' / 'migration-unit'
        temporary_root.mkdir(parents=True, exist_ok=True)
        self.temporary = tempfile.TemporaryDirectory(dir=temporary_root)
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.source = self.root / 'source'
        self.output = self.root / 'prepared'
        self.profile_name = '当前方案'
        for number in range(1, 7):
            config = self.source / str(number) / 'config'
            settings = {
                'ProfileName': self.profile_name, 'MainMode': 'CustomCombat', 'CombatMode': 'Stationary',
                'Paths': dict(self.ROUTES, TownReturnKey='OemPlus', DeathReviveClickX=680 + number,
                              DeathReviveClickY=460 + number, BagCleanupReturnByReversePath=False),
                'Combat': {'StationaryCombatRadius': 30 + number},
                'Maintenance': {'BagCleanupExcludedItemNames': ['embedded profile list']},
            }
            embedded = copy.deepcopy(settings)
            embedded['Combat']['StationaryCombatRadius'] = 999
            embedded['Paths']['RevivePathName'] = 'outdated embedded route'
            account = {'AccountName': 'account1', 'HardwareKey': f'port:mock-{number}',
                       'VmmDeviceName': f'fpga://devindex={number}', 'Enabled': True,
                       'ProfileName': self.profile_name, 'ScriptSettings': embedded,
                       'RevivePathName': 'old legacy route', 'CombatPathName': 'old legacy combat',
                       'MaintenancePathName': 'old legacy maintenance'}
            self.write(config / 'accounts.json', {'Version': 1, 'Accounts': [account]})
            self.write(config / 'profiles' / (self.profile_name + '.json'),
                       {'Version': 1, 'Name': self.profile_name, 'Settings': settings,
                        'UpdatedAt': f'2026-09-{number:02d}T10:00:00+08:00'})
            for index, name in enumerate(self.ROUTES.values()):
                self.write(config / 'paths' / (name + '.json'), {
                    'Version': 1, 'Name': name, 'MapId': 220050000,
                    'UpdatedAt': f'2026-09-{number:02d}T10:00:00+08:00',
                    'CleanupNpcName': f'cleanup-{number}', 'AuctionNpcName': f'auction-{number}',
                    'BoundStationaryCombatRadius': 35 + number,
                    'BagCleanupSellItemClickX': 200, 'BagCleanupSellItemClickY': 300 + number,
                    'BagCleanupSellButtonClickX': 283, 'BagCleanupSellButtonClickY': 477,
                    'Points': [{'Index': 0, 'X': number * 100 + index, 'Y': 2.5, 'Z': 3.5,
                                'GatherActions': [{'TargetName': f'gather-{number}', 'RepeatCount': number}]}],
                })
            self.write(config / 'kmbox-net.json', {'IpAddress': '127.0.0.1', 'Port': 40000 + number, 'Mac': '12345678'})
            # Deliberately different formatting and BOM: preservation must compare original bytes.
            (config / 'bag-cleanup-name-lists.json').write_bytes(b'\xef\xbb\xbf' + json.dumps({
                'version': 2, 'whitelist': [f'protect-{number}'], 'blacklist': [f'discard-{number}'],
                'stall': [{'Name': f'stall-{number}', 'Price': number}],
                'auctionHouse': [{'Name': f'auction-{number}', 'Price': number * 10}],
            }, indent=1).replace('\n', '\r\n').encode('utf-8'))
            self.write(config / 'radar-maps' / '220050000.json',
                       {'MapId': 220050000, 'Segments': [{'owner': number}],
                        'UpdatedAt': f'2026-09-{number:02d}T10:00:00+08:00'})
            (config / 'license.dat').write_bytes(f'test-only-credential-{number}\r\n'.encode())
            self.write(config / 'owner-license.json', {'testOnlyOwnerGrant': number})

    @staticmethod
    def write(path, document, mtime=None):
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(document, ensure_ascii=False, indent=2), encoding='utf-8')
        if mtime is not None:
            os.utime(path, (mtime, mtime))

    @staticmethod
    def read(path):
        return json.loads(path.read_text(encoding='utf-8-sig'))

    def snapshot(self):
        return {str(path.relative_to(self.source)): (path.read_bytes(), path.stat().st_mtime_ns)
                for path in self.source.rglob('*') if path.is_file()}

    def prepare(self):
        before = self.snapshot()
        manifest = migration.prepare(self.source, self.output)
        self.assertEqual(before, self.snapshot(), 'preparation must never change source contents or timestamps')
        self.assertFalse((self.output / 'INCOMPLETE.txt').exists())
        accounts = self.read(self.output / 'config' / 'accounts.json')['Accounts']
        return manifest, accounts

    def test_effective_profile_overrides_embedded_and_pins_six_routes_with_metadata(self):
        manifest, accounts = self.prepare()
        self.assertEqual(6, len(accounts))
        for number, account in enumerate(accounts, 1):
            settings = account['ScriptSettings']
            self.assertEqual(30 + number, settings['Combat']['StationaryCombatRadius'])
            self.assertEqual(680 + number, settings['Paths']['DeathReviveClickX'])
            self.assertEqual(460 + number, settings['Paths']['DeathReviveClickY'])
            self.assertEqual('OemPlus', settings['Paths']['TownReturnKey'])
            self.assertFalse(settings['Paths']['BagCleanupReturnByReversePath'])
            profile = self.read(self.output / 'config' / 'profiles' / (account['ProfileName'] + '.json'))
            self.assertEqual(account['ProfileName'], profile['Name'])
            self.assertEqual(account['ProfileName'], profile['Settings']['ProfileName'])
            self.assertEqual(settings, profile['Settings'])
            self.assertEqual(6, len(manifest['accounts'][number - 1]['pathAliases']))
            for key, original in self.ROUTES.items():
                alias = settings['Paths'][key]
                source = self.read(self.source / str(number) / 'config' / 'paths' / (original + '.json'))
                expected = dict(source, Name=alias)
                self.assertEqual(expected, self.read(self.output / 'config' / 'paths' / (alias + '.json')))
                if key in ('RevivePathName', 'CombatPathName', 'MaintenancePathName'):
                    self.assertEqual(alias, account[key])

    def test_library_uses_updated_at_before_copy_mtime_and_falls_back_when_missing(self):
        first = self.source / '1' / 'config' / 'paths'
        second = self.source / '2' / 'config' / 'paths'
        saved_new = {'Name': 'saved-time', 'UpdatedAt': '2026-09-20T12:00:00+08:00', 'Points': [1]}
        copied_new = {'Name': 'saved-time', 'UpdatedAt': '2026-09-19T12:00:00+08:00', 'Points': [2]}
        self.write(first / 'saved-time.json', saved_new, mtime=1000000000)
        self.write(second / 'saved-time.json', copied_new, mtime=1900000000)
        self.write(first / 'fallback-time.json', {'Name': 'fallback-time', 'Points': [1]}, mtime=1000000000)
        self.write(second / 'fallback-time.json', {'Name': 'fallback-time', 'Points': [2]}, mtime=1900000000)
        manifest, _ = self.prepare()
        decisions = {item['target']: item for item in manifest['mergeDecisions']}
        self.assertEqual(1, decisions['paths/saved-time.json']['winner']['script'])
        self.assertEqual('UpdatedAt', decisions['paths/saved-time.json']['winner']['timeBasis'])
        self.assertEqual(saved_new, self.read(self.output / 'config' / 'paths' / 'saved-time.json'))
        self.assertEqual(2, decisions['paths/fallback-time.json']['winner']['script'])
        self.assertEqual('LastWriteTimeUtc', decisions['paths/fallback-time.json']['winner']['timeBasis'])

    def test_account_lists_maps_and_grants_remain_exact_independent_bytes(self):
        _, accounts = self.prepare()
        for number, account in enumerate(accounts, 1):
            source = self.source / str(number) / 'config'
            preserved = self.output / 'config' / 'preserved' / str(number)
            for relative in ('bag-cleanup-name-lists.json', 'radar-maps/220050000.json', 'license.dat', 'owner-license.json'):
                self.assertEqual((source / relative).read_bytes(), (preserved / relative).read_bytes())
            self.assertEqual(f'preserved/{number}/bag-cleanup-name-lists.json', account['BagCleanupNameListPath'])
            self.assertEqual(f'preserved/{number}/radar-maps', account['RadarMapDirectory'])
            self.assertEqual(f'preserved/{number}/owner-license.json', account['OwnerLicenseGrantPath'])
            self.assertEqual(f'preserved/{number}/license.dat', account['LicenseCredentialPath'])
        self.assertEqual(6, self.read(self.output / 'config' / 'radar-maps' / '220050000.json')['Segments'][0]['owner'])
        self.assertEqual(1, self.read(self.output / 'config' / 'preserved' / '1' / 'radar-maps' / '220050000.json')['Segments'][0]['owner'])

    def test_placeholder_accounts_are_only_in_source_backup(self):
        path = self.source / '4' / 'config' / 'accounts.json'
        document = self.read(path)
        placeholders = [{'AccountName': f'placeholder-{index}', 'HardwareKey': hardware}
                        for index, hardware in enumerate(('', '0', 'auto', ' auto ', None))]
        document['Accounts'].extend(placeholders)
        self.write(path, document)
        manifest, accounts = self.prepare()
        self.assertEqual(6, len(accounts))
        self.assertEqual([item['AccountName'] for item in placeholders], manifest['accounts'][3]['skippedUnboundAccounts'])
        self.assertEqual(document, self.read(self.output / 'source-backup' / '4' / 'config' / 'accounts.json'))

    def test_missing_reference_stays_missing_even_when_other_source_has_same_name(self):
        original = self.ROUTES['CombatPathName']
        (self.source / '2' / 'config' / 'paths' / (original + '.json')).unlink()
        manifest, accounts = self.prepare()
        alias = accounts[1]['ScriptSettings']['Paths']['CombatPathName']
        self.assertIn(original, manifest['accounts'][1]['preExistingMissingPaths'])
        self.assertTrue((self.output / 'config' / 'paths' / (original + '.json')).exists())
        self.assertFalse((self.output / 'config' / 'paths' / (alias + '.json')).exists())
        self.assertEqual(alias, accounts[1]['CombatPathName'])

    def test_output_existing_or_inside_source_is_rejected_without_changes(self):
        before = self.snapshot()
        self.output.mkdir()
        sentinel = self.output / 'existing.txt'
        sentinel.write_bytes(b'leave untouched')
        with self.assertRaises(ValueError):
            migration.prepare(self.source, self.output)
        self.assertEqual(b'leave untouched', sentinel.read_bytes())
        internal = self.source / 'new-output'
        with self.assertRaises(ValueError):
            migration.prepare(self.source, internal)
        self.assertFalse(internal.exists())
        self.assertEqual(before, self.snapshot())

    def test_missing_selected_profile_falls_back_to_embedded_settings(self):
        (self.source / '3' / 'config' / 'profiles' / (self.profile_name + '.json')).unlink()
        manifest, accounts = self.prepare()
        self.assertFalse(manifest['accounts'][2]['selectedProfileFound'])
        self.assertEqual(999, accounts[2]['ScriptSettings']['Combat']['StationaryCombatRadius'])
        self.assertIn('outdated embedded route', manifest['accounts'][2]['preExistingMissingPaths'])

    def test_workers_are_excluded_but_original_config_sources_are_backed_up(self):
        workers = self.source / '1' / 'config' / 'workers' / 'instance'
        self.write(workers / 'launch.json', {'testOnlyToken': 'must not migrate live process metadata'})
        arbitrary = self.source / '1' / 'config' / 'accounts.old.bak'
        arbitrary.write_bytes(b'original historical backup')
        manifest, _ = self.prepare()
        self.assertFalse((self.output / 'source-backup' / '1' / 'config' / 'workers').exists())
        self.assertFalse(any('/workers/' in '/' + item['relative'].lower() for item in manifest['originalFiles']))
        self.assertEqual(arbitrary.read_bytes(), (self.output / 'source-backup' / '1' / 'config' / arbitrary.name).read_bytes())

    def test_external_selected_resources_are_rejected_instead_of_substituting_siblings(self):
        account_path = self.source / '1' / 'config' / 'accounts.json'
        original = self.read(account_path)
        for field, name in (('LicenseCredentialPath', 'license.dat'),
                            ('OwnerLicenseGrantPath', 'owner-license.json'),
                            ('BagCleanupNameListPath', 'bag-cleanup-name-lists.json'),
                            ('RadarMapDirectory', 'radar-maps')):
            with self.subTest(field=field):
                document = copy.deepcopy(original)
                external = self.root / 'external' / name
                document['Accounts'][0][field] = str(external)
                self.write(account_path, document)
                before = self.snapshot()
                output = self.root / ('rejected-' + field)
                with self.assertRaisesRegex(ValueError, 'external ' + field):
                    migration.prepare(self.source, output)
                self.assertEqual(before, self.snapshot())
                self.assertTrue((output / 'INCOMPLETE.txt').exists())
                self.assertFalse((output / 'manifest.json').exists())

    def test_explicit_original_sibling_resources_are_preserved(self):
        account_path = self.source / '1' / 'config' / 'accounts.json'
        document = self.read(account_path)
        account = document['Accounts'][0]
        account['LicenseCredentialPath'] = str(account_path.parent / 'license.dat')
        account['OwnerLicenseGrantPath'] = 'owner-license.json'
        account['BagCleanupNameListPath'] = str(account_path.parent / 'bag-cleanup-name-lists.json')
        account['RadarMapDirectory'] = 'radar-maps'
        self.write(account_path, document)
        _, accounts = self.prepare()
        self.assertEqual('preserved/1/license.dat', accounts[0]['LicenseCredentialPath'])

    def test_missing_credentials_retain_owner_grant_without_fabricating_new_credentials(self):
        for number in (4, 6):
            (self.source / str(number) / 'config' / 'license.dat').unlink()
        manifest, _ = self.prepare()
        for number in (4, 6):
            detail = manifest['accounts'][number - 1]
            self.assertFalse(detail['credentialPresent'])
            self.assertTrue(detail['ownerGrantPresent'])
            self.assertFalse((self.output / 'config' / 'preserved' / str(number) / 'license.dat').exists())


if __name__ == '__main__':
    unittest.main()
