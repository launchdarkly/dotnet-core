
build:
	dotnet build

test:
	dotnet test

clean:
	dotnet clean

TEMP_TEST_OUTPUT=/tmp/sdk-contract-test-service.log

build-contract-tests:
	@./scripts/build-contract-tests.sh

start-contract-test-service:
	@./scripts/start-contract-test-service.sh

start-contract-test-service-bg:
	@echo "Test service output will be captured in $(TEMP_TEST_OUTPUT)"
	@./scripts/start-contract-test-service.sh >$(TEMP_TEST_OUTPUT) 2>&1 &

run-contract-tests:
	@./scripts/run-contract-tests.sh

contract-tests: build-contract-tests start-contract-test-service-bg run-contract-tests

.PHONY: build test clean build-contract-tests start-contract-test-service run-contract-tests contract-tests