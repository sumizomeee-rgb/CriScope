#!/usr/bin/env python3
"""Build, install and check CriScope Unity SDK copies (Python 3.9+, standard library only)."""
import argparse
import datetime
import difflib
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import sys
import tempfile
import uuid
import zipfile

STAMP = '.criscope-sdk.json'
GUID_SEED = uuid.UUID('f8fdb909-41fc-4c57-af3b-ad5a52b51888')
COMMON = ['CriScopeBridge.cs', 'CriScopeMonitor.cs', 'CriScopeNativeRelay.cs', 'CriScopeCaptureSession.cs', 'CriScopeSdk.cs']
DEFAULT_NS = {'runtime': 'CriScope.Unity', 'editor': 'CriScope.Unity.Editor', 'mirror': 'CriScope.Unity'}

def digest(data):
    return hashlib.sha256(data).hexdigest()

def normalized(data):
    return data.decode('utf-8-sig').replace('\r\n', '\n')

def preserve_format(data, previous):
    """Keep unchanged lines byte-for-byte, including mixed EOLs; preserve the host's BOM."""
    raw = previous.decode('utf-8-sig').splitlines(keepends=True)
    before = [line.replace('\r\n','\n') for line in raw]
    after = normalized(data).splitlines(keepends=True)
    crlf = previous.count(b'\r\n'); lf = previous.count(b'\n')-crlf
    ending = '\r\n' if crlf >= lf and crlf else '\n'
    result = []
    for tag,i,j,a,b in difflib.SequenceMatcher(a=before,b=after,autojunk=False).get_opcodes():
        result.extend(raw[i:j] if tag == 'equal' else [line.replace('\n',ending) for line in after[a:b]])
    return (b'\xef\xbb\xbf' if previous.startswith(b'\xef\xbb\xbf') else b'')+''.join(result).encode('utf-8')

def json_bytes(value):
    return (json.dumps(value, ensure_ascii=False, indent=2, sort_keys=True)+'\n').encode('utf-8')

def source_root():
    here = Path(__file__).resolve().parent
    return here if (here/'SDK_MANIFEST.json').is_file() else here.parent/'integrations'/'unity'

def safe_name(name):
    if Path(name).name != name or '/' in name or '\\' in name or name in ('.', '..', STAMP):
        raise ValueError('Invalid SDK file name: '+name)
    return name

def namespace(value):
    if not re.fullmatch(r'[A-Za-z_][A-Za-z_0-9]*(\.[A-Za-z_][A-Za-z_0-9]*)*', value):
        raise ValueError('Invalid C# namespace: '+value)
    return value

def meta(profile, name):
    key = ('runtime' if profile == 'mirror' else profile)+'/'+name
    guid = uuid.uuid5(GUID_SEED, key).hex
    return ('fileFormatVersion: 2\nguid: '+guid+'\nMonoImporter:\n  externalObjects: {}\n  serializedVersion: 2\n'
            '  defaultReferences: []\n  executionOrder: 0\n  icon: {instanceID: 0}\n  userData: \n'
            '  assetBundleName: \n  assetBundleVariant: \n').encode('utf-8')

def load_sdk(root=None):
    root = Path(root) if root else source_root()
    release = root/'SDK_MANIFEST.json'
    if release.is_file():
        document = json.loads(release.read_text(encoding='utf-8'))
        if document.get('format') != 1 or not re.fullmatch(r'\d+\.\d+\.\d+',str(document.get('version',''))):
            raise ValueError('Unsupported SDK manifest')
        profiles = {}
        for profile, info in document['profiles'].items():
            if profile not in DEFAULT_NS: raise ValueError('Unknown SDK profile')
            files = {}
            for name, sha in info['files'].items():
                data = (root/profile/safe_name(name)).read_bytes()
                if digest(data) != sha: raise ValueError('SDK package has been modified: '+profile+'/'+name)
                files[name] = data
            profiles[profile] = files
        return document['version'], document['protocol'], profiles
    version_source = (root/'CriScopeSdk.cs').read_text(encoding='utf-8-sig')
    version = re.search(r'const string Version\s*=\s*"(\d+\.\d+\.\d+)"', version_source).group(1)
    protocol = int(re.search(r'const int ProtocolVersion\s*=\s*(\d+)', version_source).group(1))
    profiles = {}
    for profile in DEFAULT_NS:
        names = COMMON+(['CriScopeEditorCapture.cs','CriScopeEditorWindow.cs'] if profile == 'editor' else ['CriScopeDiagnostics.cs'])
        files = {}
        for name in names:
            if profile == 'mirror' and name != 'CriScopeSdk.cs':
                data = (root/'Mirrors'/name).read_bytes()
            else:
                path = root/('Editor' if name.startswith('CriScopeEditor') else '')/name
                data = path.read_bytes()
            text = normalized(data)
            if profile == 'editor' and not name.startswith('CriScopeEditor'):
                text = text.replace('namespace CriScope.Unity', 'namespace '+DEFAULT_NS[profile])
                text = text.replace('#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS', '#if UNITY_EDITOR')
            files[name] = text.encode('utf-8')
            files[name+'.meta'] = meta(profile, name)
        profiles[profile] = files
    return version, protocol, profiles

