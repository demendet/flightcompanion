using Xunit;

// A few tests switch the game's language and cast settings, which are process wide.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
