import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT=Path(__file__).resolve().parents[2]
TOOL=ROOT/'tools/unity-sdk.py'
spec=importlib.util.spec_from_file_location('unity_sdk',TOOL)
sdk=importlib.util.module_from_spec(spec);spec.loader.exec_module(sdk)

class SdkDeliveryTests(unittest.TestCase):
    def run_tool(self,*arguments,ok=True,tool=TOOL):
        result=subprocess.run([sys.executable,str(tool),*map(str,arguments)],capture_output=True,text=True,encoding='utf-8')
        if ok:self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        else:self.assertNotEqual(result.returncode,0,result.stdout)
        return result

    def test_profiles_share_sampler_and_transport(self):
        version,protocol,profiles=sdk.load_sdk(ROOT/'integrations/unity')
        self.assertRegex(version,r'^\d+\.\d+\.\d+$');self.assertEqual(protocol,3)
        for name in sdk.COMMON:
            runtime=profiles['runtime'][name].decode()
            editor=profiles['editor'][name].decode().replace('CriScope.Unity.Editor','CriScope.Unity')
            editor=editor.replace('#if UNITY_EDITOR\n','#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS\n')
            self.assertEqual(runtime,editor,name)
        sampler=profiles['runtime']['CriScopeCaptureSession.cs'].decode()
        self.assertIn('panType = (int)cue.panType',sampler)
        self.assertIn('minAttenuationDistance = cue.pos3dInfo.minAttenuationDistance',sampler)

    def test_pack_is_repeatable_and_installer_works_outside_repo(self):
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            temp=Path(tmp)
            a=sdk.pack(temp/'a');b=sdk.pack(temp/'b')
            self.assertEqual(a.read_bytes(),b.read_bytes())
            with zipfile.ZipFile(a) as archive:archive.extractall(temp/'extract')
            package=temp/'extract'/('CriScope-UnitySDK-'+sdk.load_sdk(ROOT/'integrations/unity')[0])
            self.assertEqual(sdk.load_sdk(package)[0],sdk.load_sdk(ROOT/'integrations/unity')[0])
            target=temp/'game'/'Editor'/'CriScope'
            self.run_tool('sync','--profile','editor','--target',target,tool=package/'install.py')
            self.run_tool('check','--profile','editor','--target',target,tool=package/'install.py')
            (package/'editor/CriScopeSdk.cs').write_text('modified',encoding='utf-8')
            self.run_tool('check','--profile','editor','--target',target,ok=False,tool=package/'install.py')

    def test_published_version_cannot_be_rewritten(self):
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            archive=sdk.pack(tmp);original=archive.read_bytes()
            version,protocol,profiles=sdk.load_sdk(ROOT/'integrations/unity')
            profiles['runtime']['CriScopeSdk.cs']+=b'// changed distribution\n'
            with patch.object(sdk,'load_sdk',return_value=(version,protocol,profiles)):
                with self.assertRaisesRegex(ValueError,'bump CriScopeSdk.Version'):sdk.pack(tmp)
            self.assertEqual(archive.read_bytes(),original)

    def test_preserve_guid_line_endings_and_game_entry(self):
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            target=Path(tmp)/'runtime';target.mkdir()
            _,_,profiles=sdk.load_sdk(ROOT/'integrations/unity')
            original=b'\xef\xbb\xbf'+profiles['runtime']['CriScopeDiagnostics.cs'].replace(b'\n',b'\r\n')
            (target/'CriScopeDiagnostics.cs').write_bytes(original)
            identity=b'fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n'
            (target/'CriScopeDiagnostics.cs.meta').write_bytes(identity)
            entry=target/'GameGmEntry.cs';entry.write_text('// host-owned',encoding='utf-8')
            self.run_tool('sync','--profile','runtime','--target',target)
            self.run_tool('check','--profile','runtime','--target',target)
            self.assertEqual((target/'CriScopeDiagnostics.cs.meta').read_bytes(),identity)
            self.assertEqual((target/'CriScopeDiagnostics.cs').read_bytes(),original)
            self.assertEqual(entry.read_text(),'// host-owned')
            before=(target/'CriScopeSdk.cs').read_bytes()
            with (target/'CriScopeCaptureSession.cs').open('a',encoding='utf-8') as file:file.write('// local edit\n')
            self.run_tool('check','--profile','runtime','--target',target,ok=False)
            self.run_tool('sync','--profile','runtime','--target',target,ok=False)
            self.assertEqual((target/'CriScopeSdk.cs').read_bytes(),before)

    def test_config_keeps_host_window_and_mirror_inert(self):
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            temp=Path(tmp);editor=temp/'Editor'/'CriScope';editor.mkdir(parents=True)
            window=editor/'CriScopeEditorWindow.cs';window.write_text('// project menu',encoding='utf-8')
            mirror=temp/'mirror'
            config=temp/'targets.json';config.write_text(json.dumps({'targets':[
                {'target':str(editor),'profile':'editor','namespace':'Example.Editor','includeWindow':False},
                {'target':str(mirror),'profile':'mirror'}]}),encoding='utf-8')
            self.run_tool('sync','--config',config)
            self.run_tool('check','--config',config)
            self.assertEqual(window.read_text(),'// project menu')
            self.assertIn('namespace Example.Editor',(editor/'CriScopeCaptureSession.cs').read_text(encoding='utf-8'))
            self.assertNotIn('new CriScopeBridge',(mirror/'CriScopeDiagnostics.cs').read_text(encoding='utf-8'))
            self.assertNotIn('TcpClient',(mirror/'CriScopeBridge.cs').read_text(encoding='utf-8'))
            _,_,profiles=sdk.load_sdk(ROOT/'integrations/unity')
            self.assertEqual(profiles['mirror']['CriScopeDiagnostics.cs.meta'],profiles['runtime']['CriScopeDiagnostics.cs.meta'])

    def test_upgrade_preserves_host_format(self):
        import argparse
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            target=Path(tmp)/'runtime'
            self.run_tool('sync','--profile','runtime','--target',target)
            path=target/'CriScopeBridge.cs'
            original=path.read_bytes();path.write_bytes(b'\xef\xbb\xbf'+original.replace(b'\n',b'\r\n'))
            version,protocol,profiles=sdk.load_sdk(ROOT/'integrations/unity')
            profiles['runtime']['CriScopeBridge.cs']+=b'// upstream update\n'
            arguments=argparse.Namespace(command='sync',config=None,profile='runtime',target=str(target),namespace=None,
                no_window=False,adopt_existing=False,backup_root=str(Path(tmp)/'backups'))
            with patch.object(sdk,'load_sdk',return_value=(version,protocol,profiles)):
                sdk.sync_or_check(arguments)
            self.assertEqual(path.read_bytes(),b'\xef\xbb\xbf'+original.replace(b'\n',b'\r\n')+b'// upstream update\r\n')

    def test_invalid_editor_destination_is_rejected(self):
        with tempfile.TemporaryDirectory(prefix='criscope-sdk-') as tmp:
            result=self.run_tool('sync','--profile','editor','--target',Path(tmp)/'Runtime',ok=False)
            self.assertIn('Editor directory',result.stderr)

if __name__=='__main__':unittest.main(verbosity=2)
