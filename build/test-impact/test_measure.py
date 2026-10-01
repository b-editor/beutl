import tempfile
from pathlib import Path
import unittest

import impact
import measure


class MeasurementTests(unittest.TestCase):
    def test_reports_execution_saving_and_analysis_cost_separately(self):
        runs = [{"kind": "full", "wallSeconds": 10}, {"kind": "selected", "wallSeconds": 6},
                {"kind": "selected", "wallSeconds": 8}, {"kind": "full", "wallSeconds": 12}]
        result = measure.summarize_runs(runs, 5)
        self.assertEqual(4, result["executionSavingSeconds"])
        self.assertEqual(-1, result["savingAfterAnalysisSeconds"])

    def test_measured_xml_must_contain_every_parameter_case_exactly_once(self):
        key = impact.test_key(measure.PROJECT, "Tests", "M")
        expected = {key: {"cases": [{"fullname": "M(1)"}, {"fullname": "M(2)"}]}}
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            xml = '<test-run><test-suite type="Assembly" duration="1.5">{}</test-suite></test-run>'
            case = '<test-case classname="Tests" methodname="M" fullname="M({})" result="Passed" />'
            (folder / "results.xml").write_text(xml.format(case.format(1) + case.format(2)))
            result = measure.inspect_execution(folder, expected)
            self.assertEqual(2, result["cases"])
            self.assertEqual(1.5, result["engineSeconds"])
            for body in (case.format(1), case.format(1) * 2 + case.format(2)):
                (folder / "results.xml").write_text(xml.format(body))
                with self.assertRaises(ValueError):
                    measure.inspect_execution(folder, expected)

    def test_failed_measurement_is_not_reported_as_a_speedup(self):
        with tempfile.TemporaryDirectory() as folder:
            folder = Path(folder)
            (folder / "results.xml").write_text('<test-run><test-suite type="Assembly" duration="1">'
                '<test-case classname="Tests" methodname="M" fullname="M" result="Failed" />'
                '</test-suite></test-run>')
            expected = {impact.test_key(measure.PROJECT, "Tests", "M"): {"cases": [{"fullname": "M"}]}}
            with self.assertRaises(ValueError):
                measure.inspect_execution(folder, expected)

    def test_experiments_are_specific_nonzero_method_edits(self):
        root = impact.HERE.parents[1]
        for _, path, before, after in measure.SCENARIOS:
            self.assertEqual(1, (root / path).read_bytes().count(before))
            self.assertNotEqual(before, after)
