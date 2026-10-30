"""Detector runtime source guard; not runtime privacy or sensor-coverage proof.

Reads source only. It does not start Detector, access devices, read registry
activity, change services, or make network requests.
"""
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / "src"

# Network counters and local pipes need no client connection or DNS API.
NETWORK = (
    r"\bHttpClient\b", r"\bWebClient\b", r"\bWebRequest\b",
    r"\bTcpClient\b", r"\bTcpListener\b", r"\bUdpClient\b",
    r"\bSystem\.Net\.Sockets\b", r"\bDns\s*\.",
    r"\bSocket\s*\(", r"\bProcess\.Start\s*\(\s*\"https?://",
)
# Observing capability-use metadata must not open an audio/video/screen source.
ACQUISITION = (
    r"\bWasapiCapture\b", r"\bWaveInEvent\b", r"\bwaveInOpen\b",
    r"\bwaveInStart\b", r"\bMediaCapture\b", r"\bAudioRecord\b",
    r"\bMFCreateDeviceSource\b", r"\bVideoCapture\b",
    r"\bgetUserMedia\b", r"\bCopyFromScreen\b", r"\bBitBlt\b",
    r"\bPrintWindow\b", r"\bcapCreateCaptureWindow\w*\b",
)
FOREIGN_IDENTITIES = (
    "CylonRaider", "EirenCylonRaider",
    "C6235B22-2E4E-4C4D-BA36-1B0F34E26420",
    "0x43595250", "1129927248",  # Foreign Raider report-index identity.
    "../Raider.", "..\\Raider.",
)
FORBIDDEN_NATIVE_MODULES = {"ws2_32", "winhttp", "wininet", "dnsapi", "avicap32"}


def audit() -> int:
    failures = []
    files = sorted(path for path in SOURCE.rglob("*")
                   if path.suffix in {".cs", ".csproj", ".manifest"}
                   and not {"obj", "bin"}.intersection(path.parts))
    if not files:
        raise AssertionError("Detector runtime source was not found")
    for path in files:
        text = path.read_text(encoding="utf-8-sig")
        relative = path.relative_to(ROOT)
        for pattern in NETWORK + ACQUISITION:
            if re.search(pattern, text, flags=re.IGNORECASE):
                failures.append(f"{relative}: forbidden acquisition/network API {pattern}")
        for identity in FOREIGN_IDENTITIES:
            if identity.casefold() in text.casefold():
                failures.append(f"{relative}: foreign application identity {identity}")
        for module in re.findall(r'(?:DllImport|LibraryImport)\s*\(\s*"([^"]+)"', text):
            if module.casefold().removesuffix(".dll") in FORBIDDEN_NATIVE_MODULES:
                failures.append(f"{relative}: forbidden native network/capture module {module}")
    # Compare independent installer/runtime declarations rather than relying on
    # a copied fixture's expected GUID. An uninstall mismatch can affect another app.
    platform = (SOURCE / "Detector.App" / "PlatformPaths.cs").read_text(encoding="utf-8-sig")
    uninstall = re.search(r'Uninstall\\\{([0-9A-Fa-f-]{36})\}_is1', platform)
    for recipe in sorted((ROOT / "packaging").rglob("installer.iss")):
        text = recipe.read_text(encoding="utf-8-sig")
        installer = re.search(r'^AppId=\{\{([0-9A-Fa-f-]{36})\}', text, flags=re.MULTILINE)
        if not uninstall or not installer or uninstall[1].casefold() != installer[1].casefold():
            failures.append(f"{recipe.relative_to(ROOT)}: installer AppId and runtime uninstaller lookup differ")
    if failures:
        raise AssertionError("\n".join(failures))
    print(f"PASS Detector source guard: {len(files)} runtime source/project/manifest files; "
          "no listed network-client or media/screen-acquisition API; independent application identities")
    print("LIMIT: static API checks do not establish runtime absence of communication, "
          "device coverage, retention correctness, or installer/service isolation")
    return 0


if __name__ == "__main__":
    raise SystemExit(audit())
