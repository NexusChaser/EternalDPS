# Changelog

Format based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

Semantic versioning applies, with one caveat: **the public API is considered unstable** until a
second project consumes the package. Until then, a minor release may break compilation.

The **file format** is not covered by that caveat. From version 1 of the container onwards it is
only ever extended, never changed.

## [Unreleased]

### Added

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
