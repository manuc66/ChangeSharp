namespace ChangeSharp.Tests;

public class GetNextSemanticVersionTests
{
    [SetUp]
    public void Setup()
    {
    }

    [Test]
    public void ItComputesAMinorFromChanged()
    {
        string changeset = @"
        ### Changed
        - Improved existing feature 1.

        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.1.0"));
    }

    [Test]
    public void ItComputesAMajorFromBreakingChanges()
    {
        string changeset = @"
        ### Breaking Changes
        - Improved existing feature 1.

        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void ItComputeAMinorFromAdded()
    {
        string changeset = @"
        ### Added
        - New feature 1.
        - New feature 2.

        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.1.0"));
    }

    [Test]
    public void ItComputesAMajorFromRemoved()
    {
        string changeset = @"

        ### Removed
        - Removed feature 2.
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("2.0.0"));
    }

    [Test]
    public void ItComputeAPatchFromOnlySecurity()
    {
        string changeset = @"

        ### Security
        - Security update 1.
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.0.1"));
    }

    [Test]
    public void ComputeVersion_InvalidCurrentVersion_Throws()
    {
        var changeSet = new ChangeSet();
        changeSet.Added.Add("Feature");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            NextVersionComputer.ComputeVersion("not-a-version", changeSet));
        Assert.That(ex.Message, Does.Contain("not-a-version"));
    }

    [Test]
    public void ComputeVersion_NoChanges_ReturnsCurrentVersion()
    {
        var changeSet = new ChangeSet();

        string nextVersion = NextVersionComputer.ComputeVersion("2.3.0", changeSet);

        Assert.That(nextVersion, Is.EqualTo("2.3.0"));
    }

    [Test]
    public void ComputeVersionWithWarning_InvalidVersion_ReturnsWarning()
    {
        var changeSet = new ChangeSet();
        changeSet.Added.Add("Feature");

        var (version, warning) = NextVersionComputer.ComputeVersionWithWarning("not-a-version", changeSet);

        Assert.That(version, Is.EqualTo("0.1.0"));
        Assert.That(warning, Is.Not.Null);
        Assert.That(warning, Does.Contain("not a valid SemVer"));
    }

    [Test]
    public void ItComputeAPatchFromOnlyFixes()
    {
        string changeset = @"
        ### Fixed
        - Bug fix 1.
        - Bug fix 2.
";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.0.1"));
    }

    [Test]
    public void ItComputeAMajorFromAFullChangeSet()
    {
        string changeset = @"
        ### Added
        - New feature 1.
        - New feature 2.

        ### Changed
        - Improved existing feature 1.

        ### Deprecated
        - Deprecated feature 1.

        ### Removed
        - Removed feature 2.

        ### Fixed
        - Bug fix 1.
        - Bug fix 2.

        ### Security
        - Security update 1.
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("2.0.0"));
    }


    [Test]
    public void ItComputeAMinorIfChangedANdRemovedAreEmpty()
    {
        string changeset = @"
        ### Added
        - New feature 1.
        - New feature 2.

        ### Changed

        ### Deprecated
        - Deprecated feature 1.

        ### Removed

        ### Fixed
        - Bug fix 1.
        - Bug fix 2.

        ### Security
        - Security update 1.
        
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.1.0"));
    }

    [Test]
    public void ItComputeAFixIfOnlyFixedAndSecurityAreFilled()
    {
        string changeset = @"
        ### Added

        ### Changed

        ### Deprecated

        ### Removed

        ### Fixed
        - Bug fix 1.
        - Bug fix 2.

        ### Security
        - Security update 1.
        
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.0.1"));
    }

    [Test]
    public void ItComputesAPatchIfOnlyFixedIsFilled()
    {
        string changeset = @"
        ### Added

        ### Changed

        ### Deprecated

        ### Removed

        ### Fixed
        - Bug fix 1.
        - Bug fix 2.

        ### Security
        
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.0.1"));
    }

    [Test]
    public void ItComputesAPatchIfOnlySecurityIsFilled()
    {
        string changeset = @"
        ### Added

        ### Changed

        ### Deprecated

        ### Removed

        ### Fixed

        ### Security
        - Security update 1.
        
        ";

        string nextVersion = NextVersionComputer.GetNextSemanticVersion(changeset, "1.0.0");

        Assert.That(nextVersion, Is.EqualTo("1.0.1"));
    }
}