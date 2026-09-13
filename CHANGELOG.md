# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Semantic versioning applies, with one caveat: **the public API is considered unstable** until a
second project consumes the package. Until then, a minor release may break compilation.

The **file format** is not covered by that caveat. From version 1 of the container onwards it is
only ever extended, never changed.

## [Unreleased]

### Added

- `NewtonsoftJsonSerializer` (`0x01`), also a document serializer so a tool can inspect a save
  without knowing the game's types. `TypeNameHandling` is forced off and cannot be turned back on:
  with it enabled the file itself names the .NET type to construct, which turns a save into a small
  program.
- `EternalTypeDiscriminatorConverter<T>`: polymorphism behind a closed list of names the game
  registers. A name that is not on the list is refused, and editing a save cannot add one.
- `FileStore`: an asynchronous store over a real folder. Writes go to a `.part` file and then take
  the record's place in one operation, so a write that dies partway never leaves half a save under
  the real name. The extension is deliberately not `.etm`, so a Steam Auto-Cloud pattern cannot
  upload a half-written file as if it were a save.
- Backup rotation driven by `SaveProfile.Backups`, through the store's own operations so every
  backend gets it. A record that does not verify is never promoted over a good backup.
- `SiblingAdoption`: recovers saves stranded in a neighbouring folder by a rename. Bounded to
  sibling folders, copies rather than moves, keeps the most recently played candidate, and leaves a
  marker so it runs once.
- `EternalKeyRing`: one active key plus every retired one, refusing a ring with two active keys, a
  repeated identifier, or an active key that is not the highest — rotation only moves forward.
- HKDF over SHA-256 (RFC 5869), verified against the known-answer vectors in the RFC's Appendix A.
  The derivation is frozen: changing it would invalidate every signature already written.
- Automatic re-signing. A save carrying a retired key is moved onto the current one without the
  player doing anything, and without a single byte of its contents changing. It verifies first, so
  a tampered file cannot be laundered into a validly signed one.
- Key retirement in four stages, with `Warn` now actually warning instead of behaving like
  `ReadOnly`.
- A startup check that refuses two records resolving to the same file, and a warning for scope and
  kind pairings the design does not account for.
- The `.etm` container: preamble, metadata block, transform chain and an HMAC-SHA256 signature over
  all of it, so that stripping a transform from the header cannot get past verification.
- Metadata in a fixed built-in encoding, deliberately independent of the pluggable serializer, so a
  save stays identifiable to a build that lacks the module that wrote it — and readable while its
  body is corrupt.
- `IKeyProvider` and key material with a 32-byte floor, four retirement states, and a loud failure
  when asked to sign without a key. The package ships no key and never will.
- `DeflateTransform` (`0x01`), plus transform and serializer registries that refuse to hand one
  identifier to two implementations.
- `ProductIdentity` and `Adoption.CanAdopt`, so a save survives the product being renamed.
- `EternalDataDriver`: save, load, delete, exists, verify and read-metadata, turning every failure
  into a verdict and applying the profile's policy, including recovery from a backup.
- Core model: `Scope`, `RecordKind`, `SlotId`, `EternalKey` with path composition and record id
  validation, `SaveProfile` with its four presets, and the advisory `ScopeRules` matrix.
- Load outcomes as values rather than exceptions: `LoadStatus` with its ten verdicts,
  `LoadResult<T>`, and `IntegrityReport` carrying the failing region and the sizes that were read.
- Core interfaces: `ISerializer`, `IDocumentSerializer` over the `EternalNode` tree,
  `IByteTransform`, `IClock`, `IEternalLog` and the asynchronous `IStore` with
  `StoreCapabilities`.
- Package scaffolding: `package.json`, folder layout and the seven assemblies (`Core`, `Tooling`,
  `Serialization.Newtonsoft`, `Crypto`, `Unity`, `Unity.Editor`, `Tests`).
- `Core` and `Tooling` declared without engine references (`noEngineReferences`).
- `Serialization.Newtonsoft` behind a version define: it is not compiled at all when the
  Newtonsoft.Json package is absent.
- Architecture and implementation plan documents under `Documentation~/`.

### Changed

- License moved from MIT to Apache-2.0. MIT does not require stating modifications nor protect the
  project name; Apache-2.0 does both, and adds an explicit patent grant.

## [0.1.0] - unreleased

Phase 0 of the implementation plan: scaffolding. **No functional code yet.**