def payload(profiles, profile, ns=None, include_window=True):
    ns = namespace(ns or DEFAULT_NS[profile])
    result = {}
    for name, data in profiles[profile].items():
        if not include_window and name.startswith('CriScopeEditorWindow.cs'): continue
        if name.endswith('.cs'):
            data = normalized(data).replace(DEFAULT_NS[profile], ns).encode('utf-8')
        result[name] = data
    return ns, result

def targets(args):
    if args.config:
        document = json.loads(Path(args.config).read_text(encoding='utf-8-sig'))
        return document['targets']
    if not args.target or not args.profile: raise ValueError('Specify --target and --profile, or --config')
    return [dict(target=args.target, profile=args.profile, namespace=args.namespace, includeWindow=not args.no_window)]

def target_file(target, name):
    path = target/safe_name(name)
    if path.is_symlink() or target.resolve() not in path.resolve().parents:
        raise ValueError('SDK target escapes its directory: '+str(path))
    return path

def inspect_target(target, expected, version, profile, ns, adopt=False):
    stamp_path = target/STAMP
    if stamp_path.is_symlink(): raise ValueError('SDK manifest is a symlink')
    stamp = json.loads(stamp_path.read_text(encoding='utf-8')) if stamp_path.is_file() else None
    if stamp and (stamp.get('profile') != profile or stamp.get('namespace') != ns):
        raise ValueError('Installed SDK profile/namespace differs: '+str(target))
    problems = []
    if not stamp: problems.append('missing '+STAMP)
    elif stamp.get('version') != version: problems.append('SDK version '+str(stamp.get('version'))+' -> '+version)
    for name, data in expected.items():
        path = target_file(target, name)
        if not path.is_file(): problems.append('missing '+name); continue
        current = path.read_bytes()
        old = stamp.get('files', {}).get(name) if stamp else None
        if name.endswith('.meta'):
            # Existing Unity asset identities and importer settings belong to the host project.
            if old and digest(current) != old['installed']: problems.append('modified '+name)
            continue
        if normalized(current) == normalized(data): continue
        problems.append('different '+name)
        unchanged = old and (digest(current) == old['installed'] or digest(normalized(current).encode('utf-8')) == old.get('normalized'))
        if not adopt and not unchanged:
            raise ValueError('Local SDK edit; sync refused: '+str(path)+' (first migration: --adopt-existing)')
    return stamp, problems

