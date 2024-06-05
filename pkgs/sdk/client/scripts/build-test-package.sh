#!/bin/bash

set -e

# build-test-package.sh
# Temporarily changes the project version to a unique prerelease version string based on the current
# date and time, builds a .nupkg package, and places the package in ./test-packages. You can then use
# the test-packages directory as a package source to use this package in our testing tools.

TEST_VERSION="0.0.1-$(date +%Y%m%d.%H%M%S)"
PROJECT_FILE=src/LaunchDarkly.ClientSdk/LaunchDarkly.ClientSdk.csproj
SAVE_PROJECT_FILE="${PROJECT_FILE}.orig"
TEST_PACKAGE_DIR=./test-packages

mkdir -p "${TEST_PACKAGE_DIR}"

cp "${PROJECT_FILE}" "${SAVE_PROJECT_FILE}"

trap 'mv "${SAVE_PROJECT_FILE}" "${PROJECT_FILE}"' EXIT

temp_file="${PROJECT_FILE}.tmp"
sed "s#^\( *\)<Version>[^<]*</Version>#\1<Version>${TEST_VERSION}</Version>#g" "${PROJECT_FILE}" > "${temp_file}"
mv "${temp_file}" "${PROJECT_FILE}"

msbuild /restore -t:pack "${PROJECT_FILE}"

NUPKG_FILE="src/LaunchDarkly.ClientSdk/bin/Debug/LaunchDarkly.ClientSdk.${TEST_VERSION}.nupkg"
if [ -f "${NUPKG_FILE}" ]; then
	mv "${NUPKG_FILE}" "${TEST_PACKAGE_DIR}"
	echo; echo; echo "Success! Created test package version ${TEST_VERSION} in ${TEST_PACKAGE_DIR}"
else
	echo; echo "Unknown problem - did not build the expected package file"
	exit 1
fi
