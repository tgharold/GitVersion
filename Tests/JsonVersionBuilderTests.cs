using ApprovalTests;
using GitFlowVersion;
using NUnit.Framework;

[TestFixture]
public class JsonVersionBuilderTests
{
    [Test]
    public void Json()
    {
        var semanticVersion = new VersionAndBranch
                              {
                                  BranchType = BranchType.Feature,
                                  BranchName = "feature1",
                                  Sha = "a682956dc1a2752aa24597a0f5cd939f93614509",
                                  Version = new SemanticVersion
                                                       {
                                                           Major = 1,
                                                           Minor = 2,
                                                           Patch = 3,
                                                           Tag = "unstable4",
                                                           Suffix = "a682956d",
                                                       }
                              };
        var dictionary = semanticVersion.GetVariables();
        var json = JsonOutputFormatter.ToJson(dictionary);
        Approvals.Verify(json);
    }

}