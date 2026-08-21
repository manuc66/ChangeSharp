# ChangeSharp

> **Git commits are for developers. Changelogs are for users. ChangeSharp keeps them separate.**

**ChangeSharp** is a command-line tool (.NET Tool) and a C# library designed to aggregate independent changelog fragments (changesets) formatted according to the *Keep a Changelog* specification. It consolidates them into a global changelog file and automatically calculates the next release version.

---

## 📖 Documentation Index

### 💡 Core Concept
* [Strategic Positioning](#strategic-positioning-why-choose-changesharp)
* [SemVer Derivation Rules](SemVer%20Rules.md) - How fragments impact versioning.
* [Approval Gates](features/ApprovalGates.md) - Security and human-in-the-loop.
* [Migration Guide](Migration.md) - Moving from GitVersion/MinVer to ChangeSharp.

### 🛠️ Development & Roadmap
* [Codebase Analysis](Architecture.md) - Technical overview of the project.
* [Roadmap](Roadmap.md) - Completed and planned steps.
* [CI/CD Integration](features/CiIntegration.md) - Step 6 details and exit codes.

### ✨ Key Features
* [Pre-release & Branching](features/Prereleases.md) - Detailed spec for SemVer pre-releases.
* [Monorepo Support](features/MonorepoSupport.md) - Multi-team hierarchical configuration.

---

## 🚀 Strategic Positioning: Why Choose ChangeSharp?

In the .NET ecosystem, release versioning is heavily dominated by tools like **GitVersion** and **MinVer** (commit-driven) or general tools like **Changie** and **Release Please**.

ChangeSharp distinguishes itself as the **Changelog-driven versioning enterprise-ready for .NET with AI-native integration**:

*   **Natively Integrated in .NET**: First-class support for MSBuild, .NET Global Tools, and C# library usage.
*   **Safety Gates (The Differentiator)**: Unlike simple fragment managers, ChangeSharp includes **Syntactic Safety Gates** to cross-verify declared SemVer impact against actual API surface changes.
*   **Separation of Concerns**: Git history is for developers; changelogs are for users.
*   **Conflict-Free Workflows**: Independent Markdown fragments eliminate merge conflicts on `CHANGELOG.md`.
*   **AI-Native (MCP Layer)**: First tool to expose changelog management to AI agents via the **Model Context Protocol (MCP)**, with built-in security approval gates.
*   **Enterprise-Ready**: Support for complex monorepos, hierarchical configurations, and strict CI/CD validation.

#### Why This Matters
Unlike commit-driven tools, ChangeSharp ensures that version numbers are predictable and tied to documented user-facing value. It provides a level of automated trust (via Safety Gates) that neither simple fragment tools nor commit-parsers can offer today.
