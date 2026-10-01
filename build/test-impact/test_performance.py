import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import impact
import measure


class PerformanceTests(unittest.TestCase):
    def test_syntax_cache_requires_exact_sources_sha_and_environment(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            source = root / 'A.cs'
            source.write_text('class A {}')
            cache, timings = {}, {}
            head = ['first']
            calls = []

            def git(_, *args):
                return head[0] if args[0] == 'rev-parse' else ('A.cs' if '--others' not in args else '')

            def run(args, *unused, **kwargs):
                calls.append(args)
                if str(args[1]).endswith('Indexer.dll'):
                    impact.write_json(Path(args[-1]), {'A.cs': {'hash': source.read_text()}})

            with patch.object(impact, 'git', side_effect=git), patch.object(impact, 'run', side_effect=run):
                def index(env=None, scope=None):
                    return impact.source_index(root, root, {'A.cs'} if scope is None else scope,
                                               timings, cache, env or {'sdk': '10', 'tfm': 'net10.0'})
                original = index()
                self.assertEqual(2, len(calls))
                original['A.cs']['hash'] = 'mutated caller'
                self.assertNotEqual(original, index())
                self.assertTrue(timings['sourceIndexCacheHit'])
                self.assertEqual(2, len(calls))
                source.write_text('global using Alias = Other; class A {}')
                index()
                self.assertFalse(timings['sourceIndexCacheHit'])
                for key in ('sdk', 'tfm', 'configuration', 'os', 'toolHash'):
                    index({key: 'changed'})
                    self.assertFalse(timings['sourceIndexCacheHit'])
                head[0] = 'second'
                index()
                self.assertFalse(timings['sourceIndexCacheHit'])
                index(scope=set())
                self.assertFalse(timings['sourceIndexCacheHit'])
                self.assertLessEqual(len(cache), 2)

    def test_duration_ranking_preserves_inventory_and_ignores_unknown_cases(self):
        candidates = [dict(key=name, className='Tests', name=name) for name in ('Fast', 'Slow', 'Unknown')]
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            (folder / 'result.xml').write_text('<test-run>'
                '<test-case classname="Tests" methodname="Slow" duration="2" />'
                '<test-case classname="Tests" methodname="Fast" duration="0.1" />'
                '<test-case classname="Tests" methodname="Slow" duration="3" />'
                '<test-case classname="Tests" methodname="Missing" duration="999" />'
                '</test-run>')
            ranked = impact.duration_priority(candidates, folder)
            self.assertEqual(['Slow', 'Fast', 'Unknown'], [t['key'] for t in ranked])
            self.assertEqual(set(t['key'] for t in candidates), set(t['key'] for t in ranked))
            (folder / 'broken.xml').write_text('broken')
            self.assertEqual(ranked, impact.duration_priority(candidates, folder))

    def test_compile_scope_diamond_evaluates_each_project_once_and_rejects_escape(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder).resolve()
            graph = {'Test': ['A', 'B'], 'A': ['Shared'], 'B': ['Shared'], 'Shared': []}
            seen = []

            def evaluate(args, *unused):
                name = Path(args[2]).stem
                seen.append(name)
                return json.dumps({'Items': {'Compile': [{'FullPath': str(root / (name + '.cs'))}],
                    'ProjectReference': [{'FullPath': str(root / (p + '.csproj'))} for p in graph[name]]}})

            with patch.object(impact, 'run', side_effect=evaluate):
                self.assertEqual({n + '.cs' for n in graph}, impact.compilation_scope(root, ['Test.csproj'], 'net10.0', 'Debug'))
                self.assertCountEqual(graph, seen)
                with self.assertRaises(ValueError):
                    impact.compilation_scope(root, ['../External.csproj'], 'net10.0', 'Debug')

    def test_paired_coverage_modes_use_identical_command_except_collector(self):
        test = {'t': {'className': 'Tests', 'name': 'M'}}
        with tempfile.TemporaryDirectory() as folder, patch.object(impact, 'run') as run, \
                patch.object(measure, 'inspect_execution', return_value={'engineSeconds': 0}):
            root = Path(folder)
            for coverage in (True, False):
                out = root / str(coverage)
                measure.execute(root, out, test, False, coverage)
                self.assertEqual(coverage, '--collect:XPlat Code Coverage' in run.call_args.args[0])
                self.assertEqual(coverage, 'DataCollector' in (out / 'test.runsettings').read_text())
