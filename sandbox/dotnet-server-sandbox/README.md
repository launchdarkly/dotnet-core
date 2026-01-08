# LaunchDarkly .NET Server SDK Sandbox - Environment Variable Configuration

This sandbox application supports configuration through environment variables, allowing you to test different SDK configurations.

## Quick Start

The minimal configuration requires only the SDK key:

```bash
export LAUNCHDARKLY_SDK_KEY="your-sdk-key-here"
dotnet run
```

## Build Configuration Requirements

### Persistent Store Support (Redis and DynamoDB)

**Important:** Redis and DynamoDB persistent store integrations are only available when building with the `DebugLocalReferences` configuration.

```bash
# Run with persistent store support
export LAUNCHDARKLY_SDK_KEY="your-sdk-key"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="persistent-store"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="redis"
dotnet run --configuration DebugLocalReferences
```

**Why DebugLocalReferences?**

This is required to be able to use local dependencies without building them with the strong-naming key. This project uses a project reference for the Server SDK and the persistent stores also need to be using a project reference. If they use the nuget references, then the strong naming requirement will prevent the project from building/running.

**What works in other configurations (Debug/Release):**
- Default mode
- Streaming-only mode
- Polling-only mode
- Offline mode

**What requires DebugLocalReferences:**
- Daemon mode (read-only from persistent store)
- Persistent-store mode (default + persistent store backup)

If you attempt to use persistent store features without building with DebugLocalReferences, you'll see a clear warning message and the application will either fall back to in-memory storage or fail with an informative error.

## Environment Variables Reference

### Required Variables

| Variable | Description | Example |
|----------|-------------|---------|
| `LAUNCHDARKLY_SDK_KEY` | Your LaunchDarkly SDK key (required) | `sdk-abc-123-xyz` |

### Optional - Core Configuration

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `LAUNCHDARKLY_OFFLINE` | Boolean | `false` | Run SDK in offline mode (no connections to LaunchDarkly) |
| `LAUNCHDARKLY_DATA_SYSTEM_MODE` | Enum | `default` | Data acquisition strategy (see modes below) |
| `LAUNCHDARKLY_START_WAIT_TIME_MS` | Integer | `10000` | Maximum time (ms) to wait for SDK initialization |

#### Data System Modes

| Mode | Description                                        |
|------|----------------------------------------------------|
| `default` | poll + streaming with polling fallback |
| `streaming` | Real-time streaming updates only |
| `polling` | Periodic polling only, no streaming |
| `daemon` | Read-only from persistent store |
| `persistent-store` | Default mode + persistent store backup |

### Optional - Persistent Store Configuration

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `LAUNCHDARKLY_PERSISTENT_STORE_TYPE` | Enum | (none) | Type of persistent store: `redis` or `dynamodb` |

**Note:** Required when using `daemon` or `persistent-store` data system modes.

### Optional - Redis Configuration

These variables apply when `LAUNCHDARKLY_PERSISTENT_STORE_TYPE=redis`:

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `LAUNCHDARKLY_REDIS_HOST` | String | `localhost` | Redis server hostname |
| `LAUNCHDARKLY_REDIS_PORT` | Integer | `6379` | Redis server port |
| `LAUNCHDARKLY_REDIS_PREFIX` | String | `launchdarkly` | Key prefix for all Redis keys |
| `LAUNCHDARKLY_REDIS_CONNECT_TIMEOUT_MS` | Integer | `5000` | Connection timeout in milliseconds |
| `LAUNCHDARKLY_REDIS_OPERATION_TIMEOUT_MS` | Integer | `3000` | Operation timeout in milliseconds |

### Optional - DynamoDB Configuration

These variables apply when `LAUNCHDARKLY_PERSISTENT_STORE_TYPE=dynamodb`:

| Variable | Type | Default | Description |
|----------|------|---------|-------------|
| `LAUNCHDARKLY_DYNAMODB_TABLE_NAME` | String | (required) | DynamoDB table name (must exist) |
| `LAUNCHDARKLY_DYNAMODB_PREFIX` | String | `""` | Key prefix for DynamoDB items |

**DynamoDB Table Requirements:**
- Partition key: `namespace` (String)
- Sort key: `key` (String)

## Configuration Examples

### 1. Default Mode (Recommended)

Minimal configuration with just SDK key. Uses CDN + streaming:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
dotnet run
```

### 2. Offline Mode

For testing without connecting to LaunchDarkly:

```bash
export LAUNCHDARKLY_SDK_KEY="any-value"
export LAUNCHDARKLY_OFFLINE="true"
dotnet run
```

### 3. Redis Persistent Store

Default mode backed by Redis persistent store:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="persistent-store"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="redis"
export LAUNCHDARKLY_REDIS_HOST="redis.example.com"
export LAUNCHDARKLY_REDIS_PORT="6379"
export LAUNCHDARKLY_REDIS_PREFIX="myapp:prod"
dotnet run
```