def atomic_write(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix='.'+path.name+'.', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream: stream.write(data)
        os.replace(name, path)
    finally:
        if os.path.exists(name): os.unlink(name)

def sync_or_check(args):
    version, _, profiles = load_sdk()
    plans = []
    for config in targets(args):
        profile = config['profile']
        ns, files = payload(profiles, profile, config.get('namespace'), config.get('includeWindow', True))
        target = Path(config['target']).absolute()
        if profile == 'editor' and 'editor' not in [p.lower() for p in target.parts]:
            raise ValueError('Editor SDK must be installed below an Editor directory')
        stamp, problems = inspect_target(target, files, version, profile, ns, args.adopt_existing or args.command == 'check')
        plans.append((target, profile, ns, files, stamp, problems))
    # Inspect every configured target before changing any of them.
    if args.command == 'check':
        for target, profile, _, _, _, problems in plans:
            print(('DRIFT' if problems else 'OK')+' '+profile+' '+str(target))
            for problem in problems: print('  '+problem)
        return 1 if any(p[-1] for p in plans) else 0
    for target, profile, ns, files, stamp, problems in plans:
        if not problems:
            print('UNCHANGED '+profile+' '+str(target)); continue
        backup = Path(args.backup_root or Path(tempfile.gettempdir())/'CriScope-SDK-backups')/(
            datetime.datetime.now().strftime('%Y%m%d-%H%M%S')+'-'+uuid.uuid4().hex[:8])
        target.mkdir(parents=True, exist_ok=True)
        records = {}
        for name, data in files.items():
            path = target_file(target, name)
            current = path.read_bytes() if path.is_file() else None
            preserve = current is not None and (name.endswith('.meta') or normalized(current) == normalized(data))
            if current is not None and not preserve:
                backup.mkdir(parents=True, exist_ok=True); shutil.copy2(path, backup/name)
            if not preserve: atomic_write(path, preserve_format(data,current) if current is not None and name.endswith('.cs') else data)
            installed = path.read_bytes()
            records[name] = {'source': digest(data), 'installed': digest(installed), 'preservedMeta': bool(preserve and name.endswith('.meta'))}
            if name.endswith('.cs'): records[name]['normalized'] = digest(normalized(installed).encode('utf-8'))
        if (target/STAMP).exists():
            backup.mkdir(parents=True, exist_ok=True); shutil.copy2(target/STAMP, backup/STAMP)
        atomic_write(target/STAMP, json_bytes(dict(format=1,version=version,profile=profile,namespace=ns,files=records)))
        print('SYNCED '+profile+' '+str(target)+' SDK '+version)
        if backup.exists(): print('  backup: '+str(backup))
    return 0

def pack(output):
    version, protocol, profiles = load_sdk()
    root = source_root()
    name = 'CriScope-UnitySDK-'+version
    document = dict(format=1,version=version,protocol=protocol,profiles={p:dict(namespace=DEFAULT_NS[p],files={n:digest(d) for n,d in files.items()}) for p,files in profiles.items()})
    entries = {'SDK_MANIFEST.json':json_bytes(document), 'install.py':Path(__file__).read_bytes(), 'README.md':(root/'README.md').read_bytes()}
    for profile, files in profiles.items():
        entries.update({profile+'/'+n:data for n,data in files.items()})
    output = Path(output); output.mkdir(parents=True, exist_ok=True)
    archive = output/(name+'.zip')
    candidate = output/(name+'.zip.tmp')
    with zipfile.ZipFile(candidate, 'w', zipfile.ZIP_DEFLATED) as zipped:
        for path,data in sorted(entries.items()):
            info = zipfile.ZipInfo(name+'/'+path, (2026,1,1,0,0,0))
            info.compress_type = zipfile.ZIP_DEFLATED; info.external_attr = 0o644 << 16
            zipped.writestr(info, data)
    if archive.exists():
        if archive.read_bytes() != candidate.read_bytes():
            candidate.unlink()
            raise ValueError('SDK version package already exists with different contents; bump CriScopeSdk.Version')
        candidate.unlink()
    else: os.replace(candidate,archive)
    print(str(archive))
    return archive

def main(argv=None):
    parser=argparse.ArgumentParser(description=__doc__)
    sub=parser.add_subparsers(dest='command', required=True)
    package=sub.add_parser('pack'); package.add_argument('--output', required=True)
    for command in ('sync','check'):
        part=sub.add_parser(command)
        part.add_argument('--profile', choices=DEFAULT_NS); part.add_argument('--target')
        part.add_argument('--namespace'); part.add_argument('--no-window', action='store_true')
        part.add_argument('--config'); part.add_argument('--adopt-existing', action='store_true')
        part.add_argument('--backup-root')
    args=parser.parse_args(argv)
    try:
        if args.command == 'pack': pack(args.output); return 0
        return sync_or_check(args)
    except (ValueError, OSError, KeyError, json.JSONDecodeError) as error:
        print('ERROR: '+str(error), file=sys.stderr); return 2

if __name__ == '__main__': sys.exit(main())
