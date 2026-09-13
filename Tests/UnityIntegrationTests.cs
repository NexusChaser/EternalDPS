using System;
using System.IO;
using System.Threading.Tasks;
using NexusChaser.EternalDPS.Abstractions;
using NexusChaser.EternalDPS.Keys;
using NexusChaser.EternalDPS.Unity;
using NUnit.Framework;
using UnityEngine;

namespace NexusChaser.EternalDPS.Tests
{
    /// <summary>
    /// Covers the Unity layer: that the driver assembles itself from a setup, that the runner writes
    /// when it should, and that the things that must fail loudly do.
    /// </summary>
    public class UnityIntegrationTests
    {
        private static readonly Guid ThisGame = new Guid("aaaaaaaa-0000-0000-0000-000000000001");

        private InMemoryStore _store;
        private EternalSaveRunner _runner;

        [SetUp]
        public void SetUp()
        {
            _store = new InMemoryStore();
        }

        [TearDown]
        public void TearDown()
        {
            if (_runner != null)
            {
                UnityEngine.Object.DestroyImmediate(_runner.gameObject);
                _runner = null;
            }
        }

        private static EternalUnitySetup Setup(IStore store)
        {
            return new EternalUnitySetup
            {
                Identity = new ProductIdentity(ThisGame),
                Keys = new EternalKeyRing(new EternalKeyMaterial(1, TestDoubles.KeyBytes(1))),
                Store = store,
                Serializer = new Utf8StringSerializer(),
            };
        }

        [Test]
        public async Task A_driver_assembled_from_a_setup_saves_and_loads()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            var key = EternalKey.AccountSettings("prefs");

            await driver.SaveAsync(key, "es");

            Assert.AreEqual("es", (await driver.LoadAsync<string>(key)).Value);
        }

        [Test]
        public async Task The_build_version_is_recorded_in_every_save()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            var key = EternalKey.AccountSettings("prefs");

            await driver.SaveAsync(key, "es");

            var metadata = (await driver.ReadMetadataAsync(key)).ValueOrThrow();