### 4. Daemon Mode with Redis

Read-only mode for SDKs behind Relay Proxy:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="daemon"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="redis"
export LAUNCHDARKLY_REDIS_HOST="localhost"
export LAUNCHDARKLY_REDIS_PREFIX="ld:relay:prod"
dotnet run
```

### 5. DynamoDB Persistent Store

Default mode backed by DynamoDB:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="persistent-store"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="dynamodb"
export LAUNCHDARKLY_DYNAMODB_TABLE_NAME="launchdarkly-features"
export LAUNCHDARKLY_DYNAMODB_PREFIX="myapp"
dotnet run
```

### 6. Streaming-Only Mode

Real-time updates without CDN initialization:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="streaming"
dotnet run
```

### 7. Polling-Only Mode

For restricted network environments:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_DATA_SYSTEM_MODE="polling"
dotnet run
```

### 8. Custom Timeouts

Adjust SDK behavior for slow networks:

```bash
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_START_WAIT_TIME_MS="30000"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="redis"
export LAUNCHDARKLY_REDIS_CONNECT_TIMEOUT_MS="10000"
export LAUNCHDARKLY_REDIS_OPERATION_TIMEOUT_MS="5000"
dotnet run
```

## Data System Modes Explained

### Default Mode
**Use when:** You want LaunchDarkly's recommended configuration (best for most production scenarios)

**How it works:**
1. Fetches initial data from global CDN (fast)
2. Establishes streaming connection for real-time updates
3. Falls back to polling if streaming is interrupted

### Streaming Mode
**Use when:** You need real-time updates without CDN initialization delay

**How it works:**
- Establishes streaming connection immediately
- Receives real-time updates as flags change
- Can fall back to polling if instructed by LaunchDarkly

### Polling Mode
**Use when:** Your network doesn't support streaming (firewall restrictions, etc.)

**How it works:**
- Periodically polls LaunchDarkly CDN for updates
- No real-time updates
- Predictable network traffic pattern

### Daemon Mode
**Use when:** Multiple SDK instances need to share flag data via Relay Proxy

**How it works:**
- SDK never connects to LaunchDarkly
- Reads flag data from persistent store (populated by Relay Proxy or another SDK)
- SDK never writes to the store (read-only)

**Requirements:** Must configure `LAUNCHDARKLY_PERSISTENT_STORE_TYPE`

### Persistent Store Mode
**Use when:** You want fast startup times and resilience to temporary network issues

**How it works:**
- Uses default mode (CDN + streaming)
- Maintains a persistent store with latest flag data
- On startup, can serve flags from store while waiting for fresh data
- Keeps store up-to-date as new data arrives

**Requirements:** Must configure `LAUNCHDARKLY_PERSISTENT_STORE_TYPE`

## Troubleshooting

### Error: "LAUNCHDARKLY_SDK_KEY environment variable is required"
**Solution:** Set the `LAUNCHDARKLY_SDK_KEY` environment variable before running the application.

### Error: "Invalid data system mode"
**Solution:** Check that `LAUNCHDARKLY_DATA_SYSTEM_MODE` is set to one of: `default`, `streaming`, `polling`, `daemon`, or `persistent-store`.

### Error: "daemon mode requires a persistent store"
**Solution:** When using daemon or persistent-store modes, you must set `LAUNCHDARKLY_PERSISTENT_STORE_TYPE` to either `redis` or `dynamodb`.

### Error: "LAUNCHDARKLY_DYNAMODB_TABLE_NAME is required"
**Solution:** When using DynamoDB as the persistent store, you must specify the table name via `LAUNCHDARKLY_DYNAMODB_TABLE_NAME`.

### Error: "Invalid integer value for..."
**Solution:** Ensure numeric environment variables (ports, timeouts) contain valid integer values.

### Error: "Invalid boolean value for..."
**Solution:** Boolean environment variables must be set to either `true` or `false` (case-insensitive).

## AWS Credentials for DynamoDB

When using DynamoDB, the SDK uses the standard AWS credential chain:
1. Environment variables (`AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`)
2. Shared credentials file (`~/.aws/credentials`)
3. IAM role (when running on AWS infrastructure)

Example with explicit credentials:
```bash
export AWS_ACCESS_KEY_ID="your-access-key"
export AWS_SECRET_ACCESS_KEY="your-secret-key"
export AWS_REGION="us-east-1"
export LAUNCHDARKLY_SDK_KEY="sdk-abc-123"
export LAUNCHDARKLY_PERSISTENT_STORE_TYPE="dynamodb"
export LAUNCHDARKLY_DYNAMODB_TABLE_NAME="launchdarkly-flags"
dotnet run
```

## Boolean Values

Boolean environment variables accept the following values (case-insensitive):
- True: `true`, `True`, `TRUE`
- False: `false`, `False`, `FALSE`

Any other value will result in a format error.
