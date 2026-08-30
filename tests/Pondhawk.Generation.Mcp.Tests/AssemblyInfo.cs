// ServerContextTests redirects Console.Out/Console.Error to prove the server never
// writes diagnostics to the stdout it speaks MCP over. That redirection is
// process-global, so it can only be asserted safely with test classes run in
// sequence. The suite runs in well under a second, so the cost is negligible.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
