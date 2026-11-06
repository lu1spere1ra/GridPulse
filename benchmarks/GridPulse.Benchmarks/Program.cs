using BenchmarkDotNet.Running;

// Run all:      dotnet run -c Release -- --filter '*'
// Quick check:  dotnet run -c Release -- --filter '*' --job short
BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
