#!/bin/bash

# can add options here for filtering contract tests
TEST_HARNESS_EXTRA_PARAMS=

curl -s https://raw.githubusercontent.com/launchdarkly/sdk-test-harness/main/downloader/run.sh \
  | VERSION=v2 PARAMS="-url http://localhost:8000 -debug -stop-service-at-end \
    ${TEST_HARNESS_PARAMS:-} ${TEST_HARNESS_EXTRA_PARAMS:-}" sh
