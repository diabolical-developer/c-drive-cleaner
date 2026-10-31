# C Drive Cleaner

A small Windows app that shows what is filling up your C: drive and helps you free space safely.
It needs nothing installed.

## Download

1. Download **[C-Drive-Cleaner.zip](https://github.com/diabolical-developer/c-drive-cleaner/releases/latest/download/C-Drive-Cleaner.zip)**.
2. Right-click the zip, choose **Extract All**, then double-click `C Drive Cleaner.exe`.
3. If Windows shows "Windows protected your PC", click **More info**, then **Run anyway**.
   This appears because the app isn't digitally signed.

Works on Windows 10 and 11.

## Pages

- **Home** – free space at a glance, a one-click drive scan, and summaries of everything below.
- **Clean up** – temp files, Recycle Bin, Windows Update leftovers, crash dumps, thumbnail and
  browser/app caches, developer package caches. Each item says whether it's safe and how much it frees.
  Files used in the last 24 hours and files that are open are always skipped.
- **What's using space** – every folder on C:, biggest first. Double-click to look inside. Every row has a
  plain-English note ("Windows system files – don't delete", "Cache – safe to delete", …).
  Deleting only ever moves things to the Recycle Bin, and Windows/program folders are protected.
- **Installed apps** – all installed programs sorted by size, with search and an Uninstall button that
  runs the app's own uninstaller.

Click **Run as administrator** in the sidebar to include Windows' protected folders and clean the
Windows-level items.

## Building from source

Run `build.bat`. It uses the C# compiler that ships with Windows (.NET Framework 4.8).
Source is in `src/`.
