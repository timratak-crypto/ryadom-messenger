#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p build/polling-tests
# Compile the real model declarations on macOS; only the iPhone views/import are excluded.
python3 - <<'PY'
from pathlib import Path
source = Path("ios/Ryadom/RyadomApp.swift").read_text()
assert ".task(id: polling)" in source
assert "await model.poll(active: polling.active)" in source
model = source.split("import PhotosUI", 1)[0].replace("import UIKit\n", "")
Path("build/polling-tests/ChatModel.swift").write_text(model)
PY
xcrun swiftc -parse-as-library -swift-version 5 \
  build/polling-tests/ChatModel.swift ios/tests/PollingRegression.swift \
  -o build/polling-tests/run
build/polling-tests/run
