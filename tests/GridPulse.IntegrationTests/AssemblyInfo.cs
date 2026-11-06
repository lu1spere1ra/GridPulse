using GridPulse.IntegrationTests.Database;
using GridPulse.IntegrationTests.Messaging;

[assembly: AssemblyFixture(typeof(CommunityDatabase))]
[assembly: AssemblyFixture(typeof(ApacheDatabase))]
[assembly: AssemblyFixture(typeof(MosquittoBroker))]
