using Xunit;

// Some tests change the local time zone of the process, which would otherwise affect
// tests running in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
