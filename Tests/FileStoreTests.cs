using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Stores;
using NUnit.Framework;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the file store against a real temporary folder. An in-memory double cannot tell us
    /// anything about path handling or about what a half-finished write leaves behind.
    /// </summary>
    public class FileStoreTests
    {
        private string _root;
        private FileStore _store;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "eternal-tests", Guid.NewGuid().ToString("N"));
            _store = new FileStore(_root);
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
                // A leftover temporary folder is not worth failing a test run over.
            }
        }

        private static byte[] Bytes(string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        [Test]
        public void The_root_is_created_if_it_is_not_there()
        {
            Assert.IsTrue(Directory.Exists(_root));
            Assert.AreEqual(StoreCapabilities.All, _store.Capabilities);
        }

        [Test]
        public async Task A_record_round_trips_through_the_disk()
        {
            var key = EternalKey.SlotSession("save");

            Assert.IsFalse(await _store.ExistsAsync(key.RelativePath));

            await _store.WriteAsync(key.RelativePath, Bytes("board state"));

            Assert.IsTrue(await _store.ExistsAsync(key.RelativePath));
            Assert.AreEqual("board state", Encoding.UTF8.GetString(await _store.ReadAsync(key.RelativePath)));
        }

        [Test]
        public async Task Nested_folders_are_created_as_needed()
        {
            var key = EternalKey.SlotProgress("save", SlotId.New());

            await _store.WriteAsync(key.RelativePath, Bytes("x"));

            Assert.IsTrue(File.Exists(Path.Combine(_root, key.RelativePath.Replace('/', Path.DirectorySeparatorChar))));
        }

        [Test]
        public async Task Reading_something_that_is_not_there_returns_null()
        {
            Assert.IsNull(await _store.ReadAsync("account/missing.etm"));
        }

        [Test]
        public async Task Deleting_something_that_is_not_there_succeeds()
        {
            // Otherwise every caller would need a check-then-delete race.
            Assert.DoesNotThrowAsync(() => _store.DeleteAsync("account/missing.etm"));

            await _store.WriteAsync("account/prefs.etm", Bytes("es"));
            await _store.DeleteAsync("account/prefs.etm");

            Assert.IsFalse(await _store.ExistsAsync("account/prefs.etm"));
        }

        [Test]
        public async Task An_empty_record_is_a_record()
        {
            await _store.WriteAsync("account/prefs.etm", new byte[0]);

            Assert.IsTrue(await _store.ExistsAsync("account/prefs.etm"));
            Assert.AreEqual(0, (await _store.ReadAsync("account/prefs.etm")).Length);
        }

        [Test]
        public async Task A_write_leaves_no_temporary_file_behind()
        {
            await _store.WriteAsync("account/prefs.etm", Bytes("es"));

            Assert.AreEqual(0, Directory.GetFiles(_root, "*" + FileStore.PartialExtension, SearchOption.AllDirectories).Length);
        }

        [Test]
        public void The_temporary_extension_is_not_the_record_extension()
        {
            // Steam's Auto-Cloud is configured with a pattern, and the obvious pattern is *.etm.
            // A half-written temporary file matching it would be uploaded as if it were a save.
            Assert.AreNotEqual(EternalPackage.FileExtension, FileStore.PartialExtension);
            Assert.AreEqual(".part", FileStore.PartialExtension);
        }

        [Test]
        public async Task A_temporary_file_is_never_listed_as_a_record()
        {
            await _store.WriteAsync("account/prefs.etm", Bytes("es"));

            // Debris from a write that died partway.
            File.WriteAllBytes(Path.Combine(_root, "account", "progress.etm" + FileStore.PartialExtension), Bytes("half"));

            var listed = await _store.ListAsync(string.Empty);

            CollectionAssert.AreEqual(new[] { "account/prefs.etm" }, listed);
        }

        [Test]
        public async Task Debris_from_a_dead_write_can_be_swept_up()
        {
            File.WriteAllBytes(Path.Combine(_root, "a.etm" + FileStore.PartialExtension), Bytes("half"));
            File.WriteAllBytes(Path.Combine(_root, "b.etm" + FileStore.PartialExtension), Bytes("half"));
            await _store.WriteAsync("account/prefs.etm", Bytes("keep"));

            Assert.AreEqual(2, _store.CleanPartialFiles());
            Assert.IsTrue(await _store.ExistsAsync("account/prefs.etm"));
        }

        [Test]
        public async Task Overwriting_a_record_replaces_it_whole()
        {
            await _store.WriteAsync("account/prefs.etm", Bytes("a much longer first value"));
            await _store.WriteAsync("account/prefs.etm", Bytes("short"));

            // Writing in place would leave the tail of the old value behind.
            Assert.AreEqual("short", Encoding.UTF8.GetString(await _store.ReadAsync("account/prefs.etm")));
        }

        [Test]
        public async Task Listing_is_scoped_to_the_prefix_and_uses_forward_slashes()
        {
            var slot = SlotId.New();

            await _store.WriteAsync(EternalKey.MachineSettings("display").RelativePath, Bytes("x"));
            await _store.WriteAsync(EternalKey.AccountSettings("prefs").RelativePath, Bytes("x"));
            await _store.WriteAsync(EternalKey.SlotProgress("save", slot).RelativePath, Bytes("x"));

            var everything = await _store.ListAsync(string.Empty);
            var slots = await _store.ListAsync("slots/");

            Assert.AreEqual(3, everything.Count);
            Assert.AreEqual(1, slots.Count);

            // The same path has to name the same record on Windows, on Linux and in Steam Cloud.
            foreach (var path in everything)
            {
                Assert.That(path, Does.Not.Contain("\\"));
            }
        }

        [Test]
        public async Task Listing_an_empty_store_is_empty_rather_than_an_error()
        {
            Assert.AreEqual(0, (await _store.ListAsync(string.Empty)).Count);
        }

        [TestCase("../escape.etm", TestName = "Store path: traversal")]
        [TestCase("a/../../escape.etm", TestName = "Store path: traversal through a folder")]
        [TestCase("", TestName = "Store path: empty")]
        public void A_path_that_would_leave_the_root_is_refused(string path)
        {
            // EternalKey builds safe paths, but a path can also arrive from a file already on disk,
            // and that one has been through no validation at all.
            Assert.ThrowsAsync<ArgumentException>(() => _store.ReadAsync(path));
            Assert.ThrowsAsync<ArgumentException>(() => _store.WriteAsync(path, new byte[1]));
        }

        [Test]
        public void An_absolute_path_is_refused()
        {
            var absolute = Path.Combine(Path.GetTempPath(), "elsewhere.etm");

            Assert.ThrowsAsync<ArgumentException>(() => _store.WriteAsync(absolute, new byte[1]));
        }

        [Test]
        public void A_store_needs_a_root()
        {
            Assert.Throws<ArgumentException>(() => new FileStore(null));
            Assert.Throws<ArgumentException>(() => new FileStore(string.Empty));
        }

        [Test]
        public async Task A_driver_over_a_real_folder_saves_and_loads()
        {
            // The whole stack against a real disk, which is the first point at which this package
            // does something a game could use.
            var driver = new EternalDataDriver(new EternalDataDriverOptions
            {
                Store = _store,
                Serializer = new Utf8StringSerializer(),
                Keys = TestKeyProvider.WithSingleKey(),
                Identity = new ProductIdentity(new Guid("aaaaaaaa-0000-0000-0000-000000000001")),
            });

            var key = EternalKey.SlotSession("save");

            await driver.SaveAsync(key, "mid match");

            var loaded = await driver.LoadAsync<string>(key);

            Assert.IsTrue(loaded.IsOk, loaded.ToString());
            Assert.AreEqual("mid match", loaded.Value);

            // And what landed on disk is binary, not the text that went in.
            var raw = File.ReadAllBytes(Path.Combine(_root, "slots", "default", "save.etm"));
            Assert.That(Encoding.UTF8.GetString(raw), Does.Not.Contain("mid match"));
        }
    }
}
