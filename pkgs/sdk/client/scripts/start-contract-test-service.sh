#!/bin/bash
cd contract-tests && dotnet bin/Debug/${TESTFRAMEWORK:-net7.0}/ContractTestService.dll
