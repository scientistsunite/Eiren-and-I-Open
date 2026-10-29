"""Source guard, not a substitute for observed runtime Windows evidence."""
from pathlib import Path
import re
root=Path(__file__).resolve().parents[1]
for file in (root/'src').rglob('*.cs'):
    text=file.read_text(encoding="utf-8-sig")
    for token in ('HttpClient','WebClient','TcpClient','TcpListener','UdpClient','System.Net.Sockets','Dns.','WebRequest','Socket(','EnableTelemetry','Process.Start("http'):
        assert token not in text, (file,token)
    for dll in re.findall(r'(?:DllImport|LibraryImport)\s*\(\s*"([^"]+)"',text):
        assert dll.lower().removesuffix('.dll') not in ('ws2_32','winhttp','wininet','dnsapi'), (file,dll)
pipe=(root/'src/Raider.App/LocalPipe.cs').read_text(encoding="utf-8-sig")
assert '0x8' in pipe and 'D:P(A;;GA;;;' in pipe and 'CreateNamedPipe' in pipe
collector=(root/'src/Raider.Engine/EtwCollector.cs').read_text(encoding="utf-8-sig")
assert 'Marshal.ReadInt32(e.DataStart)' in collector
assert not re.search(r'Accept\(e,e.size.*e.ProcessID',collector)
assert 'TcpIpRetransmit+=' not in collector and 'TcpIpTCPCopy+=' not in collector
print('PASS runtime source network API guard, payload PID, restricted local pipe, no retransmit subscription')
