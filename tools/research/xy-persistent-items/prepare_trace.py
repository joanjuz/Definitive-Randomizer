"""Prepare (never launch) an original X/Y Citra Qt tracing session in a NEW directory.

Copies only runtime files, one ROM, one save plus metadata, and pinned research
dependencies. No installed mods, updates, cheats, keys or savestates are imported.
The session and its memory traces are private and must never be committed.
"""
import argparse
import hashlib
import json
import re
import shutil
import struct
from pathlib import Path

from inspect_persistent_items import inspect, require, sha


def file_sha(path):
    with open(path, 'rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def no_links(path):
    for part in (path, *path.parents):
        require(not part.is_symlink() and not getattr(part, 'is_junction', lambda: False)(),
                'Session inputs/outputs must not pass through links or junctions')


def ini_set(text, section, key, value):
    require('\n' not in value and '\r' not in value, 'Invalid INI value')
    lines = text.splitlines()
    sections = [i for i, line in enumerate(lines) if line.strip() == f'[{section}]']
    require(len(sections) <= 1, 'Duplicate INI section')
    begin = sections[0] if sections else None
    if begin is None:
        return text.rstrip()+f'\n\n[{section}]\n{key}={value}\n'
    end = next((i for i in range(begin+1, len(lines)) if lines[i].strip().startswith('[')), len(lines))
    found = [i for i in range(begin+1, end) if lines[i].split('=', 1)[0].strip() == key]
    require(len(found) <= 1, 'Duplicate INI key')
    if found:
        lines[found[0]] = f'{key}={value}'
    else:
        lines.insert(end, f'{key}={value}')
    return '\n'.join(lines)+'\n'


def rom_title(path):
    with open(path, 'rb') as rom:
        header = rom.read(0x200)
        require(len(header) == 0x200 and header[0x100:0x104] == b'NCSD', 'Expected a decrypted NCSD .3ds copy')
        offset, size = struct.unpack_from('<II', header, 0x120)
        require(offset >= 2 and size > 0 and (offset+size)*0x200 <= path.stat().st_size, 'NCSD partition bounds')
        rom.seek(offset*0x200)
        ncch = rom.read(0x200)
        require(len(ncch) == 0x200 and ncch[0x100:0x104] == b'NCCH', 'NCCH partition header missing')
        return ncch[0x118:0x120][::-1].hex().upper()


def prepare(project, rom, emulator, save, config, session, port):
    project, rom, emulator, save, config, session = [p.absolute() for p in (project, rom, emulator, save, config, session)]
    for path in (project, rom, emulator, save, config, session):
        no_links(path)
    require(not session.exists(), 'Session destination already exists; never overwrite an earlier experiment')
    require(not any(session.is_relative_to(source) for source in (project, emulator, save.parent.parent, config.parent)),
            'Session must be outside every source project, emulator, save and configuration directory')
    require(1024 <= port <= 65535, 'Invalid local debugger port')
    require(save.name == 'main' and save.parent.name == '00000001', 'Expected an X/Y main save')
    metadata = save.parent.parent/'00000001.metadata'
    no_links(metadata)
    require(save.stat().st_size == 415232 and metadata.is_file(), 'Save or matching archive metadata missing')
    profiles = json.loads(Path(__file__).with_name('observed_profiles.json').read_text(encoding='utf-8'))
    report = inspect(project, profiles)
    require(report['observation_matches'] and report['cro_hashes_valid'], 'Project does not match an observed original research set')
    title = report['title_id']
    require(rom_title(rom) == title, 'ROM and project title differ')
    require(save.parent.parent.parent.name.lower() == title[-8:].lower(), 'Save belongs to the other title')
    executable = emulator/'citra-qt.exe'
    require(executable.is_file(), 'Expected existing Citra Qt runtime')
    original = {str(path): file_sha(path) for path in (rom, save, metadata, config, executable)}
    # All validation above is read-only. Every write below is confined to a new session.
    session.mkdir(parents=True)
    runtime = session/'runtime'
    runtime.mkdir()
    for file in emulator.iterdir():
        if file.is_file() and file.suffix.lower() in ('.exe', '.dll', '.txt', '.conf'):
            no_links(file)
            shutil.copyfile(file, runtime/file.name)
    plugins = emulator/'plugins'
    if plugins.is_dir():
        for child in plugins.rglob('*'):
            no_links(child)
        shutil.copytree(plugins, runtime/'plugins')
    shutil.copyfile(rom, session/'game.3ds')
    require(file_sha(session/'game.3ds') == original[str(rom)], 'ROM copy verification failed')
    rel = Path('Nintendo 3DS')/('0'*32)/('0'*32)/'title/00040000'/title[-8:].lower()/'data'
    data = session/'sd'/rel
    (data/'00000001').mkdir(parents=True)
    shutil.copyfile(save, data/'00000001/main')
    shutil.copyfile(metadata, data/'00000001.metadata')
    require(file_sha(data/'00000001/main') == original[str(save)], 'Save copy verification failed')
    (session/'nand').mkdir()
    (session/'evidence').mkdir()
    for name in profiles['resources']:
        source = project/name
        no_links(source)
        destination = session/'stock'/name
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(source, destination)
        require(file_sha(destination) == report['sha256'][name], 'Research dependency changed during preparation')
    qt = config.read_text(encoding='utf-8-sig')
    for section, key, value in (
        ('Data%20Storage', 'use_custom_storage', 'true'),
        ('Data%20Storage', 'sdmc_directory', (session/'sd').as_posix()+'/'),
        ('Data%20Storage', 'nand_directory', (session/'nand').as_posix()+'/'),
        ('Debugging', 'use_gdbstub', 'true'),
        ('Debugging', 'gdbstub_port', str(port)),
        ('Miscellaneous', 'log_filter', '*:Info Service.LDR:Debug'),
    ):
        qt = ini_set(qt, section, key+'\\default', 'false')
        qt = ini_set(qt, section, key, value)
    target = runtime/'user/config/qt-config.ini'
    target.parent.mkdir(parents=True)
    target.write_text(qt, encoding='utf-8')
    require(all(file_sha(Path(path)) == expected for path, expected in original.items()), 'A source changed during preparation')
    manifest = dict(purpose='xy-persistent-items-original-trace', installable=False, title_id=title,
                    port=port, stock_sha256=report['sha256'], emulator_sha256=original[str(executable)],
                    source_sha256=original, copied_rom_sha256=original[str(rom)],
                    limitation='No battle-mode or consumable-restoration validation; user plays all emulator cases manually.')
    (session/'session.json').write_text(json.dumps(manifest, indent=2)+'\n', encoding='utf-8')
    print(f'Prepared original-game session: {session}\nNo emulator was launched; no patch was installed.')
    return manifest


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    for option in ('project', 'rom', 'emulator', 'save', 'config', 'session'):
        parser.add_argument('--'+option, type=Path, required=True)
    parser.add_argument('--port', type=int, default=24691)
    args = parser.parse_args()
    prepare(args.project, args.rom, args.emulator, args.save, args.config, args.session, args.port)
