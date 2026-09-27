using System.Runtime.CompilerServices;

// Matches the accepted convention in EngiSentry.AutoCAD2021: the offline test harness composes real
// standards-authority contexts (sources/clauses/policies/gate) through the public factories, and needs the
// context constructor to build a TEST_ONLY deterministic authority chain. No production behavior changes.
[assembly: InternalsVisibleTo("EngiSentry.Tests")]
