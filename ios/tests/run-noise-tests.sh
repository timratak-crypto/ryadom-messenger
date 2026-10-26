#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/../.."
mkdir -p build/noise-tests
xcrun swiftc -O -parse-as-library -swift-version 5 ios/Ryadom/NoiseFilter.swift ios/tests/NoiseTests.swift -o build/noise-tests/run
build/noise-tests/run
