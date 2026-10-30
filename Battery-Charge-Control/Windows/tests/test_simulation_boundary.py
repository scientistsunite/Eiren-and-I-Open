"""Exercise the safety check against isolated copies, never mutate repository sources."""
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class BoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        repo = Path(self.temp.name) / 'repo'
        self.root = repo / 'Battery-Charge-Control/Windows'
        shutil.copytree(ROOT, self.root, ignore=shutil.ignore_patterns('bin', 'obj', 'artifacts', '__pycache__'))
        workflow = repo / '.github/workflows'
        workflow.mkdir(parents=True)
        shutil.copyfile(ROOT.parents[1] / '.github/workflows/windows-build.yml', workflow / 'windows-build.yml')

    def run_check(self):
        return subprocess.run([sys.executable, '-B', str(self.root / 'scripts/check-simulation.py')],
                              capture_output=True, text=True, timeout=15)

    def test_reviewed_sources_pass(self):
        result = self.run_check()
        self.assertEqual(0, result.returncode, result.stderr)

    def test_accidental_production_adapter_inclusion_fails(self):
        project = self.root / 'tests/BCC.Simulation/BCC.Simulation.csproj'
        project.write_text(project.read_text().replace('<Compile Include="SimulationEnvironment.cs" />',
            '<Compile Include="SimulationEnvironment.cs" /><Compile Include="../../src/BCC.Core/DeviceAndBattery.cs" />'))
        self.assertNotEqual(0, self.run_check().returncode)

    def test_process_operation_in_allowlisted_source_fails(self):
        source = self.root / 'tests/BCC.Simulation/SimulationEnvironment.cs'
        source.write_text(source.read_text() + '\n// Accidental addition: Process.Start("sc.exe");\n')
        self.assertNotEqual(0, self.run_check().returncode)

    def test_production_assembly_reference_fails(self):
        project = self.root / 'tests/BCC.Simulation/BCC.Simulation.csproj'
        project.write_text(project.read_text().replace('</Project>',
            '<ItemGroup><ProjectReference Include="../../src/BCC.Core/BCC.Core.csproj" /></ItemGroup></Project>'))
        self.assertNotEqual(0, self.run_check().returncode)

    def test_installer_execution_hook_fails(self):
        source = self.root / 'installer/MonitoringOnly.iss'
        source.write_text(source.read_text() + '\n[Run]\nFilename: "{app}\\BCC.NoFirmwarePreview.exe"\n')
        self.assertNotEqual(0, self.run_check().returncode)


if __name__ == '__main__':
    unittest.main()
