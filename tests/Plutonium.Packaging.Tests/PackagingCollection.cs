using Xunit;

namespace Plutonium.Packaging.Tests;

/// <summary>Defines the shared packaging fixture collection.</summary>
[CollectionDefinition("packaging")]
public sealed class PackagingCollection : ICollectionFixture<PackagingFixture>
{
}
