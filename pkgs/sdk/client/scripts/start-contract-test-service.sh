#!/bin/bash
cd contract-tests && dotnet bin/Debug/${TESTFRAMEWORK:-net8.0}/ContractTestService.dll
