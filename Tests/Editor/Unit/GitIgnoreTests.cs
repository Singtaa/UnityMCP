using NUnit.Framework;
using System.IO;

namespace UnityMcp.Tests {
    [TestFixture]
    public class GitIgnoreTests {
        string _tempDir;
        string _gitignorePath;

        [SetUp]
        public void SetUp() {
            _tempDir = Path.Combine(Path.GetTempPath(), $"GitIgnoreTests_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _gitignorePath = Path.Combine(_tempDir, ".gitignore");
        }

        [TearDown]
        public void TearDown() {
            if (Directory.Exists(_tempDir)) {
                Directory.Delete(_tempDir, true);
            }
        }

        [Test]
        public void LoadFromRootGitIgnore_NoFile_ReturnsEmptyIgnore() {
            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsFalse(gitignore.IsIgnored("anyfile.txt", false));
        }

        [Test]
        public void IsIgnored_SimplePattern_MatchesFile() {
            File.WriteAllText(_gitignorePath, "*.meta\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("test.meta", false));
            Assert.IsTrue(gitignore.IsIgnored("folder/test.meta", false));
            Assert.IsFalse(gitignore.IsIgnored("test.cs", false));
        }

        [Test]
        public void IsIgnored_DirectoryPattern_MatchesOnlyDirectories() {
            File.WriteAllText(_gitignorePath, "Library/\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("Library", true));
            Assert.IsTrue(gitignore.IsIgnored("Library/", true));
            Assert.IsFalse(gitignore.IsIgnored("Library", false)); // File named Library
        }

        [Test]
        public void IsIgnored_NegationPattern_Unignores() {
            File.WriteAllText(_gitignorePath, "*.meta\n!important.meta\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("test.meta", false));
            Assert.IsFalse(gitignore.IsIgnored("important.meta", false));
        }

        [Test]
        public void IsIgnored_AnchoredPattern_MatchesFromRoot() {
            File.WriteAllText(_gitignorePath, "/Temp\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("Temp", true));
            Assert.IsFalse(gitignore.IsIgnored("SubFolder/Temp", true));
        }

        [Test]
        public void IsIgnored_DoubleStarPattern_MatchesAnyPath() {
            File.WriteAllText(_gitignorePath, "**/build/\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("build", true));
            Assert.IsTrue(gitignore.IsIgnored("project/build", true));
            Assert.IsTrue(gitignore.IsIgnored("deep/nested/build", true));
        }

        [Test]
        public void IsIgnored_CommentLines_AreIgnored() {
            File.WriteAllText(_gitignorePath, "# This is a comment\n*.meta\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("test.meta", false));
            Assert.IsFalse(gitignore.IsIgnored("# This is a comment", false));
        }

        [Test]
        public void IsIgnored_EmptyLines_AreIgnored() {
            File.WriteAllText(_gitignorePath, "*.meta\n\n*.log\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("test.meta", false));
            Assert.IsTrue(gitignore.IsIgnored("test.log", false));
        }

        [Test]
        public void IsIgnored_ParentIgnored_ChildAlsoIgnored() {
            File.WriteAllText(_gitignorePath, "Library/\n");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("Library", true));
            Assert.IsTrue(gitignore.IsIgnored("Library/ScriptAssemblies", true));
            Assert.IsTrue(gitignore.IsIgnored("Library/ScriptAssemblies/Assembly.dll", false));
        }

        [Test]
        public void IsIgnored_CommonUnityPatterns() {
            File.WriteAllText(_gitignorePath, @"
Library/
Temp/
Logs/
*.csproj
*.sln
*.meta
");

            var gitignore = GitIgnore.LoadFromRootGitIgnore(_tempDir);

            Assert.IsTrue(gitignore.IsIgnored("Library", true));
            Assert.IsTrue(gitignore.IsIgnored("Temp", true));
            Assert.IsTrue(gitignore.IsIgnored("Logs", true));
            Assert.IsTrue(gitignore.IsIgnored("Project.csproj", false));
            Assert.IsTrue(gitignore.IsIgnored("Project.sln", false));
            Assert.IsTrue(gitignore.IsIgnored("Script.cs.meta", false));

            Assert.IsFalse(gitignore.IsIgnored("Assets", true));
            Assert.IsFalse(gitignore.IsIgnored("Assets/Script.cs", false));
        }
    }
}
