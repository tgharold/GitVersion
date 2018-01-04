namespace GitVersionCore.Tests.VersionCalculation
{
    using System;
    using System.Collections.Generic;
    using GitTools;
    using GitTools.Testing;
    using GitVersion;
    using GitVersion.VersionCalculation;
    using LibGit2Sharp;
    using NUnit.Framework;
    using Shouldly;

    public class NextVersionCalculatorTests
    {
        [Test]
        public void ShouldIncrementVersionBasedOnConfig()
        {
            var baseCalculator = new TestBaseVersionCalculator(true, new SemanticVersion(1), new MockCommit());
            var semanticVersionBuildMetaData = new SemanticVersionBuildMetaData(1, "master", "b1a34e", DateTimeOffset.Now);
            var sut = new NextVersionCalculator(baseCalculator, new TestMetaDataCalculator(semanticVersionBuildMetaData));
            var config = new Config();
            var context = new GitVersionContextBuilder().WithConfig(config).Build();

            var version = sut.FindVersion(context);

            version.ToString().ShouldBe("1.0.1");
        }

        [Test]
        public void DoesNotIncrementWhenBaseVersionSaysNotTo()
        {
            var baseCalculator = new TestBaseVersionCalculator(false, new SemanticVersion(1), new MockCommit());
            var semanticVersionBuildMetaData = new SemanticVersionBuildMetaData(1, "master", "b1a34e", DateTimeOffset.Now);
            var sut = new NextVersionCalculator(baseCalculator, new TestMetaDataCalculator(semanticVersionBuildMetaData));
            var config = new Config();
            var context = new GitVersionContextBuilder().WithConfig(config).Build();

            var version = sut.FindVersion(context);

            version.ToString().ShouldBe("1.0.0");
        }

        [Test]
        public void AppliesBranchPreReleaseTag()
        {
            var baseCalculator = new TestBaseVersionCalculator(false, new SemanticVersion(1), new MockCommit());
            var semanticVersionBuildMetaData = new SemanticVersionBuildMetaData(2, "develop", "b1a34e", DateTimeOffset.Now);
            var sut = new NextVersionCalculator(baseCalculator, new TestMetaDataCalculator(semanticVersionBuildMetaData));
            var context = new GitVersionContextBuilder()
                .WithDevelopBranch()
                .Build();

            var version = sut.FindVersion(context);

            version.ToString("f").ShouldBe("1.0.0-alpha.1+2");
        }

        [Test]
        public void PreReleaseTagCanUseBranchName()
        {
            var config = new Config
            {
                NextVersion = "1.0.0",
                Branches = new Dictionary<string, BranchConfig>
                {
                    {
                        "custom", new BranchConfig
                        {
                            Regex = "custom/",
                            Tag = "useBranchName",
                            SourceBranches = new List<string>()
                        }
                    }
                }
            };

            using (var fixture = new EmptyRepositoryFixture())
            {
                fixture.MakeACommit();
                fixture.BranchTo("develop");
                fixture.MakeACommit();
                fixture.BranchTo("custom/foo");
                fixture.MakeACommit();

                fixture.AssertFullSemver(config, "1.0.0-foo.1+2");
            }
        }

        [Test]
        public void PreReleaseTagCanUseBranchNameVariable()
        {
            var config = new Config
            {
                NextVersion = "1.0.0",
                Branches = new Dictionary<string, BranchConfig>
                {
                    {
                        "custom", new BranchConfig
                        {
                            Regex = "custom/",
                            Tag = "alpha.{BranchName}",
                            SourceBranches = new List<string>()
                        }
                    }
                }
            };

            using (var fixture = new EmptyRepositoryFixture())
            {
                fixture.MakeACommit();
                fixture.BranchTo("develop");
                fixture.MakeACommit();
                fixture.BranchTo("custom/foo");
                fixture.MakeACommit();

                fixture.AssertFullSemver(config, "1.0.0-alpha.foo.1+2");
            }
        }

        private void CheckoutAndAssertFullSemver(EmptyRepositoryFixture fixture, string branch, Config config, string expectedFullSemver)
        {
            Commands.Checkout(fixture.Repository, branch);
            fixture.AssertFullSemver(config, expectedFullSemver);
        }

        private void MakeCommits(EmptyRepositoryFixture fixture, int numberOfCommits)
        {
            for (var i = 0; i < numberOfCommits; i++)
            {
                fixture.MakeACommit();
            }
        }

        [Test]
        public void Experiment_with_mainline_1()
        {
            var config = new Config
            {
                NextVersion = "0.1.0",
                VersioningMode = VersioningMode.Mainline
            }
            .ApplyDefaults();

            using (var fixture = new EmptyRepositoryFixture())
            {
                // Start with a blank repository, create a single commit into 'master' branch
                MakeCommits(fixture, 1);
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");

                // Create the 'development' branch, add a commit
                fixture.BranchTo("development");
                fixture.AssertFullSemver(config, "0.1.0-alpha.0");
                MakeCommits(fixture, 1);
                fixture.AssertFullSemver(config, "0.1.0-alpha.1");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "0.2.0");

                // Create a feature branch off of development, start making commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "0.1.0-alpha.1");

                fixture.BranchTo("issue1");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.1.2-issue1.2");
                MakeCommits(fixture, 1);
                fixture.AssertFullSemver(config, "0.1.2-issue1.3");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "0.1.0-alpha.1");
                fixture.MergeNoFF("issue1");
                fixture.AssertFullSemver(config, "0.1.0-alpha.5");

                // Merge 'development' into 'master'
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.2.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "0.3.0");
            }
        }

        [Test]
        public void Experiment_with_mainline_2_only_master_branch_increment_patch()
        {
            var config = new Config
            {
                NextVersion = "0.1.0",
                VersioningMode = VersioningMode.Mainline,
                Increment = IncrementStrategy.Patch,
            }
            .ApplyDefaults();

            using (var fixture = new EmptyRepositoryFixture())
            {
                // Start with a blank repository, create a single commit into 'master' branch
                fixture.MakeACommit();
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");

                // Create a feature branch off of master, start making commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");
                fixture.BranchTo("issue1");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.1.1-issue1.2");
                MakeCommits(fixture, 1);
                fixture.AssertFullSemver(config, "0.1.1-issue1.3");

                // Merge feature branch into master, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");
                fixture.MergeNoFF("issue1");
                fixture.AssertFullSemver(config, "0.1.1");

                // Create another feature branch off of master, start making commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.1");
                fixture.BranchTo("issue2a");
                MakeCommits(fixture, 3);
                fixture.AssertFullSemver(config, "0.1.2-issue2a.3");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.1.2-issue2a.5");

                // Start a parallel feature branch off of master, start making commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.1");
                fixture.BranchTo("issue2b");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.1.2-issue2b.2");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.1.2-issue2b.4");

                // Merge 2B branch, then merge 2A branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.1");
                fixture.MergeNoFF("issue2b");
                fixture.AssertFullSemver(config, "0.1.2");
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.2");
                fixture.MergeNoFF("issue2a");
                fixture.AssertFullSemver(config, "0.1.3");

                // Increment minor version with commit message
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.3");
                fixture.MakeACommit("+semver:minor");
                fixture.AssertFullSemver(config, "0.2.0");

                // Start a feature branch off of master, start making commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.2.0");
                fixture.BranchTo("issue3");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.2.1-issue3.2");
                MakeCommits(fixture, 2);
                fixture.AssertFullSemver(config, "0.2.1-issue3.4");

                // Merge feature branch into master, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.2.0");
                fixture.MergeNoFF("issue3");
                fixture.AssertFullSemver(config, "0.2.1");

                // Increment major version with commit message
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.2.1");
                fixture.MakeACommit("+semver:major");
                fixture.AssertFullSemver(config, "1.0.0");

                // Change version using a tag on the mainline branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.0");
                fixture.ApplyTag("2.0.0");
                fixture.AssertFullSemver(config, "2.0.0");
            }
        }

        [Test]
        public void Experiment_3_master_development_increments()
        {
            var config = new Config
            {
                VersioningMode = VersioningMode.Mainline,
                Increment = IncrementStrategy.Patch,
            }
            .ApplyDefaults();

            config.Branches[ConfigurationProvider.DevelopBranchKey].Increment = IncrementStrategy.Patch;

            using (var fixture = new EmptyRepositoryFixture())
            {
                // Start with a blank repository, create a single commit into 'master' branch
                fixture.MakeACommit();
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0");
                fixture.ApplyTag("1.0.0");
                fixture.AssertFullSemver(config, "1.0.0");

                // Create the 'development' branch
                fixture.BranchTo("development");
                fixture.AssertFullSemver(config, "1.0.0");

                // New feature branch
                fixture.BranchTo("issue1");
                fixture.AssertFullSemver("1.0.0");
                MakeCommits(fixture, 1);
                fixture.AssertFullSemver("1.1.0-issue1.1+1");
                MakeCommits(fixture, 3);
                fixture.AssertFullSemver("1.1.0-issue1.1+4");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.0");
                fixture.MergeNoFF("issue1");
                fixture.AssertFullSemver(config, "1.0.1-alpha.5");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.0.1");

                // Create another feature branch off of development, start making commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.1-alpha.5");
                fixture.BranchTo("issue2a");
                MakeCommits(fixture, 3);
                fixture.AssertFullSemver(config, "1.0.2-issue2a.3");
                MakeCommits(fixture, 5);
                fixture.AssertFullSemver(config, "1.0.2-issue2a.8");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.1-alpha.5");
                fixture.MergeNoFF("issue2a");
                fixture.AssertFullSemver(config, "1.0.1-alpha.14");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.1");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.0.2");

                // Merge master into development
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.1-alpha.14");
                fixture.MergeNoFF("master");
                fixture.AssertFullSemver(config, "1.0.1-alpha.17");

                // Create another feature branch off of development, start making commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.1-alpha.17");
                fixture.BranchTo("issue3c");
                MakeCommits(fixture, 3);
                fixture.AssertFullSemver(config, "1.0.4-issue3c.4");
                MakeCommits(fixture, 5);
                fixture.AssertFullSemver(config, "1.0.4-issue3c.9");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.1-alpha.17");
                fixture.MergeNoFF("issue3c");
                fixture.AssertFullSemver(config, "1.0.1-alpha.26");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.2");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.0.3");

                // Tag master again, merge to development
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.3");
                fixture.ApplyTag("1.1.0");
                fixture.AssertFullSemver(config, "1.1.0");

                // Merge master into development
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.1.1-alpha.0");
                fixture.MergeNoFF("master");
                fixture.AssertFullSemver(config, "1.1.1-alpha.1");

                // Create another feature branch off of development, start making commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.1.1-alpha.1");
                fixture.BranchTo("i4d");
                MakeCommits(fixture, 5);
                fixture.AssertFullSemver(config, "1.1.2-i4d.6");
                MakeCommits(fixture, 6);
                fixture.AssertFullSemver(config, "1.1.2-i4d.12");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.1.1-alpha.1");
                fixture.MergeNoFF("i4d");
                fixture.AssertFullSemver(config, "1.1.1-alpha.13");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.1.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.1.1");

                // Put a +semver:minor commit on development
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.1.1-alpha.13");
                fixture.MakeACommit("some other part of message +semver:minor with semver between");
                fixture.AssertFullSemver(config, "1.2.0-alpha.14");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.1.1");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.2.0");

                // Put a +semver:minor commit on development
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.2.0-alpha.14");
                fixture.MakeACommit("+semver:minor");
                fixture.AssertFullSemver(config, "1.2.0-alpha.15");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.2.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.3.0");
                fixture.ApplyTag("1.3.0");
                fixture.AssertFullSemver(config, "1.3.0");

                // Merge master into development
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.3.1-alpha.0");
                fixture.MergeNoFF("master");
                fixture.AssertFullSemver(config, "1.3.1-alpha.1");
            }
        }


        [Test]
        public void Experiment_4_just_the_defaults()
        {
            var config = new Config()
            .ApplyDefaults();

            using (var fixture = new EmptyRepositoryFixture())
            {
                // Start with a blank repository, create a single commit into 'master' branch
                fixture.MakeACommit();
                CheckoutAndAssertFullSemver(fixture, "master", config, "0.1.0+0");
                fixture.ApplyTag("1.0.0");
                fixture.AssertFullSemver(config, "1.0.0");

                // Create the 'development' branch
                fixture.BranchTo("development");
                fixture.AssertFullSemver(config, "1.0.0");

                // New feature branch
                fixture.BranchTo("issue1");
                fixture.AssertFullSemver("1.0.0");
                MakeCommits(fixture, 1);
                fixture.AssertFullSemver("1.1.0-issue1.1+1");
                MakeCommits(fixture, 3);
                fixture.AssertFullSemver("1.1.0-issue1.1+4");

                // Merge feature branch into development, without squashing commits
                CheckoutAndAssertFullSemver(fixture, "development", config, "1.0.0");
                fixture.MergeNoFF("issue1");
                fixture.AssertFullSemver(config, "1.1.0-alpha.5");

                // Merge development back into the master branch
                CheckoutAndAssertFullSemver(fixture, "master", config, "1.0.0");
                fixture.MergeNoFF("development");
                fixture.AssertFullSemver(config, "1.0.1+6");
            }
        }

        [Test]
        public void PreReleaseNumberShouldBeScopeToPreReleaseLabelInContinuousDelivery()
        {
            var config = new Config
            {
                VersioningMode = VersioningMode.ContinuousDelivery,
                Branches = new Dictionary<string, BranchConfig>
                {
                    {
                        "master", new BranchConfig()
                        {
                            Tag = "beta"
                        }
                    },
                }
            };

            using (var fixture = new EmptyRepositoryFixture())
            {
                fixture.Repository.MakeACommit();

                fixture.Repository.CreateBranch("feature/test");
                Commands.Checkout(fixture.Repository, "feature/test");
                fixture.Repository.MakeATaggedCommit("0.1.0-test.1");
                fixture.Repository.MakeACommit();

                fixture.AssertFullSemver(config, "0.1.0-test.2+2");

                Commands.Checkout(fixture.Repository, "master");
                fixture.Repository.Merge(fixture.Repository.FindBranch("feature/test"), Generate.SignatureNow());

                fixture.AssertFullSemver(config, "0.1.0-beta.1+2");
            }
        }
    }
}