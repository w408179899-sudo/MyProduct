"""Create a separate, auditable migration package without touching live clients.

Selected profiles and their path references are pinned under unique names. Per-account
lists, maps and authorization files remain separate; the remaining library selects the
newest UpdatedAt (or original file mtime). No process or hardware is started.
"""
from __future__ import annotations

import argparse
import copy
import hashlib
import json
import os
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
import re
import uuid


def get(document, name, default=None):
    return next((v for k, v in document.items() if k.casefold() == name.casefold()), default)


def put(document, name, value):
    key = next((k for k in document if k.casefold() == name.casefold()), name)
    document[key] = value


def digest(data):
    return hashlib.sha256(data).hexdigest()


def safe_name(name):
    return re.sub(r'[<>:"/\\|?*\x00-\x1f]', '_', name.strip())


def has_physical_key(account):
    key = str(get(account, 'HardwareKey') or '').strip()
    return bool(key) and key != '0' and not key.casefold().startswith('auto')


def write_json(path, document):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(document, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')


@dataclass(frozen=True)
class Captured:
    script: int
    relative: str
    data: bytes
    mtime_ns: int

    def document(self):
        return json.loads(self.data.decode('utf-8-sig'))

    def time(self):
        if self.relative.lower().endswith('.json'):
            value = get(self.document(), 'UpdatedAt')
            if isinstance(value, str):
                try:
                    parsed = datetime.fromisoformat(value.replace('Z', '+00:00'))
                    if parsed.tzinfo is not None and parsed.year > 1:
                        return parsed.timestamp(), 'UpdatedAt'
                except ValueError:
                    pass
        return self.mtime_ns / 1_000_000_000, 'LastWriteTimeUtc'

    def metadata(self):
        timestamp, basis = self.time()
        return dict(script=self.script, relative=self.relative, sha256=digest(self.data),
                    bytes=len(self.data), mtimeNs=self.mtime_ns,
                    selectedTimeUtc=datetime.fromtimestamp(timestamp, timezone.utc).isoformat(),
                    timeBasis=basis)

    def save(self, target):
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(self.data)
        os.utime(target, ns=(self.mtime_ns, self.mtime_ns))


def source_files(root, script):
    directory = root / str(script) / 'config'
    if not directory.is_dir():
        raise ValueError(f'Missing source configuration directory: {directory}')
    result = []
    for path in sorted(directory.rglob('*')):
        relative = path.relative_to(directory)
        if any(part.casefold() == 'workers' for part in relative.parts):
            continue  # Never copy active IPC tokens, startup journals or process identities.
        if path.is_symlink() or path.is_junction():
            raise ValueError(f'Source configuration contains a linked path: {path}')
        if path.is_file():
            result.append((path, relative.as_posix()))
    return result


def capture(root, scripts):
    records = {}
    for script in scripts:
        for path, relative in source_files(root, script):
            before = path.stat()
            data = path.read_bytes()
            after = path.stat()
            if (before.st_mtime_ns, before.st_size) != (after.st_mtime_ns, after.st_size):
                raise RuntimeError(f'Source changed while reading: {path}; rerun into a new output folder.')
            key = (script, relative.casefold())
            if key in records:
                raise ValueError(f'Case-insensitive source name collision: {path}')
            records[key] = Captured(script, relative, data, after.st_mtime_ns)
    return records


def verify_source_unchanged(root, scripts, records):
    current = {(script, relative.casefold()): path
               for script in scripts for path, relative in source_files(root, script)}
    if set(current) != set(records):
        raise RuntimeError('Source file set changed during capture; migration is incomplete.')
    for key, path in current.items():
        if digest(path.read_bytes()) != digest(records[key].data):
            raise RuntimeError(f'Source changed during migration: {path}; migration is incomplete.')


def is_library(relative):
    parts = relative.lower().split('/')
    return (parts[0] in ('paths', 'profiles', 'radar-maps') and relative.lower().endswith('.json')) \
        or relative.lower() in ('bag-cleanup-name-lists.json', 'bag-cleanup-excluded.txt')


def merge_libraries(records, output):
    groups = {}
    for record in records.values():
        if is_library(record.relative):
            groups.setdefault(record.relative.casefold(), []).append(record)
    decisions = []
    for key, candidates in sorted(groups.items()):
        # Original mtime, then source number break equal saved times deterministically.
        ordered = sorted(candidates, key=lambda r: (r.time()[0], r.mtime_ns, r.script))
        winner = ordered[-1]
        winner.save(output / 'config' / winner.relative)
        different = len({digest(r.data) for r in candidates}) > 1
        decisions.append(dict(target=winner.relative, winner=winner.metadata(),
                              conflicting=different,
                              tiedSavedTime=different and sum(r.time()[0] == winner.time()[0] for r in candidates) > 1,
                              candidates=[r.metadata() for r in candidates]))
    return decisions


def walk_path_references(value):
    if isinstance(value, dict):
        for name, item in value.items():
            if name.lower().endswith('pathname') and isinstance(item, str) and item.strip():
                yield value, name, item
            else:
                yield from walk_path_references(item)
    elif isinstance(value, list):
        for item in value:
            yield from walk_path_references(item)


def unique_alias(kind, script, original, output):
    stem = f'保留_脚本{script}_{original}'
    name = stem
    index = 2
    while (output / 'config' / kind / (safe_name(name) + '.json')).exists():
        name = f'{stem}_{index}'
        index += 1
    return name


def pin_account(script, records, output, source_root=None):
    original_doc = records[(script, 'accounts.json')].document()
    all_accounts = get(original_doc, 'Accounts', [])
    bound = [a for a in all_accounts if has_physical_key(a)]
    if len(bound) != 1:
        raise ValueError(f'Script {script}: expected one physically bound account, found {len(bound)}.')
    account = copy.deepcopy(bound[0])
    settings = copy.deepcopy(get(account, 'ScriptSettings') or {})
    profile_name = get(settings, 'ProfileName') or get(account, 'ProfileName', '')
    profile_record = records.get((script, f'profiles/{safe_name(profile_name)}.json'.casefold()))
    if profile_record:
        profile = profile_record.document()
        settings = copy.deepcopy(get(profile, 'Settings') or {})
        put(settings, 'ProfileName', get(profile, 'Name') or profile_name)
    else:
        if not settings:
            raise ValueError(f'Script {script}: neither selected profile nor embedded settings are available.')
        profile = dict(Version=1, Name=profile_name, Settings=settings)

    preserved = output / 'config' / 'preserved' / str(script)
    preserved.mkdir(parents=True, exist_ok=True)
    # Keep the raw effective profile before changing names; .NET performs the usual
    # defaults and list normalization when independently auditing this package.
    write_json(preserved / 'effective-before.json', dict(
        SourceAccount=bound[0], SelectedProfile=profile_name,
        ProfileFound=profile_record is not None, SettingsBeforeNameLists=settings))

    aliases = {}
    missing = []
    for parent, key, name in list(walk_path_references(settings)):
        if name not in aliases:
            alias = unique_alias('paths', script, name, output)
            aliases[name] = alias
            source = records.get((script, f'paths/{safe_name(name)}.json'.casefold()))
            if source:
                path_doc = source.document()
                put(path_doc, 'Name', alias)
                write_json(output / 'config' / 'paths' / (safe_name(alias) + '.json'), path_doc)
            else:
                # Pin missing references too: a file from another account must not
                # silently fill a pre-existing missing/inactive route.
                missing.append(name)
        parent[key] = aliases[name]

    profile_alias = unique_alias('profiles', script, profile_name or '当前设置', output)
    put(settings, 'ProfileName', profile_alias)
    put(profile, 'Name', profile_alias)
    put(profile, 'Settings', settings)
    write_json(output / 'config' / 'profiles' / (safe_name(profile_alias) + '.json'), profile)
    put(account, 'ScriptSettings', copy.deepcopy(settings))
    put(account, 'ProfileName', profile_alias)
    put(account, 'AccountName', f'脚本{script}')
    put(account, 'InstanceId', str(uuid.uuid4()))
    for name in ('MainMode', 'CombatMode'):
        value = get(settings, name)
        if value is not None:
            put(account, name, value)
    for name in ('RevivePathName', 'CombatPathName', 'MaintenancePathName'):
        put(account, name, get(get(settings, 'Paths', {}), name, ''))
    if not get(account, 'KmBox'):
        put(account, 'KmBox', records[(script, 'kmbox-net.json')].document())

    # The six clients' sources are intentionally not replaced by the merged library.
    for record in records.values():
        if record.script == script and (record.relative.casefold().startswith('radar-maps/')
                or record.relative.casefold() in ('bag-cleanup-name-lists.json', 'bag-cleanup-excluded.txt',
                                                  'license.dat', 'owner-license.json')):
            record.save(preserved / record.relative)
    if (script, 'bag-cleanup-name-lists.json') not in records:
        raise ValueError(f'Script {script}: expected current JSON name lists; preserve manually before migrating.')
    # Never silently substitute a sibling file for an external selected resource.
    for field, sibling in (('LicenseCredentialPath', 'license.dat'),
                           ('OwnerLicenseGrantPath', 'owner-license.json'),
                           ('BagCleanupNameListPath', 'bag-cleanup-name-lists.json'),
                           ('RadarMapDirectory', 'radar-maps')):
        original = str(get(account, field, '') or '')
        if original and source_root is not None:
            source_directory = source_root / str(script) / 'config'
            selected = (source_directory / original).resolve()
            if selected != (source_directory / sibling).resolve():
                raise ValueError(f'Script {script}: external {field} requires explicit preservation.')
    put(account, 'LicenseCredentialPath', f'preserved/{script}/license.dat')
    put(account, 'OwnerLicenseGrantPath', f'preserved/{script}/owner-license.json')
    put(account, 'BagCleanupNameListPath', f'preserved/{script}/bag-cleanup-name-lists.json')
    put(account, 'RadarMapDirectory', f'preserved/{script}/radar-maps')
    (preserved / 'radar-maps').mkdir(exist_ok=True)
    detail = dict(script=script, accountName=get(account, 'AccountName'),
                  sourceAccountName=get(bound[0], 'AccountName'), selectedProfile=profile_name,
                  preservedProfile=profile_alias, selectedProfileFound=profile_record is not None,
                  pathAliases=aliases, preExistingMissingPaths=missing,
                  skippedUnboundAccounts=[get(a, 'AccountName') for a in all_accounts if a not in bound],
                  credentialPresent=(preserved / 'license.dat').exists(),
                  ownerGrantPresent=(preserved / 'owner-license.json').exists())
    return account, detail


def verify_package(output, records, scripts, decisions, details):
    for record in records.values():
        backup = output / 'source-backup' / str(record.script) / 'config' / record.relative
        if backup.read_bytes() != record.data:
            raise AssertionError(f'Backup differs: {backup}')
    for decision in decisions:
        target = output / 'config' / decision['target']
        if digest(target.read_bytes()) != decision['winner']['sha256']:
            raise AssertionError(f'Merged winner differs: {target}')
    for detail in details:
        script = detail['script']
        for name, alias in detail['pathAliases'].items():
            source = records.get((script, f'paths/{safe_name(name)}.json'.casefold()))
            target = output / 'config' / 'paths' / (safe_name(alias) + '.json')
            if source:
                expected = source.document()
                put(expected, 'Name', alias)
                if json.loads(target.read_text(encoding='utf-8')) != expected:
                    raise AssertionError(f'Preserved path differs: {target}')
            elif target.exists():
                raise AssertionError(f'A missing source path was silently replaced: {target}')
        for record in records.values():
            if record.script == script and (record.relative.casefold().startswith('radar-maps/')
                    or record.relative.casefold() in ('bag-cleanup-name-lists.json', 'bag-cleanup-excluded.txt',
                                                      'license.dat', 'owner-license.json')):
                if (output / 'config' / 'preserved' / str(script) / record.relative).read_bytes() != record.data:
                    raise AssertionError(f'Preserved account file differs: script {script}/{record.relative}')
    accounts = json.loads((output / 'config' / 'accounts.json').read_text(encoding='utf-8'))['Accounts']
    if len(accounts) != len(scripts) or len({a['InstanceId'] for a in accounts}) != len(scripts):
        raise AssertionError('Account count or instance uniqueness is invalid.')


def prepare(source, output, scripts=range(1, 7)):
    source, output = Path(source).resolve(), Path(output).resolve()
    scripts = tuple(scripts)
    if output.exists():
        raise ValueError(f'Output already exists; nothing overwritten: {output}')
    if source == output or source in output.parents:
        raise ValueError('Output must be outside the live script directory.')
    records = capture(source, scripts)
    output.mkdir(parents=True)
    marker = output / 'INCOMPLETE.txt'
    marker.write_text('Do not use until migration validation completes.\n', encoding='utf-8')
    for record in records.values():
        record.save(output / 'source-backup' / str(record.script) / 'config' / record.relative)
    decisions = merge_libraries(records, output)
    accounts, details = [], []
    for script in scripts:
        account, detail = pin_account(script, records, output, source)
        accounts.append(account)
        details.append(detail)
    write_json(output / 'config' / 'accounts.json', dict(Version=1, Accounts=accounts))
    verify_package(output, records, scripts, decisions, details)
    verify_source_unchanged(source, scripts, records)
    manifest = dict(version=1, capturedAtUtc=datetime.now(timezone.utc).isoformat(),
                    sourceRoot=str(source), outputRoot=str(output),
                    timePolicy='Valid timezone-aware UpdatedAt; otherwise original LastWriteTimeUtc. Ties: mtime, then script number.',
                    originalFileCount=len(records), originalFiles=[r.metadata() for r in records.values()],
                    accounts=details, mergeDecisions=decisions,
                    verification=dict(sourceBytesUnchanged=True, backupBytesMatch=True,
                                      selectedLibraryBytesMatch=True, preservedAccountBytesMatch=True))
    write_json(output / 'manifest.json', manifest)
    rows = '\n'.join(f"| {a['script']} | {a['selectedProfile']} | {len(a['pathAliases'])} | "
                     f"{', '.join(a['preExistingMissingPaths']) or '无'} |" for a in details)
    (output / '迁移说明.md').write_text(
        '# 六账号配置迁移副本\n\n'
        '原六个客户端未修改、未停止。此目录保存准备迁入新客户端的配置，不会自动启动账号。\n\n'
        '`config/accounts.json` 包含脚本1至脚本6；当前方案和路径使用“保留_脚本N_”前缀。'
        '名单、地图和授权来源分别保存在 `config/preserved/N`，不受共享库选新版本影响。\n\n'
        '其余同名资料按配置内 UpdatedAt 选最新；没有有效保存时间则按原文件修改时间。'
        '相同保存时间按原修改时间、目录编号决胜，所有候选原件保留在 source-backup，选择明细见 manifest.json。\n\n'
        '| 原目录 | 当前方案 | 保留路径引用数 | 原来已缺失的路径 |\n| --- | --- | --- | --- |\n' + rows + '\n\n'
        '缺失路径没有猜测补齐，也没有让其他账号的同名文件顶替。未绑定硬件的占位账号仅保留在原始备份。\n\n'
        '授权文件只保留原件，仍需正常验证；没有 license.dat 的账号保留其 owner-license.json，未创建新凭据。'
        '凭据及配置备份仅供当前机器/用户迁移，不应公开上传。\n\n'
        '该副本依据保存到磁盘的配置及原启动加载顺序生成，不读取运行进程内存；未保存的界面改动不包含在内。\n',
        encoding='utf-8')
    marker.unlink()
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', required=True)
    parser.add_argument('--output', required=True)
    args = parser.parse_args()
    result = prepare(args.source, args.output)
    print(json.dumps(dict(output=result['outputRoot'], accounts=len(result['accounts']),
                          sourceFiles=result['originalFileCount'],
                          mergedFiles=len(result['mergeDecisions']),
                          conflicts=sum(d['conflicting'] for d in result['mergeDecisions']),
                          preExistingMissingPaths=[dict(script=a['script'], paths=a['preExistingMissingPaths'])
                                                   for a in result['accounts'] if a['preExistingMissingPaths']]),
                     ensure_ascii=False, indent=2))
