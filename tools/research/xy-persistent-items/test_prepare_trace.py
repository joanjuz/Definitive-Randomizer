"""Preparation tests with synthetic runtime, ROM headers, saves and mocked accreditation."""
import contextlib
import hashlib
import io
import json
import struct
import tempfile
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

import prepare_trace as prep


TITLE = '0004000000055D00'


def rom_bytes(title=TITLE):
    data = bytearray(0x800)
    data[0x100:0x104] = b'NCSD'
    struct.pack_into('<II', data, 0x120, 2, 2)
    data[0x500:0x504] = b'NCCH'
    data[0x518:0x520] = int(title, 16).to_bytes(8, 'little')
    return data


class IniAndPathTests(unittest.TestCase):
    def test_ini_updates_one_exact_key_and_preserves_other_sections(self):
        text = '[First]\nkey=old\nother=preserved\n[Second]\nkey=untouched\n'
        updated = prep.ini_set(text, 'First', 'key', 'new')
        self.assertEqual(updated, '[First]\nkey=new\nother=preserved\n[Second]\nkey=untouched\n')
        self.assertEqual(prep.ini_set(updated, 'First', 'added', 'yes'),
                         '[First]\nkey=new\nother=preserved\nadded=yes\n[Second]\nkey=untouched\n')

    def test_ini_adds_missing_section_and_rejects_duplicate_keys_or_injection(self):
        self.assertEqual(prep.ini_set('[Other]\nx=1\n', 'New', 'y', '2'), '[Other]\nx=1\n\n[New]\ny=2\n')
        for text, value in (('[Target]\nx=1\nx=2\n', '3'), ('[Target]\n', 'a\nb'), ('', 'a\rb')):
            with self.subTest(text=text, value=value), self.assertRaises(ValueError):
                prep.ini_set(text, 'Target', 'x', value)

    def test_ini_rejects_duplicate_section_that_could_override_isolated_storage(self):
        with self.assertRaises(ValueError):
            prep.ini_set('[Storage]\nsd=old\n[Storage]\nsd=external\n', 'Storage', 'sd', 'isolated')

    def test_ini_respects_indented_section_boundary(self):
        self.assertEqual(prep.ini_set('[First]\nx=1\n  [Second]\ny=2\n', 'First', 'y', 'new'),
                         '[First]\nx=1\ny=new\n  [Second]\ny=2\n')

    def test_no_links_checks_target_and_parent_junctions(self):
        for linked in ('self', 'parent'):
            path, parent = Mock(), Mock()
            path.parents = (parent,)
            path.is_symlink.return_value = linked == 'self'
            path.is_junction.return_value = False
            parent.is_symlink.return_value = False
            parent.is_junction.return_value = linked == 'parent'
            with self.subTest(linked=linked), self.assertRaises(ValueError):
                prep.no_links(path)


class RomHeaderTests(unittest.TestCase):
    def test_rom_title_decodes_partition_header_without_modifying_source(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory)/'synthetic.3ds'
            data = rom_bytes()
            path.write_bytes(data)
            self.assertEqual(prep.rom_title(path), TITLE)
            self.assertEqual(path.read_bytes(), data)

    def test_rom_title_rejects_container_partition_and_truncation_bounds(self):
        for mutation in ('short', 'ncsd', 'partition_start', 'partition_size', 'partition_end', 'ncch'):
            with self.subTest(mutation=mutation), tempfile.TemporaryDirectory() as directory:
                data = rom_bytes()
                if mutation == 'short':
                    data = data[:0x1ff]
                elif mutation == 'ncsd':
                    data[0x100:0x104] = b'FAIL'
                elif mutation == 'partition_start':
                    struct.pack_into('<I', data, 0x120, 1)
                elif mutation == 'partition_size':
                    struct.pack_into('<I', data, 0x124, 0)
                elif mutation == 'partition_end':
                    struct.pack_into('<I', data, 0x124, 3)
                else:
                    data[0x500:0x504] = b'FAIL'
                path = Path(directory)/'synthetic.3ds'
                path.write_bytes(data)
                with self.assertRaises(ValueError):
                    prep.rom_title(path)


