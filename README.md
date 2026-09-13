# EternalDPS <!-- omit in toc -->

**Eternal Data Persistence System** — a reusable save system for Unity, built around an
engine-independent core.

[![](https://img.shields.io/github/v/tag/NexusChaser/EternalDPS?label=version)](https://github.com/NexusChaser/EternalDPS/tags)
[![](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](LICENSE)
![](https://img.shields.io/badge/Unity-2022.3+-57b9d3.svg?style=flat&logo=unity)
![](https://img.shields.io/badge/status-early%20development-orange.svg)

<< [📝 Description](#-description) | [📌 Key Features](#-key-features) | [⚙ Installation](#-installation) | [🏗 Architecture](#-architecture) | [📄 File Format](#-file-format) | [🗺 Roadmap](#-roadmap) | [📜 License](#-license) >>

---

> [!WARNING]
> **This package is under early development and is not ready for production use.**
>
> The core, the JSON adapter and the file store are done and covered by 206 tests, so the package
> saves and loads against a real folder. What is missing is the Unity integration — path
> resolution, startup and save triggers — so it is not yet wired into a game. The public API is unstable and
> will keep changing until a second project consumes the package. The **file format**, once version
> 1 of the container ships, is a different matter: it will only ever be extended, never changed.

## 📝 Description

A save system has three parts that age at different rates: **where the bytes are written**, **how
an object becomes bytes**, and **what is inside**. EternalDPS owns the first two and deliberately
leaves the third to your game, because that is the only part nobody can reuse.

The core has **no reference to `UnityEngine`**. That is not a style choice — it is what lets the
test suite run in CI without opening Unity, and what would let another engine use the same core by
supplying its own storage backend and serializer.

## 📌 Key Features

- **Signed container (`.etm`)** — HMAC-SHA256 over the whole file, header included, so tampering
  and corruption are both detected.
- **Metadata readable without opening the body** — list six save slots with level and playtime
  without deserializing six full saves, and keep a damaged save visible so the player can delete
  it.
- **Swappable serialization** — JSON today, binary later, without breaking files already written.
  The container records which serializer produced the body.
- **Pluggable byte transforms** — compression and encryption are ordered, self-describing steps.
  Files written before a transform existed still load after you add it.
- **Async by design** — WebGL flushes to IndexedDB asynchronously and console save APIs are async
  too. A synchronous API could never be correct on those platforms.
- **Scopes and slots** — machine / account / playthrough, with slots optional. A game that needs
  one save never learns that slots exist.
- **Engine-agnostic tooling** — inspection, editing and repacking live outside Unity, so every
  engine can build its own visual layer on the same public API.

## ⚙ Installation

### Unity Package Manager (Git URL)

1. Open **Window → Package Manager**
2. Click **+** → **Add package from git URL…**
3. Enter the URL:

```
https://github.com/NexusChaser/EternalDPS.git
```

Or add it to `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.nexuschaser.eternaldps": "https://github.com/NexusChaser/EternalDPS.git"
  }
}
```

To pin a version, append the tag: `...EternalDPS.git#v0.1.0`

### Running the package tests

Package tests only compile when the consuming project opts in. Add the package to `testables`:

```json
{
  "testables": ["com.nexuschaser.eternaldps"]
}
```

### Working on the package itself

A package installed from a Git URL lives in `Library/PackageCache` and is **read-only**. To develop
against a local clone, point the manifest at it instead:

```json
"com.nexuschaser.eternaldps": "file:../../EternalDPS"
```

The path is relative to the project's `Packages/` folder.

## 🏗 Architecture

| Folder | Assembly | Depends on Unity |
| --- | --- | --- |
| `Runtime/Core` | `Eternal.Core` | **no** |
| `Runtime/Tooling` | `Eternal.Tooling` | **no** |
| `Runtime/Serialization.Newtonsoft` | `Eternal.Serialization.Newtonsoft` | **no** |
| `Runtime/Crypto` | `Eternal.Crypto` | **no** |
| `Runtime/Unity` | `Eternal.Unity` | yes |
| `Editor` | `Eternal.Unity.Editor` | editor only |
| `Tests` | `Eternal.Tests` | editor only |

Optional assemblies sit behind **version defines**: if the package they depend on is not installed,
they are not compiled at all. A project that does not want Newtonsoft.Json never pulls it in.

## 📄 File Format

Saves are written as `.etm` files:

```
PREAMBLE   magic "ETM1" · container version · flags · lengths
           serializer id · key id · ordered transform ids
METADATA   product id · schema version · timestamp · app version · slot name · …
BODY       serialized, then passed through the transform chain
SIGNATURE  HMAC-SHA256 over everything above
```

Three properties fall out of that layout:

- The **metadata block is not encrypted and sits outside the body**, so a save can be identified
  and listed without decrypting anything — and stays readable when the body is damaged.
- The **signature covers the preamble**, not just the payload. Editing the transform list to claim
  "no encryption" invalidates the file.
- The **serializer id** means a build that writes binary keeps reading JSON files written by
  earlier versions.

### Keys

**This repository contains no keys, and it never will.** It is public: any key shipped here would
be a published key, and the signature would protect nothing.

Each game supplies its own key material through `IKeyProvider`, from its own private repository or
a CI secret. What the package provides is the **generator** and the derivation — randomness is not
something each project should improvise.

Keys are versioned rather than permanent. The container records which key signed a file, so a
leaked key can be rotated out in a patch while existing saves keep loading and silently re-sign
themselves with the new one.

> [!NOTE]
> Local save protection is obfuscation, not security. The key lives in the shipped binary, so a
> determined attacker will extract it. What this design buys is that a *published* key stops being
> useful after the next patch, and that corruption is always detected.

## 🗺 Roadmap

| Phase | | Status |
| --- | --- | --- |
| 0 | Package scaffolding | ✅ done |
| 1 | Core: container and transform pipeline | ✅ done |
| 2 | Newtonsoft adapter and file store | 🟨 in progress |
| 3 | Test suite, golden files, store conformance | ⬜ |
| 4 | WebGL store | ⬜ |
| 5 | Engine-agnostic tooling | ⬜ |
| 6 | Unity editor windows | ⬜ |
| 7 | Slots and catalog | ⬜ |
| 8 | AES transform | ⬜ |
| 9 | CLI | ⬜ |
| 10 | Steam Cloud (Auto-Cloud and Cloud API) | ⬜ |
| 11 | Binary serializer | ⬜ |

## 🤝 Contributing

Issues and pull requests are welcome. Two rules matter more than the rest:

1. **`Eternal.Core` and `Eternal.Tooling` must never reference `UnityEngine`.** If the CLI stops
   compiling, that rule was broken.
2. **Identifiers are never reused** — not transform ids, not serializer ids, not key ids. Saves
   already written in the wild depend on them.

## 📜 License

Licensed under the **Apache License, Version 2.0**. See [LICENSE](LICENSE) and [NOTICE](NOTICE).

You may use and modify it in commercial and non-commercial projects. In exchange, the license
requires that attribution travels with the code (§4c, §4d), that modified files say they were
modified (§4b), and that the project and author names are not used to endorse derived work (§6).

## Author

**Nexus Chaser** — [@NexusChaser](https://github.com/NexusChaser)
