#!/usr/bin/env python3
"""Build pinned, statically linked WebRTC dependencies into one redistributable library."""
import argparse
import hashlib
import json
import platform
import shutil
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent
sys.path.insert(0, str(ROOT.parents[1] / 'build'))
from verify_native_architecture import identify


def run(*args):
    subprocess.run([str(x) for x in args], check=True)


def source(name, cache, override):
    spec = json.loads((ROOT / 'source.json').read_text())[name]
    path = Path(override).resolve() if override else cache / name
    if not path.exists():
        run('git', 'init', path)
        run('git', '-C', path, 'fetch', '--depth=1', spec['repository'], spec['commit'])
        run('git', '-C', path, 'checkout', '--detach', 'FETCH_HEAD')
        run('git', '-C', path, 'submodule', 'update', '--init', '--recursive', '--depth=1')
    actual = subprocess.check_output(['git', '-C', str(path), 'rev-parse', 'HEAD'], text=True).strip()
    if actual != spec['commit']:
        raise RuntimeError(f'{name} must be at {spec["commit"]}, found {actual}')
    return path


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--rid', choices=['linux-x64', 'linux-arm64', 'win-x64', 'win-arm64', 'osx'], required=True)
    parser.add_argument('--cache', type=Path, default=ROOT / '.build')
    parser.add_argument('--output', type=Path, default=ROOT / 'runtimes')
    parser.add_argument('--datachannel-source')
    parser.add_argument('--mbedtls-source')
    args = parser.parse_args()
    cache = args.cache.resolve()
    dc = source('libdatachannel', cache, args.datachannel_source)
    tls = source('mbedtls', cache, args.mbedtls_source)
    # v0.24.6 omitted colorSpaceId in its two-byte extension-header decision.
    patch = ROOT / 'color-space-id.patch'
    check = subprocess.run(['git', '-C', str(dc), 'apply', '--reverse', '--check', str(patch)], capture_output=True)
    if check.returncode != 0:
        run('git', '-C', dc, 'apply', patch)
    run(sys.executable, tls / 'scripts/config.py', '-f', tls / 'include/mbedtls/mbedtls_config.h', 'set', 'MBEDTLS_SSL_DTLS_SRTP')
    build = cache / args.rid
    prefix = build / 'tls-install'
    common = ['-G', 'Ninja', '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_POSITION_INDEPENDENT_CODE=ON',
              '-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded', '-DCMAKE_POLICY_VERSION_MINIMUM=3.5']
    if args.rid == 'osx':
        common += ['-DCMAKE_OSX_ARCHITECTURES=arm64;x86_64', '-DCMAKE_OSX_DEPLOYMENT_TARGET=11.0']
    run('cmake', '-S', tls, '-B', build / 'tls', *common, '-DENABLE_PROGRAMS=OFF', '-DENABLE_TESTING=OFF',
        '-DUSE_SHARED_MBEDTLS_LIBRARY=OFF', '-DUSE_STATIC_MBEDTLS_LIBRARY=ON', f'-DCMAKE_INSTALL_PREFIX={prefix}')
    run('cmake', '--build', build / 'tls', '--parallel', '4')
    run('cmake', '--install', build / 'tls')
    run('cmake', '-S', ROOT, '-B', build / 'preview', *common, f'-DDATACHANNEL_SOURCE={dc}', f'-DCMAKE_PREFIX_PATH={prefix}')
    run('cmake', '--build', build / 'preview', '--parallel', '4')
    if args.rid != 'win-arm64' or platform.machine().lower() in ('arm64', 'aarch64'):
        run('ctest', '--test-dir', build / 'preview', '--output-on-failure')
    name = 'BeutlDataChannel.dll' if args.rid.startswith('win') else 'libBeutlDataChannel.' + ('dylib' if args.rid == 'osx' else 'so')
    binary = build / 'preview' / name
    expected = {'arm64', 'x64'} if args.rid == 'osx' else {args.rid.split('-')[1]}
    kind, arches = identify(binary.read_bytes())
    if arches != expected:
        raise RuntimeError(f'Wrong native architectures: {arches}, expected {expected}')
    output = args.output.resolve() / args.rid / 'native'
    output.mkdir(parents=True, exist_ok=True)
    shutil.copy2(binary, output / name)
    manifest = {'sha256': hashlib.sha256(binary.read_bytes()).hexdigest(), 'architectures': sorted(arches),
                'sources': json.loads((ROOT / 'source.json').read_text())}
    (output / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
    licenses = [('libdatachannel (MPL-2.0)', dc / 'LICENSE'), ('Mbed TLS (Apache-2.0)', tls / 'LICENSE'),
                ('libjuice (MPL-2.0)', dc / 'deps/libjuice/LICENSE'), ('libsrtp (BSD-3-Clause)', dc / 'deps/libsrtp/LICENSE'),
                ('usrsctp (BSD-3-Clause)', dc / 'deps/usrsctp/LICENSE.md'), ('plog (MIT)', dc / 'deps/plog/LICENSE')]
    (args.output.resolve().parent / 'THIRD-PARTY-NOTICES.txt').write_text('\n\n'.join(f'{title}\n\n{path.read_text()}' for title, path in licenses))


if __name__ == '__main__':
    main()