class PrepareTests(unittest.TestCase):
    def setup_inputs(self, root, save_title=TITLE):
        project, emulator = root/'project', root/'runtime-source'
        stock = project/'ExtractedRomFS/DllBattle.cro'
        stock.parent.mkdir(parents=True)
        stock.write_bytes(b'Synthetic resource, not an installable CRO')
        emulator.mkdir()
        for name in ('citra-qt.exe', 'Qt6Core.dll', 'license.txt', 'qt.conf'):
            (emulator/name).write_bytes(name.encode())
        (emulator/'user').mkdir()
        (emulator/'user/private-config.ini').write_bytes(b'Never import source user directory')
        (emulator/'aes_keys.bin').write_bytes(b'Synthetic forbidden key file')
        (emulator/'plugins').mkdir()
        (emulator/'plugins/platform.dll').write_bytes(b'Synthetic plugin')
        rom = root/'source.3ds'
        rom.write_bytes(rom_bytes())
        save = root/'save-source'/save_title[-8:].lower()/'data/00000001/main'
        save.parent.mkdir(parents=True)
        save.write_bytes(bytes(415232))
        (save.parent.parent/'00000001.metadata').write_bytes(b'Synthetic metadata')
        config = root/'config-source/qt-config.ini'
        config.parent.mkdir()
        config.write_text('[Data%20Storage]\nuse_custom_storage=false\nsdmc_directory=external-sd\n'
                          'nand_directory=external-nand\n', encoding='utf-8')
        self.inputs = (project, rom, emulator, save, config, root/'new-session', 24691)
        self.report = dict(observation_matches=True, cro_hashes_valid=True, title_id=TITLE,
                           sha256={'ExtractedRomFS/DllBattle.cro': hashlib.sha256(stock.read_bytes()).hexdigest().upper()})
        self.profiles = dict(resources=['ExtractedRomFS/DllBattle.cro'])

    def run_prepare(self):
        with patch.object(prep.json, 'loads', return_value=self.profiles), \
             patch.object(prep, 'inspect', return_value=self.report), contextlib.redirect_stdout(io.StringIO()):
            return prep.prepare(*self.inputs)

    def test_prepare_copies_only_isolated_inputs_and_preserves_source_bytes(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            self.setup_inputs(root)
            before = {p: p.read_bytes() for p in root.rglob('*') if p.is_file()}
            manifest = self.run_prepare()
            session = self.inputs[5]
            self.assertFalse(manifest['installable'])
            self.assertEqual(manifest['title_id'], TITLE)
            self.assertEqual(manifest['purpose'], 'xy-persistent-items-original-trace')
            self.assertEqual((session/'game.3ds').read_bytes(), self.inputs[1].read_bytes())
            self.assertTrue((session/'runtime/plugins/platform.dll').is_file())
            self.assertFalse((session/'runtime/user/private-config.ini').exists())
            self.assertFalse((session/'runtime/aes_keys.bin').exists())
            configs = (session/'runtime/user/config/qt-config.ini').read_text()
            self.assertIn('sdmc_directory='+(session/'sd').as_posix()+'/', configs)
            self.assertIn('nand_directory='+(session/'nand').as_posix()+'/', configs)
            self.assertIn('use_gdbstub=true', configs)
            self.assertIn('gdbstub_port=24691', configs)
            self.assertEqual({p: p.read_bytes() for p in before}, before)
            self.assertEqual(json.loads((session/'session.json').read_text())['source_sha256'], manifest['source_sha256'])

    def test_unknown_profile_wrong_save_or_rom_title_reject_before_destination_creation(self):
        for case in ('unknown_profile', 'wrong_save', 'wrong_rom'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                self.setup_inputs(Path(directory), '0004000000055E00' if case == 'wrong_save' else TITLE)
                if case == 'unknown_profile':
                    self.report['observation_matches'] = False
                if case == 'wrong_rom':
                    self.inputs[1].write_bytes(rom_bytes('0004000000055E00'))
                before = {p: p.read_bytes() for p in Path(directory).rglob('*') if p.is_file()}
                with self.assertRaises(ValueError):
                    self.run_prepare()
                self.assertFalse(self.inputs[5].exists())
                self.assertEqual({p: p.read_bytes() for p in before}, before)

    def test_existing_destination_invalid_port_or_missing_metadata_reject_without_inspection(self):
        for case in ('existing', 'port', 'metadata'):
            with self.subTest(case=case), tempfile.TemporaryDirectory() as directory:
                self.setup_inputs(Path(directory))
                if case == 'existing':
                    self.inputs[5].mkdir()
                    (self.inputs[5]/'keep.txt').write_text('Preserve me')
                elif case == 'port':
                    self.inputs = (*self.inputs[:-1], 1023)
                else:
                    (self.inputs[3].parent.parent/'00000001.metadata').unlink()
                with patch.object(prep, 'inspect') as inspect, self.assertRaises(ValueError):
                    prep.prepare(*self.inputs)
                inspect.assert_not_called()
                if case == 'existing':
                    self.assertEqual((self.inputs[5]/'keep.txt').read_text(), 'Preserve me')
                else:
                    self.assertFalse(self.inputs[5].exists())

    def test_session_inside_any_source_directory_rejects_before_writes(self):
        for source_index in (0, 2, 3, 4):
            with self.subTest(source_index=source_index), tempfile.TemporaryDirectory() as directory:
                self.setup_inputs(Path(directory))
                source = self.inputs[source_index]
                if source_index in (3, 4):
                    source = source.parent
                destination = source/'new-session'
                self.inputs = (*self.inputs[:5], destination, self.inputs[6])
                with patch.object(prep, 'inspect') as inspect, self.assertRaises(ValueError):
                    prep.prepare(*self.inputs)
                inspect.assert_not_called()
                self.assertFalse(destination.exists())

    def test_metadata_link_is_checked_before_any_copy_or_inspection(self):
        with tempfile.TemporaryDirectory() as directory:
            self.setup_inputs(Path(directory))
            metadata = self.inputs[3].parent.parent/'00000001.metadata'
            checked = []
            def reject_metadata(path):
                checked.append(path)
                if path == metadata:
                    raise ValueError('Synthetic metadata symlink')
            with patch.object(prep, 'no_links', side_effect=reject_metadata), \
                 patch.object(prep, 'inspect') as inspect, self.assertRaises(ValueError):
                prep.prepare(*self.inputs)
            inspect.assert_not_called()
            self.assertIn(metadata, checked)
            self.assertFalse(self.inputs[5].exists())


if __name__ == '__main__':
    unittest.main()
