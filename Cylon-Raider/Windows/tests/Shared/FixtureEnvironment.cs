// Test hosts only. This is never linked into a shipped application.
internal static class FixtureEnvironment
{
    internal static void Require()
    {
        if(!Allowed(Environment.GetEnvironmentVariable))throw new InvalidOperationException("Fixture requires disposable hosted Windows or the owner-authorized dedicated EIREN-WIN-CI VM");
    }
    internal static bool Allowed(Func<string,string?> Read)
    {
        bool actions=Read("GITHUB_ACTIONS")=="true";
        bool hosted=Read("RUNNER_ENVIRONMENT")=="github-hosted";
        bool dedicated=Read("RUNNER_ENVIRONMENT")=="self-hosted" && Read("RUNNER_NAME")=="EIREN-WIN-CI"
            && Read("RAIDER_CI_FIXTURE")=="EIREN-WIN-CI-v04"
            && Read("GITHUB_REPOSITORY")=="scientistsunite/Eiren-and-I-Open" && Read("GITHUB_ACTOR")=="scientistsunite";
        return actions && (hosted || dedicated);
    }
}
