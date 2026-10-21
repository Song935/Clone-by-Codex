"""Basic pre-publication scan of staged/tracked text. Does not print matched values."""
import re
import subprocess
from pathlib import Path

patterns = {
    "provider-token": re.compile(r"(?:sk-ant-api\w*-)[A-Za-z0-9_-]{20,}"),
    "github-token": re.compile(r"gh[pousr]_[A-Za-z0-9]{20,}"),
    "aws-key": re.compile(r"AKIA[A-Z0-9]{16}"),
    "private-key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
}
paths = subprocess.check_output(["git", "ls-files", "-z"]).decode("utf-8").split("\0")
failures = []
for name in filter(None, paths):
    data = Path(name).read_bytes()
    if b"\0" in data:
        continue
    text = data.decode("utf-8", errors="replace")
    for label, pattern in patterns.items():
        if pattern.search(text):
            failures.append((name, label))
for name, label in failures:
    print(f"Potential secret: {name} ({label}); value suppressed")
if failures:
    raise SystemExit(1)
print(f"Basic secret scan passed: {len(list(filter(None, paths)))} tracked files")