            Assert.AreEqual(Application.version, metadata.AppVersion);
        }

        [Test]
        public void A_setup_with_no_identity_is_refused()
        {
            // A save with no product identity cannot be recognised as ours after a rename, which is
            // the one thing the identity exists for.
            var setup = Setup(_store);
            setup.Identity = null;

            var error = Assert.Throws<ArgumentNullException>(() => EternalUnity.CreateDriver(setup));
            Assert.That(error.Message, Does.Contain("frozen"));
        }

        [Test]
        public void A_setup_that_signs_with_no_keys_is_refused()
        {
            var setup = Setup(_store);
            setup.Keys = null;

            var error = Assert.Throws<ArgumentNullException>(() => EternalUnity.CreateDriver(setup));
            Assert.That(error.Message, Does.Contain("public repository"));
        }

        [Test]
        public void The_data_root_is_the_engine_s_persistent_path()
        {
            Assert.AreEqual(Application.persistentDataPath, EternalPaths.DataRoot);
        }

        [Test]
        public void A_sub_folder_lands_under_the_data_root()
        {
            var store = EternalPaths.CreateFileStore("eternal-tests-subfolder");

            try
            {
                Assert.AreEqual(
                    Path.GetFullPath(Path.Combine(Application.persistentDataPath, "eternal-tests-subfolder")),
                    store.RootDirectory);
            }
            finally
            {
                if (Directory.Exists(store.RootDirectory))
                {
                    Directory.Delete(store.RootDirectory, recursive: true);
                }
            }
        }

        [Test]
        public void The_editor_does_not_claim_to_need_a_platform_store()
        {
            // Only WebGL does, because there a file store appears to work and loses everything when
            // the tab closes.
            Assert.IsFalse(EternalPaths.NeedsPlatformStore);
        }

        [Test]
        public async Task A_tracked_record_is_written_once_the_change_settles()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            var key = EternalKey.AccountSettings("prefs");
            var volume = 0.1f;

            _runner = EternalSaveRunner.Create(driver, quietSeconds: 0f);
            _runner.Track(key, () => volume.ToString("F2"));

            volume = 0.9f;
            _runner.MarkDirty(key);

            Assert.IsTrue(_runner.HasPendingChanges);

            await _runner.FlushAsync();

            Assert.IsFalse(_runner.HasPendingChanges);
            Assert.AreEqual("0.90", (await driver.LoadAsync<string>(key)).Value);
        }

        [Test]
        public async Task The_value_written_is_the_one_at_the_time_of_writing()
        {
            // What makes debouncing correct: a slider dragged through fifty values writes the
            // fiftieth, not the first one late.
            var driver = EternalUnity.CreateDriver(Setup(_store));
            var key = EternalKey.AccountSettings("prefs");
            var current = "first";

            _runner = EternalSaveRunner.Create(driver);
            _runner.Track(key, () => current);

            _runner.MarkDirty(key);
            current = "fiftieth";

            await _runner.FlushAsync();

            Assert.AreEqual("fiftieth", (await driver.LoadAsync<string>(key)).Value);
        }

        [Test]
        public void Marking_something_that_is_not_tracked_says_so()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            _runner = EternalSaveRunner.Create(driver);

            var error = Assert.Throws<InvalidOperationException>(
                () => _runner.MarkDirty(EternalKey.AccountSettings("prefs")));

            Assert.That(error.Message, Does.Contain("Call Track"));
        }

        [Test]
        public async Task A_forgotten_record_is_not_written()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            var key = EternalKey.AccountSettings("prefs");

            _runner = EternalSaveRunner.Create(driver);
            _runner.Track(key, () => "value");
            _runner.MarkDirty(key);
            _runner.Forget(key);

            await _runner.FlushAsync();

            Assert.IsFalse(await driver.ExistsAsync(key));
        }

        [Test]
        public async Task Flushing_with_nothing_pending_does_nothing()
        {
            var driver = EternalUnity.CreateDriver(Setup(_store));
            _runner = EternalSaveRunner.Create(driver);

            Assert.DoesNotThrowAsync(() => _runner.FlushAsync());
            Assert.AreEqual(0, (await _store.ListAsync(string.Empty)).Count);
        }

        [Test]
        public void The_runner_survives_a_scene_load()
        {
            // A save triggered by the application being paused during a scene change would otherwise
            // have nowhere left to run.
            var driver = EternalUnity.CreateDriver(Setup(_store));
            _runner = EternalSaveRunner.Create(driver);

            Assert.AreEqual(HideFlags.HideAndDontSave, _runner.gameObject.hideFlags);
        }

        [Test]
        public void The_runner_does_not_listen_for_the_quit_message()
        {
            // Deliberate and worth pinning down: Android kills the process without it and WebGL does
            // not call it when the tab closes, so a save system that leans on it works on the desktop
            // it was built on and loses data exactly where that is hardest to reproduce.
            var quit = typeof(EternalSaveRunner).GetMethod(
                "OnApplicationQuit",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic);

            Assert.IsNull(quit, "EternalSaveRunner must not rely on OnApplicationQuit.");

            foreach (var name in new[] { "OnApplicationPause", "OnApplicationFocus" })
            {
                Assert.IsNotNull(
                    typeof(EternalSaveRunner).GetMethod(
                        name,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic),
                    name + " is the reliable trigger and has to be there.");
            }
        }

        [Test]
        public void The_package_ships_a_link_xml_that_keeps_newtonsoft_reachable()
        {
            // Stripping cannot see reflection, so a build that serialises perfectly in the editor
            // throws on device. This is the file that prevents it.
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(EternalPackage).Assembly);

            Assert.IsNotNull(package);

            var linkXml = Path.Combine(package.resolvedPath, "Runtime", "link.xml");

            Assert.IsTrue(File.Exists(linkXml), "The package must ship a link.xml at " + linkXml);

            var contents = File.ReadAllText(linkXml);

            Assert.That(contents, Does.Contain("Newtonsoft.Json.JsonConvert"));
            Assert.That(contents, Does.Contain("DefaultContractResolver"));
        }
    }
}
