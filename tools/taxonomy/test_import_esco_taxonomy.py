from __future__ import annotations

import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


HERE = Path(__file__).resolve().parent
IMPORTER = HERE / "import_esco_taxonomy.py"
FIXTURE = HERE / "fixtures" / "esco-skills.csv"
EXPECTED = HERE / "fixtures" / "expected-taxonomy.json"


def command(output: Path, *extra: str) -> list[str]:
    return [
        sys.executable,
        str(IMPORTER),
        str(FIXTURE),
        str(output),
        "--taxonomy-version",
        "demo-1",
        "--source-name",
        "ESCO-style fixture",
        "--source-version",
        "demo-1",
        "--source-license",
        "CC0-1.0",
        "--source-url",
        "https://example.com/esco-style-fixture",
        *extra,
    ]


class ImportEscoTaxonomyTests(unittest.TestCase):
    def test_fixture_is_deterministic_and_current(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            first = Path(directory) / "first.json"
            second = Path(directory) / "second.json"

            subprocess.run(command(first), check=True, capture_output=True, text=True)
            subprocess.run(command(second), check=True, capture_output=True, text=True)

            generated = first.read_text(encoding="utf-8")
            self.assertEqual(generated, second.read_text(encoding="utf-8"))
            self.assertEqual(generated, EXPECTED.read_text(encoding="utf-8"))

            subprocess.run(
                command(EXPECTED, "--check"), check=True, capture_output=True, text=True
            )


if __name__ == "__main__":
    unittest.main()
