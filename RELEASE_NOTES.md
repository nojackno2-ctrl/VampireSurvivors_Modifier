This release is a safe, backup-first save editor for Vampire Survivors on Windows x64. It refuses to write while the game is running, creates a backup before save changes, recalculates the save checksum, and writes UTF-8 without a BOM. The portable ZIP is self-contained and does not modify the game installation.

Trainer support is included only as disabled, unverified development code. Every bundled Trainer profile remains `verified: false`, so release-mode attachment fails closed. No Trainer feature or gameplay effect has been verified for this release.

The release assets are an audited self-contained `win-x64` portable ZIP and `SHA256SUMS`. The audit rejects reverse-engineering dumps, build artifacts, logs, process captures, game-derived files, personal saves/backups, and symbol files.
